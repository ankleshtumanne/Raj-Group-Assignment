using System.Globalization;
using Rdpl.Timetable.Configuration;
using Rdpl.Timetable.Data;
using Rdpl.Timetable.Models;
using Rdpl.Timetable.Normalization;
using Rdpl.Timetable.Solver;
using Rdpl.Timetable.Validation;

namespace Rdpl.Timetable.Reporting;

public static class ReportBuilder
{
    public static TimetableReport Build(
        LoadedDataset dataset,
        RuleOptions rules,
        SolverOptions solverOptions,
        NormalizationResult normalization,
        IReadOnlyList<DataConflict> dataConflicts,
        IReadOnlyList<SchedulingConflict> schedulingConflicts,
        SolveOutcome? strict,
        ValidationReport? strictValidation,
        SolveOutcome? diagnostic,
        ValidationReport? diagnosticValidation,
        string? skippedReason,
        long totalWallTimeMs)
    {
        var problem = normalization.Problem;
        var stats = normalization.Stats;
        var success = strict?.HasSolution == true && strictValidation?.HardConstraintsSatisfied == true;
        var outcome = skippedReason is not null && skippedReason.StartsWith("Data validation only", StringComparison.Ordinal) ? "DATA_VALIDATION_ONLY"
            : problem.Items.Count == 0 ? "NOTHING_TO_SCHEDULE"
            : success ? "TIMETABLE_GENERATED"
            : strict?.HasSolution == true ? "VALIDATION_FAILED"
            : strict?.Status == "UNKNOWN" ? "TIMEOUT_NO_SOLUTION"
            : "INFEASIBLE";

        var cells = strict?.HasSolution == true ? strict.Cells : Array.Empty<ScheduledCell>();
        var lessons = cells.Count(c => problem.Items[c.ItemIndex].Kind != LessonKind.ReservedUnstaffed);
        var reserved = cells.Count - lessons;
        var scheduledSections = cells.Select(c => problem.Items[c.ItemIndex].Section.Id).Distinct().Count();

        var missing = dataConflicts.Where(c => c.Type == DataConflictTypes.MissingInputFile).Select(c => c.Source ?? c.Message).Distinct().ToList();
        var dataSource = new DataSourceInfo(
            dataset.AssignmentsAreDummy ? "DUMMY_ASSIGNMENTS" : dataset.AssignmentsSource is null ? "NO_ASSIGNMENTS" : "CLIENT",
            dataset.AssignmentsSource,
            dataset.AssignmentsAreDummy
                ? "Teacher assignments are DUMMY data derived from the class-level TEACHER_CLASS_ASSIGNMENTS.md because TEACHER_CLASS_ALLOCATION.xlsx was not supplied. This is NOT the client's timetable."
                : null,
            dataset.FilesRead,
            missing);

        var solverInfo = strict is not null
            ? ToSolverInfo(strict, solverOptions, null, totalWallTimeMs)
            : new SolverInfo(TimetableEngine.EngineName, "NOT_RUN", totalWallTimeMs, null, solverOptions.TimeLimitSeconds, null, null, solverOptions.Workers, solverOptions.RandomSeed, skippedReason, null);

        var summary = new SummaryInfo(
            stats.SectionsTotal,
            stats.SectionsInScope,
            success ? scheduledSections : 0,
            normalization.ExcludedSections.Count,
            success ? lessons : 0,
            success ? reserved : 0,
            success ? cells.Count : 0,
            problem.Items.Where(i => i.BooksTeacher).Select(i => i.TeacherCode!).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            stats.AssignmentRows,
            stats.PlaceholderRows,
            stats.ZeroWorkloadRows,
            stats.OrphanRows,
            stats.UnresolvedSectionRows,
            stats.SubjectItemsStaffed,
            stats.SubjectItemsUnstaffed,
            stats.ActivityItems,
            dataConflicts.GroupBy(c => c.Type).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()));

        ValidationInfo? validation = strictValidation is null ? null : ToValidationInfo("strict solution", strictValidation, strict!, comparesObjective: true);

