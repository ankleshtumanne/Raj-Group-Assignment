using Rdpl.Timetable.Models;

namespace Rdpl.Timetable.Validation;

/// <summary>
/// Cheap counting arguments that PROVE infeasibility before CP-SAT runs and say exactly why.
/// CP-SAT itself only reports "INFEASIBLE"; it does not explain the cause. Each finding here is a
/// necessary condition — if one is violated, no timetable can satisfy the hard rules.
/// </summary>
public static class PreSolveAnalyzer
{
    public static IReadOnlyList<SchedulingConflict> Analyze(SchedulingProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        var findings = new List<SchedulingConflict>();
        var rules = problem.Rules;

        foreach (var item in problem.Items)
        {
            // H7 + H8: enough allowed capacity across the week?
            var perDayCapacity = problem.Bell.Days.Sum(d => Math.Min(item.PeriodsPerDay, MaxPlaceableOnDay(item, d, rules.BlockSinglesSoft)));
            if (perDayCapacity < item.PeriodsPerWeek)
            {
                findings.Add(new SchedulingConflict("ITEM_CAPACITY", "H7/H8",
                    $"{item.Section.DisplayName} · {item.Name}: needs {item.PeriodsPerWeek} periods/week but at most {perDayCapacity} can be placed (≤{item.PeriodsPerDay}/day within allowed slots).",
                    true, new[] { item.Section.DisplayName }, item.TeacherCode));
            }

            // H5/B1 parity: a block subject placed only as full pairs needs an even weekly count.
            if (item.IsBlock && item.PeriodsPerWeek % 2 == 1 && !rules.BlockSinglesSoft)
            {
                findings.Add(new SchedulingConflict("BLOCK_PARITY", "H5/B1/H8",
                    $"{item.Section.DisplayName} · {item.Name}: block subject (periodsPerDay {item.PeriodsPerDay}) with ODD periodsPerWeek {item.PeriodsPerWeek}. Full slot pairs can only produce even counts, so H5+B1+H8 cannot all hold. (Profile 'pragmatic' makes lone singles a minimised soft penalty — production force-fill also places block subjects as singles.)",
                    true, new[] { item.Section.DisplayName }));
            }
        }

        foreach (var section in problem.Sections)
        {
            var sectionItems = problem.ItemsFor(section).ToList();

            // H3/H8: total demand vs slots.
            var demand = sectionItems.Sum(i => i.PeriodsPerWeek);
            if (demand > problem.Bell.WeeklyTeachingSlots)
            {
                findings.Add(new SchedulingConflict("SECTION_OVERFULL", "H3/H8",
                    $"{section.DisplayName}: {demand} periods required but only {problem.Bell.WeeklyTeachingSlots} teaching slots exist.", true, new[] { section.DisplayName }));
            }

            // H5/B1: with block subjects only in full pairs, every half-pair must be filled by a ppd=1 subject, and one
            // pair cannot hold the same ppd=1 subject twice (same day → H7). So single-period demand must be even and
            // no single subject may need more than half of it.
            var everyPeriodPaired = problem.Bell.Days.All(d => d.SlotPairs.Count * 2 == d.Periods.Count);
            if (!rules.BlockSinglesSoft && everyPeriodPaired)
            {
                var singles = sectionItems.Where(i => !i.IsBlock).ToList();
                var total = singles.Sum(i => i.PeriodsPerWeek);
                var largest = singles.Count == 0 ? 0 : singles.Max(i => i.PeriodsPerWeek);
                if (total % 2 == 1 || largest * 2 > total)
                {
                    var detail = singles.Count == 0 ? "none" : string.Join(", ", singles.Select(i => $"{i.Name} {i.PeriodsPerWeek}/wk"));
                    findings.Add(new SchedulingConflict("SINGLES_UNPAIRABLE", "H5/B1",
                        $"{section.DisplayName}: single-period subjects ({detail}) cannot fill their slot pairs with each other (need an even total with no subject above half). Some block subject would have to appear as a lone single, which strict H5 forbids.",
                        true, new[] { section.DisplayName }));
                }
            }

            // L1: items sharing a first word need pairwise-disjoint days.
            if (!rules.L1SoftCurriculumKeys.Contains(section.CurriculumKey))
            {
                foreach (var group in sectionItems.GroupBy(i => i.FirstWordKey).Where(g => g.Count() > 1))
                {
                    var minDays = group.Sum(i => (int)Math.Ceiling(i.PeriodsPerWeek / (double)i.PeriodsPerDay));
                    var days = group.SelectMany(i => i.AllowedSlots.Select(s => s.Day)).Distinct().Count();
                    if (minDays > days)
                    {
                        findings.Add(new SchedulingConflict("L1_DAY_CAPACITY", "L1",
                            $"{section.DisplayName}: {string.Join(" + ", group.Select(i => $"{i.Name} ({i.PeriodsPerWeek}/wk, ≤{i.PeriodsPerDay}/day → ≥{(int)Math.Ceiling(i.PeriodsPerWeek / (double)i.PeriodsPerDay)} days)"))} need {minDays} separate days, but only {days} days exist. L1 forbids them sharing a day.",
                            true, new[] { section.DisplayName }));
                    }
                }
            }

            // G2: in Class 11/12 each Games/Library-family item needs its own days.
            if (rules.G2CurriculumKeys.Contains(section.CurriculumKey))
            {
                var family = sectionItems.Where(i => i.IsGamesOrLibrary).ToList();
                var minDays = family.Sum(i => (int)Math.Ceiling(i.PeriodsPerWeek / (double)i.PeriodsPerDay));
                if (family.Count > 1 && minDays > problem.Bell.Days.Count)
                {
                    findings.Add(new SchedulingConflict("G2_DAY_CAPACITY", "G2",
                        $"{section.DisplayName}: Games/Library items need {minDays} separate days but only {problem.Bell.Days.Count} exist.", true, new[] { section.DisplayName }));
                }
            }
        }

        // H1: a teacher can teach at most one lesson per teaching slot they are available in.
        foreach (var teacher in problem.Items.Where(i => i.BooksTeacher).GroupBy(i => i.TeacherCode!, StringComparer.OrdinalIgnoreCase))
        {
            var demand = teacher.Sum(i => i.PeriodsPerWeek);
            var available = teacher.SelectMany(i => i.AllowedSlots).Distinct().Count();
            if (demand > available)
            {
                var name = problem.Teachers.TryGetValue(teacher.Key, out var t) ? t.Name : teacher.Key;
                var sections = teacher.Select(i => i.Section.DisplayName).Distinct().ToList();
                findings.Add(new SchedulingConflict("TEACHER_OVERLOAD", "H1",
                    $"Teacher {teacher.Key} ({name}): {demand} periods required across {sections.Count} sections ({string.Join(", ", teacher.GroupBy(i => i.Name).Select(g => $"{g.Key} ×{g.Count()}"))}) but max capacity is {available} teaching slots.",
                    true, sections, teacher.Key));
            }
            else if (demand >= available - 2)
            {
                var name = problem.Teachers.TryGetValue(teacher.Key, out var t) ? t.Name : teacher.Key;
                findings.Add(new SchedulingConflict("TEACHER_NEAR_CAPACITY", "H1",
                    $"Teacher {teacher.Key} ({name}): {demand} of {available} available slots used — very little freedom; feasibility depends on the rest of the week.",
                    false, teacher.Select(i => i.Section.DisplayName).Distinct().ToList(), teacher.Key));
            }
        }

        return findings;
    }

    /// <summary>Upper bound of periods an item can take on one day given its allowed slots and the pair structure.</summary>
    private static int MaxPlaceableOnDay(LessonItem item, DaySchedule day, bool allowSingle)
    {
        var allowed = day.PeriodNumbers.Where(p => item.AllowedSlots.Contains((day.Day, p))).ToHashSet();
        if (!item.IsBlock)
        {
            return allowed.Count;
        }

        var pairs = day.SlotPairs.Count(pair => allowed.Contains(pair.First) && allowed.Contains(pair.Second)) * 2;
        return allowSingle ? allowed.Count : pairs;
    }
}
