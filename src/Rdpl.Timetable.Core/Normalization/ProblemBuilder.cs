using System.Globalization;
using System.Text.RegularExpressions;
using Rdpl.Timetable.Configuration;
using Rdpl.Timetable.Data;
using Rdpl.Timetable.Models;

namespace Rdpl.Timetable.Normalization;

public sealed record NormalizationStats(
    int SectionsTotal,
    int SectionsInScope,
    int AssignmentRows,
    int PlaceholderRows,
    int ZeroWorkloadRows,
    int OrphanRows,
    int UnresolvedSectionRows,
    int RowsUsed,
    int SubjectItemsStaffed,
    int SubjectItemsUnstaffed,
    int ActivityItems,
    int AliasMatches,
    int AmbiguousItems);

public sealed record NormalizationResult(
    SchedulingProblem Problem,
    IReadOnlyList<DataConflict> Conflicts,
    NormalizationStats Stats,
    IReadOnlyList<Section> ExcludedSections);

/// <summary>
/// Turns the raw dataset into the normalized <see cref="SchedulingProblem"/>:
///  * applies the curriculum of each grade to every section of that grade,
///  * resolves the formally assigned teacher per section-subject (T2) — never inferring from other fields,
///  * excludes UNASSIGNED-TT (DATA-1/T4) and zero-workload rows (H10) and reports every one of them,
///  * computes the allowed (day, period) cells per item (H2, H6, H9, G1).
/// </summary>
public static partial class ProblemBuilder
{
    private enum MatchKind
    {
        Exact = 0,
        Alias = 1,
        ActivityOption = 2,
    }

    private sealed record RowMatch(AssignmentRow Row, Section Section, CurriculumItem Item, MatchKind Kind, bool Usable);

    [GeneratedRegex(@"(\d{1,2})", RegexOptions.CultureInvariant)]
    private static partial Regex DigitsPattern();

    public static NormalizationResult Build(LoadedDataset dataset, RuleOptions rules)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(rules);
        var conflicts = new List<DataConflict>();
        var bell = dataset.Bell;
        var weeklySlots = bell.WeeklyTeachingSlots;

        // ---- 1. Section scope (P4/P5/P6) -------------------------------------------------------------
        var inScope = new List<Section>();
        var excluded = new List<Section>();
        foreach (var section in dataset.Sections.OrderBy(s => s.ClassNumber ?? 0).ThenBy(s => s.SectionOrder).ThenBy(s => s.Id, StringComparer.Ordinal))
        {
            if (section.IsPrePrimary && rules.ExcludePrePrimary)
            {
                excluded.Add(section);
                conflicts.Add(new DataConflict(DataConflictTypes.SectionExcludedPrePrimary, ConflictSeverity.Warning,
                    "Pre-primary section excluded (engine rule P4). CLASS_WISE_SUBJECTS.md also has no curriculum for it and no assignments exist.",
                    section.DisplayName, RuleId: "P4"));
                continue;
            }

            if (!dataset.Curricula.TryGetValue(section.CurriculumKey, out var curriculum))
            {
                excluded.Add(section);
                conflicts.Add(new DataConflict(DataConflictTypes.SectionExcludedNoCurriculum, ConflictSeverity.Error,
                    $"No curriculum for '{section.CurriculumKey}' in CLASS_WISE_SUBJECTS.md; section cannot be scheduled.", section.DisplayName, RuleId: "P3"));
                continue;
            }

            var positive = curriculum.Items.Where(i => i.PeriodsPerWeek > 0).Sum(i => i.PeriodsPerWeek);
            if (curriculum.DeclaredTotalPeriodsPerWeek is not null && curriculum.DeclaredTotalPeriodsPerWeek != curriculum.ComputedTotal)
            {
                conflicts.Add(new DataConflict(DataConflictTypes.CurriculumTotalMismatch, ConflictSeverity.Warning,
                    $"{curriculum.CurriculumKey}: document declares {curriculum.DeclaredTotalPeriodsPerWeek} periods/week but items add up to {curriculum.ComputedTotal}.",
                    section.DisplayName, RuleId: "H8"));
            }

            if (positive != weeklySlots)
            {
                excluded.Add(section);
                conflicts.Add(new DataConflict(DataConflictTypes.CurriculumTotalMismatch, ConflictSeverity.Error,
                    $"{curriculum.CurriculumKey} needs {positive} periods/week but the bell schedule has {weeklySlots} teaching slots; H3 and H8 cannot both hold, section excluded.",
                    section.DisplayName, RuleId: "H3/H8"));
                continue;
            }

            inScope.Add(section);
        }