        DiagnosticInfo? diagnosticInfo = null;
        if (diagnostic is not null)
        {
            diagnosticInfo = new DiagnosticInfo(
                diagnostic.Status == "OPTIMAL"
                    ? "NOT A VALID TIMETABLE. The strict model is infeasible, so hard rules H1/L1/G2/H5 were turned into heavily penalised soft rules. Status OPTIMAL: the counts below are the proven MINIMUM number of violations. Use it to see exactly which teachers/sections/rules must change."
                    : $"NOT A VALID TIMETABLE. The strict model is infeasible, so hard rules H1/L1/G2/H5 were turned into heavily penalised soft rules. Status {diagnostic.Status}: the search stopped at the time limit, so the counts below are an UPPER BOUND, not a proven minimum — a rule listed here is not proven to be unavoidable (the blocking schedulingConflicts are the proof). Increase the time limit for a tighter diagnosis.",
                ToSolverInfo(diagnostic, solverOptions, null, diagnostic.WallTimeMs),
                diagnostic.Penalties.Where(p => p.IsRelaxation).Select(ToPenaltyDto).ToList(),
                diagnosticValidation is null ? null : ToValidationInfo("diagnostic (relaxed) solution", diagnosticValidation, diagnostic, comparesObjective: false),
                diagnostic.HasSolution ? BuildSectionTimetables(problem, diagnostic.Cells) : null);
        }

