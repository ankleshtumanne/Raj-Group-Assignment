using Rdpl.Timetable.Models;
using Rdpl.Timetable.Solver;

namespace Rdpl.Timetable.Validation;

public sealed record Violation(string RuleId, string Message, string? Section = null, string? Day = null, int? Period = null);

public sealed record ValidationReport(
    bool HardConstraintsSatisfied,
    IReadOnlyList<Violation> HardViolations,
    IReadOnlyList<Violation> RelaxedRuleViolations,
    long SoftPenalty,
    IReadOnlyDictionary<string, long> SoftPenaltyByRule,
    IReadOnlyList<string> ChecksPerformed);

/// <summary>
/// Re-checks a generated timetable against the rules WITHOUT using the CP-SAT model or its variables.
/// A solver status of OPTIMAL/FEASIBLE is not accepted as proof of correctness on its own.
/// </summary>
public static class TimetableValidator
{
    public static readonly IReadOnlyList<string> Checks = new[]
    {
        "H4 cells exist in bell schedule (no break periods)", "H3 one lesson per section per slot", "H8 weekly totals", "H7 daily max",
        "H1 no teacher double-booking", "T2 formal teacher only", "DATA-1 no placeholder teacher scheduled", "H2/H6/H9 allowed cells",
        "G1 Games/Library not in first pair", "G2 Class 11/12 Games+Library not same day", "L1 same-first-word not same day",
        "B4 no same-first-word singles in one pair", "H5/B1 block subjects in full slot pairs", "L2 no break crossing", "L3 max consecutive",
        "Soft: PR-MATH, DB Games/Library per day, DB max two consecutive Games",
    };

    public static ValidationReport Validate(SchedulingProblem problem, IReadOnlyList<ScheduledCell> cells)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(cells);
        var rules = problem.Rules;
        var hard = new List<Violation>();
        var relaxed = new List<Violation>();
        var soft = new Dictionary<string, long>(StringComparer.Ordinal);

        void Hard(string rule, string message, Section? s = null, SchoolDay? d = null, int? p = null) =>
            hard.Add(new Violation(rule, message, s?.DisplayName, d?.ToKey(), p));

        // ---- cell validity (H4) ---------------------------------------------------------------------
        var valid = new List<(LessonItem Item, SchoolDay Day, int Period)>();
        foreach (var c in cells)
        {
            if (c.ItemIndex < 0 || c.ItemIndex >= problem.Items.Count)
            {
                Hard("MODEL", $"Cell references unknown item {c.ItemIndex}.");
                continue;
            }

            var item = problem.Items[c.ItemIndex];
            var day = problem.Bell.GetDay(c.Day);
            if (day is null || !day.HasPeriod(c.Period))
            {
                Hard("H4", $"{item.Name} placed in {c.Day.ToKey()} P{c.Period}, which is not a teaching period.", item.Section, c.Day, c.Period);
                continue;
            }

            valid.Add((item, c.Day, c.Period));
        }

        // ---- H3 ---------------------------------------------------------------------------------------
        foreach (var section in problem.Sections)
        {
            var complete = problem.ItemsFor(section).Sum(i => i.PeriodsPerWeek) == problem.Bell.WeeklyTeachingSlots;
            foreach (var day in problem.Bell.Days)
            {
                foreach (var period in day.PeriodNumbers)
                {
                    var count = valid.Count(v => v.Item.Section.Id == section.Id && v.Day == day.Day && v.Period == period);
                    if (count > 1 || (complete && count == 0))
                    {
                        Hard("H3", $"{count} lessons in one slot.", section, day.Day, period);
                    }
                }
            }
        }