        foreach (var key in dataset.Curricula.Values.SelectMany(c => c.Items).Where(i => i.PeriodsPerWeek <= 0))
        {
            conflicts.Add(new DataConflict(DataConflictTypes.ZeroCurriculumItem, ConflictSeverity.Warning,
                $"{key.CurriculumKey} item '{key.Name}' has periodsPerWeek <= 0 and is excluded (H10).", Subject: key.Name, RuleId: "H10"));
        }

        // ---- 2. Assignment rows → (section, curriculum item) matches -----------------------------------
        var sectionIndex = BuildSectionIndex(dataset.Sections);
        var matches = new List<RowMatch>();
        int placeholderRows = 0, zeroRows = 0, orphanRows = 0, unresolvedRows = 0, aliasMatches = 0;
        var aliases = rules.ApplySubjectAliases ? dataset.Aliases : Array.Empty<SubjectAlias>();
        var excludedIds = excluded.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);

        // Explicit placeholder rows per (section, subject). An alias is OUR assumption, so it must never override an
        // explicit statement in the client data that a specific item still "NEEDS_TEACHER_ASSIGNMENT".
        var explicitPlaceholders = new HashSet<(string SectionId, string Subject)>();
        foreach (var row in dataset.AssignmentRows.Where(r => rules.PlaceholderTeacherCodes.Contains(r.TeacherCode)))
        {
            if (ResolveSection(row, sectionIndex) is { } placeholderSection)
            {
                explicitPlaceholders.Add((placeholderSection.Id, SubjectNames.Normalize(row.SubjectName)));
            }
        }

        foreach (var row in dataset.AssignmentRows)
        {
            var source = $"{row.SourceFile}/{row.Sheet} row {row.RowNumber}";
            var section = ResolveSection(row, sectionIndex);
            var isPlaceholder = rules.PlaceholderTeacherCodes.Contains(row.TeacherCode);
            if (isPlaceholder)
            {
                placeholderRows++;
                conflicts.Add(new DataConflict(DataConflictTypes.UnassignedTeacher, ConflictSeverity.Warning,
                    "Assignment row uses UNASSIGNED-TT — cannot schedule until teacher assigned.",
                    section?.DisplayName ?? row.SectionText, row.SubjectName, row.TeacherCode, source, "DATA-1/T4"));
            }

            var isZero = !isPlaceholder && row.WorkloadPerWeek == 0;
            if (isZero)
            {
                zeroRows++;
                conflicts.Add(new DataConflict(DataConflictTypes.ZeroWorkload,
                    rules.ZeroWorkloadPolicy == ZeroWorkloadPolicy.Exclude ? ConflictSeverity.Warning : ConflictSeverity.Info,
                    rules.ZeroWorkloadPolicy == ZeroWorkloadPolicy.Exclude
                        ? "workload_per_week = 0 — assignment excluded (H10). The teacher is NOT used for this section-subject."
                        : "workload_per_week = 0 — kept as the formal teacher (ZeroWorkloadPolicy=TreatAsAssigned); demand taken from the curriculum.",
                    section?.DisplayName ?? row.SectionText, row.SubjectName, row.TeacherCode, source, "H10"));
            }

            if (section is null)
            {
                unresolvedRows++;
                conflicts.Add(new DataConflict(DataConflictTypes.SectionNotFound, ConflictSeverity.Warning,
                    $"Section '{row.SectionText}' (class '{row.ClassText}') does not match any section in sections.json; row ignored.",
                    row.SectionText, row.SubjectName, row.TeacherCode, source));
                continue;
            }

            if (excludedIds.Contains(section.Id))
            {
                continue; // Section-level exclusion already reported once.
            }

            var usable = !isPlaceholder && !(isZero && rules.ZeroWorkloadPolicy == ZeroWorkloadPolicy.Exclude);
            var curriculum = dataset.Curricula[section.CurriculumKey];
            var found = MatchItems(row.SubjectName, curriculum, aliases);
            if (!isPlaceholder && found.Count > 0 && found.All(f => f.Kind == MatchKind.Alias))
            {
                var superseded = found.Where(f => explicitPlaceholders.Contains((section.Id, SubjectNames.Normalize(f.Item.Name)))).ToList();
                if (superseded.Count > 0)
                {
                    conflicts.Add(new DataConflict(DataConflictTypes.AliasSupersededByPlaceholder, ConflictSeverity.Warning,
                        $"'{row.SubjectName}' would map to {string.Join(" + ", superseded.Select(f => f.Item.Name))} only through an assumed alias, but the client data explicitly marks {(superseded.Count == 1 ? "that item" : "those items")} UNASSIGNED-TT for this section. Explicit data wins over an assumption: the alias is not applied there. Confirm which item(s) this teacher really teaches.",
                        section.DisplayName, row.SubjectName, row.TeacherCode, source, "T2/DATA-1"));
                    found = found.Except(superseded).ToList();
                    if (found.Count == 0)
                    {
                        continue;
                    }
                }
            }

            if (found.Count == 0)
            {
                orphanRows++;
                conflicts.Add(new DataConflict(DataConflictTypes.OrphanAssignment, ConflictSeverity.Warning,
                    $"'{row.SubjectName}' is not in the {section.CurriculumKey} curriculum (CLASS_WISE_SUBJECTS.md); row not schedulable.",
                    section.DisplayName, row.SubjectName, row.TeacherCode, source));
                continue;
            }

            foreach (var (item, kind) in found)
            {
                matches.Add(new RowMatch(row, section, item, kind, usable));
            }
        }

        // ---- 3. Resolve one formal teacher per (section, item) -----------------------------------------
        var availability = dataset.Availability.ToDictionary(a => a.TeacherCode, StringComparer.OrdinalIgnoreCase);
        var items = new List<LessonItem>();
        int staffed = 0, unstaffed = 0, activities = 0, ambiguous = 0;
        var usedRows = new HashSet<AssignmentRow>();
        var overridesUsed = new HashSet<object>();

        var matchesBySection = matches.GroupBy(m => m.Section.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        foreach (var section in inScope)
        {
            var curriculum = dataset.Curricula[section.CurriculumKey];
            foreach (var curriculumItem in curriculum.Items.Where(i => i.PeriodsPerWeek > 0))
            {
                var itemMatches = matchesBySection.TryGetValue(section.Id, out var sectionMatches)
                    ? sectionMatches.Where(m => ReferenceEquals(m.Item, curriculumItem)).ToList()
                    : new List<RowMatch>();
                var usableMatches = itemMatches.Where(m => m.Usable).OrderBy(m => m.Kind).ThenByDescending(m => m.Row.WorkloadPerWeek ?? 0).ThenBy(m => m.Row.TeacherCode, StringComparer.Ordinal).ToList();

                LessonKind kind;
                string? teacherCode = null, teacherName = null, unstaffedReason = null;
                var instructors = new List<TeacherInfo>();

                if (curriculumItem.Type == CurriculumItemType.ActivityGroup)
                {
                    kind = LessonKind.Activity;
                    activities++;
                    instructors.AddRange(usableMatches.Select(m => new TeacherInfo(m.Row.TeacherCode, m.Row.TeacherName)).DistinctBy(t => t.Code));
                    usedRows.UnionWith(usableMatches.Select(m => m.Row));
                }
                else if (usableMatches.Count > 0)
                {
                    kind = LessonKind.Subject;
                    staffed++;
                    var best = usableMatches[0];
                    var sameRank = usableMatches.Where(m => m.Kind == best.Kind).Select(m => m.Row.TeacherCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    if (sameRank.Count > 1)
                    {
                        ambiguous++;
                        conflicts.Add(new DataConflict(DataConflictTypes.AmbiguousTeacher, ConflictSeverity.Warning,
                            $"{sameRank.Count} teachers are formally assigned to this section-subject ({string.Join(", ", sameRank)}). One lesson stream can have one teacher; chose {best.Row.TeacherCode} (highest workload, then code). Confirm with the school.",
                            section.DisplayName, curriculumItem.Name, best.Row.TeacherCode, RuleId: "T2"));
                    }

                    teacherCode = best.Row.TeacherCode;
                    teacherName = best.Row.TeacherName;
                    usedRows.Add(best.Row);

                    if (best.Kind == MatchKind.Alias)
                    {
                        aliasMatches++;
                        conflicts.Add(new DataConflict(DataConflictTypes.SubjectAliasApplied, ConflictSeverity.Info,
                            $"Assignment subject '{best.Row.SubjectName}' mapped to curriculum item '{curriculumItem.Name}' via config/subject-aliases.json (assumption — confirm).",
                            section.DisplayName, curriculumItem.Name, teacherCode));
                    }

                    if (itemMatches.Any(m => rules.PlaceholderTeacherCodes.Contains(m.Row.TeacherCode)))
                    {
                        conflicts.Add(new DataConflict(DataConflictTypes.PlaceholderSuperseded, ConflictSeverity.Info,
                            $"Both a real teacher ({teacherCode}) and an UNASSIGNED-TT placeholder exist for this section-subject; the real formal assignment is used, the placeholder is not scheduled.",
                            section.DisplayName, curriculumItem.Name, teacherCode, RuleId: "DATA-1"));
                    }

                    var siblingTargets = best.Kind == MatchKind.Alias
                        ? curriculum.Items.Where(i => sectionMatches!.Any(m => ReferenceEquals(m.Row, best.Row) && ReferenceEquals(m.Item, i))).Sum(i => i.PeriodsPerWeek)
                        : curriculumItem.PeriodsPerWeek;
                    if (best.Row.WorkloadPerWeek is int w && w > 0 && w != siblingTargets)
                    {
                        conflicts.Add(new DataConflict(DataConflictTypes.WorkloadMismatch, ConflictSeverity.Info,
                            $"Assignment workload_per_week = {w} but the curriculum requires {siblingTargets} periods/week; the curriculum value is scheduled (H8).",
                            section.DisplayName, curriculumItem.Name, teacherCode, $"{best.Row.SourceFile} row {best.Row.RowNumber}", "H8"));
                    }
                }
                else
                {
                    unstaffed++;
                    unstaffedReason = itemMatches.Any(m => rules.PlaceholderTeacherCodes.Contains(m.Row.TeacherCode))
                        ? "UNASSIGNED-TT placeholder"
                        : itemMatches.Any(m => m.Row.WorkloadPerWeek == 0)
                            ? "only zero-workload assignment rows (excluded by H10)"
                            : "no teaching_assignments row";
                    conflicts.Add(new DataConflict(DataConflictTypes.MissingAssignment, ConflictSeverity.Warning,
                        rules.UnstaffedPolicy == UnstaffedPolicy.ReserveSlot
                            ? $"No schedulable teacher ({unstaffedReason}). {curriculumItem.PeriodsPerWeek} periods/week are RESERVED (no lesson, no teacher) so the section week stays complete."
                            : $"No schedulable teacher ({unstaffedReason}). Item dropped (UnstaffedPolicy=Exclude); the section will have {curriculumItem.PeriodsPerWeek} free periods.",
                        section.DisplayName, curriculumItem.Name, RuleId: "T2/DATA-1"));
                    if (rules.UnstaffedPolicy == UnstaffedPolicy.Exclude)
                    {
                        continue;
                    }

                    kind = LessonKind.ReservedUnstaffed;
                }

                var allowed = ComputeAllowedSlots(section, curriculumItem, kind, teacherCode, bell, availability, dataset, overridesUsed);
                items.Add(new LessonItem(items.Count, section, curriculumItem.Name, curriculumItem.Type, kind,
                    curriculumItem.PeriodsPerWeek, curriculumItem.PeriodsPerDay, teacherCode, teacherName, instructors, unstaffedReason, allowed));
            }
        }

        foreach (var r in dataset.DayRestrictions.Where(r => !overridesUsed.Contains(r)))
        {
            conflicts.Add(new DataConflict(DataConflictTypes.UnknownOverride, ConflictSeverity.Warning,
                $"Day restriction for '{r.CurriculumKey} / {r.ItemName}' matches no curriculum item in scope; ignored.", Subject: r.ItemName, RuleId: "H6"));
        }

        foreach (var f in dataset.FixedSlots.Where(f => !overridesUsed.Contains(f)))
        {
            conflicts.Add(new DataConflict(DataConflictTypes.UnknownOverride, ConflictSeverity.Warning,
                $"Fixed-slot rule for '{f.CurriculumKey} / {f.ItemName}' matches no activity in scope; ignored.", Subject: f.ItemName, RuleId: "H9"));
        }

        if (items.Any(i => i.Kind == LessonKind.Activity))
        {
            conflicts.Add(new DataConflict(DataConflictTypes.ActivityInstructorNotBooked, ConflictSeverity.Info,
                "Activity-group instructors are not teacher-booked (engine rules T6/AC8: whole-school activities run simultaneously). Activity groups are scheduled per section with no teacher conflict constraint.",
                RuleId: "T6/AC8"));
        }

        // ---- 4. Teachers --------------------------------------------------------------------------------
        var teachers = new Dictionary<string, TeacherInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in dataset.AssignmentRows.Where(r => !rules.PlaceholderTeacherCodes.Contains(r.TeacherCode) && r.TeacherCode.Length > 0))
        {
            teachers.TryAdd(row.TeacherCode, new TeacherInfo(row.TeacherCode, row.TeacherName));
        }

        if (dataset.ClassLevel is not null)
        {
            foreach (var t in dataset.ClassLevel.Roster)
            {
                if (teachers.TryGetValue(t.Code, out var existing) && !string.Equals(SubjectNames.Normalize(existing.Name), SubjectNames.Normalize(t.Name), StringComparison.Ordinal))
                {
                    conflicts.Add(new DataConflict(DataConflictTypes.TeacherNameMismatch, ConflictSeverity.Info,
                        $"Teacher {t.Code} is '{existing.Name}' in the workbook but '{t.Name}' in the roster.", TeacherCode: t.Code));
                }
            }
        }

        var stats = new NormalizationStats(
            dataset.Sections.Count, inScope.Count, dataset.AssignmentRows.Count, placeholderRows, zeroRows, orphanRows, unresolvedRows,
            usedRows.Count, staffed, unstaffed, activities, aliasMatches, ambiguous);

        return new NormalizationResult(new SchedulingProblem(bell, inScope, items, teachers, rules), conflicts, stats, excluded);
    }

    private static List<(CurriculumItem Item, MatchKind Kind)> MatchItems(string subject, ClassCurriculum curriculum, IReadOnlyList<SubjectAlias> aliases)
    {
        var normalized = SubjectNames.Normalize(subject);
        var exact = curriculum.Items.Where(i => SubjectNames.Normalize(i.Name) == normalized).ToList();
        if (exact.Count > 0)
        {
            return exact.Select(i => (i, MatchKind.Exact)).ToList();
        }

        foreach (var alias in aliases.Where(a => SubjectNames.Normalize(a.AssignmentSubject) == normalized))
        {
            var targets = curriculum.Items.Where(i => alias.CurriculumItems.Any(t => SubjectNames.Normalize(t) == SubjectNames.Normalize(i.Name))).ToList();
            if (targets.Count > 0)
            {
                return targets.Select(i => (i, MatchKind.Alias)).ToList();
            }
        }

        // An activity row ("Karate", "Robotics", "Games") belongs to the activity group that lists it as an option.
        var groups = curriculum.Items.Where(i => i.Type == CurriculumItemType.ActivityGroup
            && SubjectNames.Options(i.Name).Any(o => SubjectNames.Normalize(o) == normalized)).ToList();
        return groups.Select(i => (i, MatchKind.ActivityOption)).ToList();
    }

    private static IReadOnlySet<(SchoolDay, int)> ComputeAllowedSlots(
        Section section,
        CurriculumItem item,
        LessonKind kind,
        string? teacherCode,
        BellSchedule bell,
        IReadOnlyDictionary<string, TeacherAvailability> availability,
        LoadedDataset dataset,
        HashSet<object> overridesUsed)
    {
        var nameKey = SubjectNames.Normalize(item.Name);
        var restriction = dataset.DayRestrictions.FirstOrDefault(r => string.Equals(r.CurriculumKey, section.CurriculumKey, StringComparison.OrdinalIgnoreCase) && SubjectNames.Normalize(r.ItemName) == nameKey);
        var fixedRule = kind == LessonKind.Activity
            ? dataset.FixedSlots.FirstOrDefault(f => string.Equals(f.CurriculumKey, section.CurriculumKey, StringComparison.OrdinalIgnoreCase) && SubjectNames.Normalize(f.ItemName) == nameKey)
            : null;
        if (restriction is not null)
        {
            overridesUsed.Add(restriction);
        }

        if (fixedRule is not null)
        {
            overridesUsed.Add(fixedRule);
        }

        TeacherAvailability? teacherAvailability = null;
        if (kind == LessonKind.Subject && teacherCode is not null)
        {
            availability.TryGetValue(teacherCode, out teacherAvailability);
        }

        var gamesOrLibrary = SubjectNames.HasToken(item.Name, "games") || SubjectNames.HasToken(item.Name, "library");
        var allowed = new HashSet<(SchoolDay, int)>();
        foreach (var day in bell.Days)
        {
            if (restriction is not null && !restriction.Days.Contains(day.Day))
            {
                continue; // H6
            }

            if (teacherAvailability is not null && teacherAvailability.OffDays.Contains(day.Day))
            {
                continue; // H2 off-day
            }

            var firstPair = day.SlotPairs.Count > 0 ? day.SlotPairs[0] : ((int, int)?)null;
            foreach (var period in day.PeriodNumbers)
            {
                if (teacherAvailability is not null && teacherAvailability.UnavailableSlots.Contains((day.Day, period)))
                {
                    continue; // H2 unavailable period
                }

                if (gamesOrLibrary && firstPair is { } fp && (period == fp.Item1 || period == fp.Item2))
                {
                    continue; // G1
                }

                if (fixedRule is not null && !fixedRule.Slots.Any(s => s.Day == day.Day && (s.Period is null || s.Period == period)))
                {
                    continue; // H9 / AC1 / AC2
                }

                allowed.Add((day.Day, period));
            }
        }

        return allowed;
    }

    // ---- Section resolution ---------------------------------------------------------------------------

    private sealed record SectionIndex(
        IReadOnlyDictionary<string, Section> ById,
        IReadOnlyDictionary<string, Section> ByDisplay,
        IReadOnlyDictionary<string, Section> ByClassAndName);

    private static SectionIndex BuildSectionIndex(IReadOnlyList<Section> sections)
    {
        var byId = sections.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        var byDisplay = new Dictionary<string, Section>(StringComparer.Ordinal);
        var byClassName = new Dictionary<string, Section>(StringComparer.Ordinal);
        foreach (var s in sections)
        {
            byDisplay.TryAdd(SubjectNames.Normalize(s.DisplayName), s);
            byClassName.TryAdd(ClassKey(s.CurriculumKey) + "|" + SubjectNames.Normalize(s.SectionName), s);
            byClassName.TryAdd(ClassKey(s.Grade) + "|" + SubjectNames.Normalize(s.SectionName), s);
        }

        return new SectionIndex(byId, byDisplay, byClassName);
    }

    private static Section? ResolveSection(AssignmentRow row, SectionIndex index)
    {
        var text = row.SectionText.Trim();
        if (text.Length > 0)
        {
            if (index.ById.TryGetValue(text, out var byId))
            {
                return byId;
            }

            if (index.ByDisplay.TryGetValue(SubjectNames.Normalize(text), out var byDisplay))
            {
                return byDisplay;
            }

            // "Class 10 - Lily", "Grade 10 Lily", "10-Lily" …
            var dash = text.LastIndexOf('-');
            if (dash > 0)
            {
                var key = ClassKey(text[..dash]) + "|" + SubjectNames.Normalize(text[(dash + 1)..]);
                if (index.ByClassAndName.TryGetValue(key, out var byParts))
                {
                    return byParts;
                }
            }
        }

        if (row.ClassText.Length > 0 && text.Length > 0)
        {
            var key = ClassKey(row.ClassText) + "|" + SubjectNames.Normalize(text);
            if (index.ByClassAndName.TryGetValue(key, out var byClass))
            {
                return byClass;
            }
        }

        return null;
    }

    /// <summary>"Class 10", "Grade 10", "10" → "class 10"; pre-primary names normalized as-is.</summary>
    private static string ClassKey(string text)
    {
        var digits = DigitsPattern().Match(text);
        return digits.Success
            ? "class " + int.Parse(digits.Groups[1].Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
            : SubjectNames.Normalize(text).Replace("-", " ", StringComparison.Ordinal);
    }
}