        return new TimetableReport
        {
            Success = success,
            Outcome = outcome,
            DataSource = dataSource,
            RulesApplied = new RulesAppliedInfo(
                rules.Profile, rules.BlockSinglesSoft, rules.L1SoftCurriculumKeys.OrderBy(k => k, StringComparer.Ordinal).ToList(),
                rules.G2CurriculumKeys.OrderBy(k => k, StringComparer.Ordinal).ToList(), rules.MaxConsecutiveSameSubject,
                rules.ZeroWorkloadPolicy.ToString(), rules.UnstaffedPolicy.ToString(), rules.ExcludePrePrimary, rules.ApplySubjectAliases, rules.EnableSoftObjective),
            Solver = solverInfo,
            Summary = summary,
            DataConflicts = dataConflicts.Select(c => new DataConflictDto(c.Type, c.Severity.ToString().ToUpperInvariant(), c.Message, c.Section, c.Subject, c.TeacherCode, c.Source, c.RuleId)).ToList(),
            SchedulingConflicts = schedulingConflicts.Select(c => new SchedulingConflictDto(c.Code, c.RuleId, c.Message, c.ProvesInfeasibility, c.Sections, c.TeacherCode)).ToList(),
            SectionTimetables = success ? BuildSectionTimetables(problem, cells) : null,
            TeacherTimetables = success ? BuildTeacherTimetables(problem, cells) : null,
            Validation = validation,
            Diagnostic = diagnosticInfo,
            RuleCoverage = RuleCatalog.Build(rules),
            Assumptions = Assumptions(dataset, rules),
        };
    }

    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<SectionLessonDto>>> BuildSectionTimetables(SchedulingProblem problem, IReadOnlyList<ScheduledCell> cells)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<SectionLessonDto>>>(StringComparer.Ordinal);
        foreach (var section in problem.Sections)
        {
            var days = new Dictionary<string, IReadOnlyList<SectionLessonDto>>(StringComparer.Ordinal);
            foreach (var day in problem.Bell.Days)
            {
                var lessons = new List<SectionLessonDto>();
                foreach (var period in day.Periods)
                {
                    var cell = cells.FirstOrDefault(c => c.Day == day.Day && c.Period == period.Period && problem.Items[c.ItemIndex].Section.Id == section.Id);
                    if (cell is null)
                    {
                        continue;
                    }

                    var item = problem.Items[cell.ItemIndex];
                    lessons.Add(new SectionLessonDto(
                        period.Period,
                        period.SlotLabel,
                        period.Start.ToString("HH:mm", CultureInfo.InvariantCulture),
                        period.End.ToString("HH:mm", CultureInfo.InvariantCulture),
                        item.Name,
                        item.Kind switch
                        {
                            LessonKind.Subject => "SUBJECT",
                            LessonKind.Activity => "ACTIVITY",
                            _ => "RESERVED_UNSTAFFED",
                        },
                        item.Kind == LessonKind.Subject ? item.TeacherCode : null,
                        item.Kind == LessonKind.Subject ? item.TeacherName : null,
                        item.Kind == LessonKind.Activity && item.Instructors.Count > 0 ? item.Instructors.Select(i => $"{i.Name} ({i.Code})").ToList() : null,
                        item.Kind == LessonKind.ReservedUnstaffed ? $"Reserved — no lesson until a teacher is assigned ({item.UnstaffedReason})." : null));
                }

                days[day.Day.ToKey()] = lessons;
            }

            result[section.DisplayName] = days;
        }

        return result;
    }

    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<TeacherLessonDto>>> BuildTeacherTimetables(SchedulingProblem problem, IReadOnlyList<ScheduledCell> cells)
    {
        var result = new SortedDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<TeacherLessonDto>>>(StringComparer.Ordinal);
        var booked = cells.Where(c => problem.Items[c.ItemIndex].BooksTeacher).GroupBy(c => problem.Items[c.ItemIndex].TeacherCode!, StringComparer.OrdinalIgnoreCase);
        var nameCounts = problem.Teachers.Values.GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        foreach (var teacher in booked)
        {
            var name = problem.Teachers.TryGetValue(teacher.Key, out var info) ? info.Name : teacher.Key;
            var key = nameCounts.TryGetValue(name, out var n) && n > 1 ? $"{name} ({teacher.Key})" : name;
            var days = new Dictionary<string, IReadOnlyList<TeacherLessonDto>>(StringComparer.Ordinal);
            foreach (var day in problem.Bell.Days)
            {
                var lessons = teacher.Where(c => c.Day == day.Day).OrderBy(c => c.Period)
                    .Select(c =>
                    {
                        var item = problem.Items[c.ItemIndex];
                        var slot = day.Periods.First(p => p.Period == c.Period).SlotLabel;
                        return new TeacherLessonDto(c.Period, slot, item.Section.DisplayName, item.Name);
                    })
                    .ToList();
                if (lessons.Count > 0)
                {
                    days[day.Day.ToKey()] = lessons;
                }
            }

            result[key] = days;
        }

        return result;
    }

    private static SolverInfo ToSolverInfo(SolveOutcome outcome, SolverOptions options, string? skipped, long wallMs) =>
        new(TimetableEngine.EngineName, outcome.Status, wallMs, Math.Round(outcome.SolverWallTimeSeconds, 3), outcome.TimeLimitSeconds,
            outcome.ObjectiveValue, outcome.BestObjectiveBound, options.Workers, options.RandomSeed, skipped,
            new ModelStatsDto(outcome.Statistics.Items, outcome.Statistics.CellVariables, outcome.Statistics.PairVariables, outcome.Statistics.SingleVariables,
                outcome.Statistics.TeacherSlotConstraints, outcome.Statistics.AuxiliaryVariables))
        {
            SolutionSource = outcome.HasSolution ? outcome.SolutionSource : null,
            WarmStart = outcome.WarmStart is { } w ? new WarmStartDto(w.Complete, w.SectionsSolved, w.SectionsWithoutSolution, w.WallTimeMs, w.Strategy) : null,
            StatusMeaning = StatusMeaning(outcome),
        };

    public static string StatusMeaning(SolveOutcome outcome) => outcome.Status switch
    {
        "OPTIMAL" => "A timetable was found and proven to have the lowest possible soft-constraint penalty.",
        "FEASIBLE" when outcome.SolutionSource == "WARM_START" => "The joint search returned no new solution within the time limit; the complete warm-start timetable (built with the same hard rules) is returned. Optimality is not proven.",
        "FEASIBLE" => "A timetable satisfying every hard constraint was found within the time limit; it is not proven optimal for the soft preferences (see bestObjectiveBound for the gap).",
        "INFEASIBLE" => "Proven: no timetable can satisfy all hard constraints with this data.",
        "UNKNOWN" => "The time limit was reached before any solution was found or infeasibility was proven.",
        _ => "The model was rejected by CP-SAT (modelling error).",
    };

    private static ValidationInfo ToValidationInfo(string scope, ValidationReport report, SolveOutcome outcome, bool comparesObjective)
    {
        bool? matches = comparesObjective && outcome.ObjectiveValue is double objective ? Math.Abs(objective - report.SoftPenalty) < 0.5 : null;
        return new ValidationInfo(
            scope,
            report.HardConstraintsSatisfied,
            report.HardViolations.Count,
            report.HardViolations.Take(500).Select(ToViolationDto).ToList(),
            report.RelaxedRuleViolations.Take(500).Select(ToViolationDto).ToList(),
            report.SoftPenalty,
            report.SoftPenaltyByRule,
            matches,
            outcome.Penalties.Where(p => !p.IsRelaxation || comparesObjective).Select(ToPenaltyDto).ToList(),
            report.ChecksPerformed);
    }

    private static ViolationDto ToViolationDto(Violation v) => new(v.RuleId, v.Message, v.Section, v.Day, v.Period);

    private static PenaltyDto ToPenaltyDto(PenaltySummary p) => new(p.RuleId, p.Count, p.Weighted, p.Examples);

    private static IReadOnlyList<string> Assumptions(LoadedDataset dataset, RuleOptions rules)
    {
        var list = new List<string>();
        if (dataset.AssignmentsAreDummy)
        {
            list.Add("A1 (DUMMY DATA): TEACHER_CLASS_ALLOCATION.xlsx was not supplied. Section-level assignments were synthesized from the class-level TEACHER_CLASS_ASSIGNMENTS.md (round-robin over sections when several teachers share a class-subject; placeholders repeated per section). Replace the workbook to get a client-valid result.");
        }

        list.Add("A2: Weekly demand per section-subject comes from CLASS_WISE_SUBJECTS.md (H8). Assignment workload_per_week is used only to detect zero-workload rows; mismatches are reported as WORKLOAD_MISMATCH.");
        list.Add($"A3: Zero-workload assignment rows are {(rules.ZeroWorkloadPolicy == ZeroWorkloadPolicy.Exclude ? "excluded (Tier-1 H10)" : "kept as the formal teacher (what-if mode)")}.");
        list.Add(rules.UnstaffedPolicy == UnstaffedPolicy.ReserveSlot
            ? "A4: A curriculum subject without a schedulable teacher keeps its slots as RESERVED_UNSTAFFED (no lesson, no teacher) so each week still has 52 filled cells (H3/H8) and DATA-1 holds. Reserved cells are not counted in totalSlotsScheduled."
            : "A4: A curriculum subject without a schedulable teacher is dropped; those sections have free periods (H3 relaxed to at-most-one for them).");
        list.Add("A5: Activity groups (e.g. 'Games / Library', 'Karate / Gymnastics / Skating') are scheduled per section without teacher booking (T6/AC8). Assignment rows naming one option (Karate, Robotics, Games…) are shown as instructors only.");
        list.Add("A6: teacher_availability is empty → every teacher is available in every teaching period (H2). No dayRestrictions or activity fixedSlots exist in the data (H6/H9 hooks are implemented and fed from config/scheduling-overrides.json).");
        list.Add($"A7: Pre-primary sections are {(rules.ExcludePrePrimary ? "excluded (P4) — they also have no curriculum in CLASS_WISE_SUBJECTS.md" : "included only if a curriculum exists")}. Realistic scope is therefore 30 sections × 52 = 1,560 slots, not 41 × 52 = 2,132.");
        list.Add("A8: Slot pairs follow B2 on the real bell schedule: Mon–Thu (1-2)(3-4)(5-6)(7-8); Fri–Sat (1-2)(3-4)(5-6)(7-8)(9-10). G1's 'first pair' is periods 1–2.");
        list.Add("A9: L1 'first word' = first whitespace token, case-insensitive (so 'Games' and 'Games / Library' share a word; 'Hindi' and 'Hindi Vyakaran' share a word).");
        list.Add("A10: G2 treats 'Games', 'Library' and the 'Games / Library' group as three Games-family items: at most one of them per day in Class 11/12.");
        list.Add("A11: PR-MATH penalises every Mathematics period that is not in periods 1–4 of Monday–Thursday (Friday/Saturday Mathematics is therefore penalised too).");
        list.Add("A12: Teacher-wise timetables contain booked subject lessons only; activity instructors are listed on the section timetable.");
        if (rules.ApplySubjectAliases && dataset.Aliases.Count > 0)
        {
            list.Add("A13: Subject-name aliases from config/subject-aliases.json are applied when no exact curriculum name matches (e.g. 'History & Civics' → History, 'English' → English Language + English Literature). Each use is reported as SUBJECT_ALIAS_APPLIED.");
        }

        if (rules.BlockSinglesSoft)
        {
            list.Add($"A14 (profile 'pragmatic'): H5 is soft — a block subject may appear as a lone single period at a penalty of {rules.Weights.LoneBlockSingle} each, and CP-SAT minimises how many. Strict H5 is impossible for every odd periodsPerWeek block subject and for Grade 6 (only one single-period subject).");
        }

        if (rules.L1SoftCurriculumKeys.Count > 0)
        {
            list.Add($"A15 (profile 'pragmatic'): L1 is soft for {string.Join(", ", rules.L1SoftCurriculumKeys)} because English Language (5/wk) + English Literature (7/wk) at ≤2/day need ≥7 distinct days in a 6-day week.");
        }

        return list;
    }
}