        var loneSingles = 0L;
        var byItem = valid.GroupBy(v => v.Item.Index).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var item in problem.Items)
        {
            var placed = byItem.TryGetValue(item.Index, out var list) ? list : new List<(LessonItem, SchoolDay, int)>();

            // ---- H8 / H7 / allowed cells ------------------------------------------------------------------
            if (placed.Count != item.PeriodsPerWeek)
            {
                Hard("H8", $"{item.Name}: {placed.Count} periods placed, {item.PeriodsPerWeek} required.", item.Section);
            }

            foreach (var dayGroup in placed.GroupBy(p => p.Item2))
            {
                if (dayGroup.Count() > item.PeriodsPerDay)
                {
                    Hard("H7", $"{item.Name}: {dayGroup.Count()} periods on one day (max {item.PeriodsPerDay}).", item.Section, dayGroup.Key);
                }
            }

            foreach (var (_, d, p) in placed)
            {
                if (!item.AllowedSlots.Contains((d, p)) && !(item.IsGamesOrLibrary && IsFirstPair(problem, d, p)))
                {
                    Hard("H2/H6/H9", $"{item.Name} placed in a cell excluded by availability, day restriction or fixed slot.", item.Section, d, p);
                }

                if (item.IsGamesOrLibrary && IsFirstPair(problem, d, p))
                {
                    Hard("G1", $"{item.Name} placed in the first slot pair.", item.Section, d, p);
                }
            }

            // ---- T2 / DATA-1 ---------------------------------------------------------------------------
            if (placed.Count > 0)
            {
                if (item.TeacherCode is not null && rules.PlaceholderTeacherCodes.Contains(item.TeacherCode))
                {
                    Hard("DATA-1", $"{item.Name} scheduled with placeholder teacher {item.TeacherCode}.", item.Section);
                }

                if (item.Kind == LessonKind.Subject && (item.TeacherCode is null || !problem.Teachers.ContainsKey(item.TeacherCode)))
                {
                    Hard("T2", $"{item.Name} scheduled without a formally assigned teacher.", item.Section);
                }

                if (item.Kind == LessonKind.ReservedUnstaffed && item.TeacherCode is not null)
                {
                    Hard("DATA-1", $"Reserved (unstaffed) {item.Name} carries a teacher.", item.Section);
                }
            }

            // ---- H5 / B1 --------------------------------------------------------------------------------
            if (item.IsBlock)
            {
                var singles = 0;
                foreach (var dayGroup in placed.GroupBy(p => p.Item2))
                {
                    var day = problem.Bell.GetDay(dayGroup.Key)!;
                    var periods = dayGroup.Select(p => p.Item3).ToHashSet();
                    foreach (var p in periods)
                    {
                        var pair = day.PairOf(p);
                        var partner = pair is null ? (int?)null : pair.Value.First == p ? pair.Value.Second : pair.Value.First;
                        if (partner is null || !periods.Contains(partner.Value))
                        {
                            singles++;
                        }
                    }
                }

                if (singles > 0)
                {
                    var v = new Violation("H5/B1", $"{item.Name} (block subject) has {singles} lone single period(s).", item.Section.DisplayName);
                    if (rules.BlockSinglesSoft)
                    {
                        relaxed.Add(v);
                        loneSingles += singles;
                    }
                    else
                    {
                        hard.Add(v);
                    }
                }
            }

            // ---- L2 / L3 --------------------------------------------------------------------------------
            foreach (var dayGroup in placed.GroupBy(p => p.Item2))
            {
                var day = problem.Bell.GetDay(dayGroup.Key)!;
                var periods = dayGroup.Select(p => p.Item3).ToHashSet();
                var ordered = day.PeriodNumbers.ToList();
                for (var k = 0; k + 1 < ordered.Count; k++)
                {
                    if (day.LastPeriodsBeforeBreak.Contains(ordered[k]) && periods.Contains(ordered[k]) && periods.Contains(ordered[k + 1]))
                    {
                        Hard("L2", $"{item.Name} spans the break between P{ordered[k]} and P{ordered[k + 1]}.", item.Section, day.Day, ordered[k]);
                    }
                }

                foreach (var segment in day.Segments)
                {
                    var run = 0;
                    foreach (var p in segment)
                    {
                        run = periods.Contains(p) ? run + 1 : 0;
                        if (run > rules.MaxConsecutiveSameSubject)
                        {
                            Hard("L3", $"{item.Name} has more than {rules.MaxConsecutiveSameSubject} consecutive periods.", item.Section, day.Day, p);
                        }
                    }
                }
            }
        }

        // ---- H1 -------------------------------------------------------------------------------------------
        foreach (var clash in valid.Where(v => v.Item.BooksTeacher)
                     .GroupBy(v => (Teacher: v.Item.TeacherCode!.ToUpperInvariant(), v.Day, v.Period))
                     .Where(g => g.Select(v => v.Item.Section.Id).Distinct().Count() > 1 || g.Count() > 1))
        {
            Hard("H1", $"Teacher {clash.Key.Teacher} double-booked: {string.Join(" & ", clash.Select(v => $"{v.Item.Section.DisplayName} {v.Item.Name}"))}.", null, clash.Key.Day, clash.Key.Period);
        }

        // ---- L1 / B4 / G2 (per section, per day) ---------------------------------------------------------
        foreach (var section in problem.Sections)
        {
            var sectionCells = valid.Where(v => v.Item.Section.Id == section.Id).ToList();
            var l1Soft = rules.L1SoftCurriculumKeys.Contains(section.CurriculumKey);
            foreach (var day in problem.Bell.Days)
            {
                var dayCells = sectionCells.Where(v => v.Day == day.Day).ToList();
                foreach (var group in dayCells.GroupBy(v => v.Item.FirstWordKey))
                {
                    var distinct = group.Select(v => v.Item.Name).Distinct().ToList();
                    if (distinct.Count > 1)
                    {
                        var v = new Violation("L1", $"{string.Join(" + ", distinct)} on the same day.", section.DisplayName, day.Day.ToKey());
                        (l1Soft ? relaxed : hard).Add(v);
                    }
                }

                foreach (var (first, second) in day.SlotPairs)
                {
                    var a = dayCells.FirstOrDefault(v => v.Period == first).Item;
                    var b = dayCells.FirstOrDefault(v => v.Period == second).Item;
                    if (a is not null && b is not null && a.Index != b.Index && !a.IsBlock && !b.IsBlock && a.FirstWordKey == b.FirstWordKey && !l1Soft)
                    {
                        Hard("B4", $"{a.Name} and {b.Name} paired in one slot pair.", section, day.Day, first);
                    }
                }

                if (rules.G2CurriculumKeys.Contains(section.CurriculumKey))
                {
                    var family = dayCells.Where(v => v.Item.IsGamesOrLibrary).Select(v => v.Item.Name).Distinct().ToList();
                    if (family.Count > 1)
                    {
                        Hard("G2", $"{string.Join(" + ", family)} on the same day.", section, day.Day);
                    }
                }
            }
        }

        // ---- soft objective, recomputed independently ----------------------------------------------------
        if (rules.EnableSoftObjective)
        {
            var w = rules.Weights;
            foreach (var (item, d, p) in valid.Where(v => v.Item.IsMathematics && v.Item.Kind != LessonKind.ReservedUnstaffed))
            {
                var day = problem.Bell.GetDay(d)!;
                if (!(d <= SchoolDay.Thursday && day.MorningPeriods.Contains(p)))
                {
                    Accumulate(soft, "PR-MATH", w.MathOutsideMorning);
                }
            }

            foreach (var section in problem.Sections)
            {
                var sectionCells = valid.Where(v => v.Item.Section.Id == section.Id).ToList();
                foreach (var day in problem.Bell.Days)
                {
                    var games = sectionCells.Where(v => v.Day == day.Day && v.Item.IsGames).Select(v => v.Period).ToHashSet();
                    var library = sectionCells.Count(v => v.Day == day.Day && v.Item.IsLibrary);
                    if (games.Count > 1)
                    {
                        Accumulate(soft, "DB-GAMES-1/DAY", (games.Count - 1) * (long)w.GamesAboveOnePerDay);
                    }

                    if (library > 1)
                    {
                        Accumulate(soft, "DB-LIBRARY-1/DAY", (library - 1) * (long)w.LibraryAboveOnePerDay);
                    }

                    foreach (var segment in day.Segments)
                    {
                        for (var s = 0; s + 3 <= segment.Count; s++)
                        {
                            if (segment.Skip(s).Take(3).All(games.Contains))
                            {
                                Accumulate(soft, "DB-GAMES-MAX2-CONSEC", w.GamesConsecutiveAboveTwo);
                            }
                        }
                    }
                }
            }

            if (relaxed.Any(v => v.RuleId == "L1"))
            {
                Accumulate(soft, "L1(relaxed)", relaxed.Count(v => v.RuleId == "L1") * (long)w.L1Relaxed);
            }

            if (loneSingles > 0)
            {
                Accumulate(soft, "H5/B1(relaxed)", loneSingles * w.LoneBlockSingle);
            }
        }

        return new ValidationReport(hard.Count == 0, hard, relaxed, soft.Values.Sum(), soft, Checks);
    }

    private static bool IsFirstPair(SchedulingProblem problem, SchoolDay d, int p)
    {
        var day = problem.Bell.GetDay(d);
        return day is not null && day.SlotPairs.Count > 0 && (day.SlotPairs[0].First == p || day.SlotPairs[0].Second == p);
    }

    private static void Accumulate(Dictionary<string, long> map, string key, long value) =>
        map[key] = map.TryGetValue(key, out var v) ? v + value : value;
}
