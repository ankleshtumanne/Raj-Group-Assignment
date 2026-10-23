using Google.OrTools.Sat;
using Rdpl.Timetable.Models;

namespace Rdpl.Timetable.Constraints;

/// <summary>
/// Hard rules that may be turned into heavily-penalised soft rules for the DIAGNOSTIC solve only.
/// The diagnostic solve never claims success; it exists to show *where* the strict model breaks.
/// </summary>
public sealed record Relaxation
{
    public static readonly Relaxation None = new();

    public static readonly Relaxation Diagnostic = new() { TeacherClash = true, SameFirstWordDay = true, GamesLibrarySameDay = true, BlockSingles = true };

    public bool TeacherClash { get; init; }

    public bool SameFirstWordDay { get; init; }

    public bool GamesLibrarySameDay { get; init; }

    public bool BlockSingles { get; init; }

    public bool Any => TeacherClash || SameFirstWordDay || GamesLibrarySameDay || BlockSingles;

    public const long TeacherClashWeight = 100_000;
    public const long SameFirstWordWeight = 50_000;
    public const long GamesLibraryWeight = 50_000;
    public const long BlockSingleWeight = 20_000;
}

/// <summary>An objective term: either a Tier-3 soft preference or a diagnostic relaxation slack.</summary>
public sealed record PenaltyTerm(IntVar Variable, long Weight, string RuleId, bool IsRelaxation, string Context);

/// <summary>The CP-SAT model plus the handles needed to read the solution back.</summary>
public sealed class BuiltModel
{
    internal BuiltModel(CpModel model, BoolVar?[][][] x, IReadOnlyList<PenaltyTerm> penalties, ModelStatistics statistics)
    {
        Model = model;
        X = x;
        Penalties = penalties;
        Statistics = statistics;
    }

    public CpModel Model { get; }

    /// <summary>x[item][dayIndex][periodIndex] = 1 iff the item is taught in that cell; null = cell not allowed for the item.</summary>
    public BoolVar?[][][] X { get; }

    public IReadOnlyList<PenaltyTerm> Penalties { get; }

    public ModelStatistics Statistics { get; }
}

public sealed record ModelStatistics(int Items, int CellVariables, int PairVariables, int SingleVariables, int TeacherSlotConstraints, int AuxiliaryVariables);

/// <summary>
/// Builds the CP-SAT model.
///
/// Decision variables
///   x[i,d,p] ∈ {0,1}  item i (one section's curriculum item, teacher fixed by T2) is taught on day d, period p.
///                     Created only for allowed cells, so H2/H4/H6/H9/G1 are enforced structurally.
///   y[i,d,k] ∈ {0,1}  block item i occupies the whole fixed slot pair k on day d (B1/B2).
///   z[i,d,p] ∈ {0,1}  block item i appears as a single in period p (only when allowed — see H5 handling).
/// Linking for block items: x[i,d,p] = y[i,d,pair(p)] + z[i,d,p]  (a cell is either part of a pair-block or a single).
///
/// Teacher conflicts (H1) are what couple sections: AtMostOne over every x of a teacher in the same cell.
/// That is why all sections are solved in ONE model rather than independently.
/// </summary>
public static class TimetableModelBuilder
{
    public static BuiltModel Build(SchedulingProblem problem, Relaxation relaxation)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(relaxation);
        var model = new CpModel();
        var rules = problem.Rules;
        var days = problem.Bell.Days;
        var items = problem.Items;
        var penalties = new List<PenaltyTerm>();
        int cellVars = 0, pairVars = 0, singleVars = 0, teacherConstraints = 0, auxVars = 0;

        // ---------------------------------------------------------------- variables (H4: breaks never get a variable)
        var x = new BoolVar?[items.Count][][];
        foreach (var item in items)
        {
            x[item.Index] = new BoolVar?[days.Count][];
            for (var d = 0; d < days.Count; d++)
            {
                var periods = days[d].Periods;
                x[item.Index][d] = new BoolVar?[periods.Count];
                for (var k = 0; k < periods.Count; k++)
                {
                    if (item.AllowedSlots.Contains((days[d].Day, periods[k].Period)))
                    {
                        x[item.Index][d][k] = model.NewBoolVar($"x_i{item.Index}_d{d}_p{periods[k].Period}");
                        cellVars++;
                    }
                }
            }
        }

        // ---------------------------------------------------------------- block structure: H5, B1, B2, B3b
        foreach (var item in items.Where(i => i.IsBlock))
        {
            var singlesAllowed = rules.BlockSinglesSoft || relaxation.BlockSingles;
            var singleWeight = rules.BlockSinglesSoft ? rules.Weights.LoneBlockSingle : Relaxation.BlockSingleWeight;
            var singles = new List<BoolVar>();
            for (var d = 0; d < days.Count; d++)
            {
                var day = days[d];
                var cover = new Dictionary<int, List<BoolVar>>();
                var dayZ = new Dictionary<int, BoolVar>();
                foreach (var (first, second) in day.SlotPairs)
                {
                    var a = Cell(x, item, d, day, first);
                    var b = Cell(x, item, d, day, second);
                    if (a is null || b is null)
                    {
                        continue;
                    }

                    var y = model.NewBoolVar($"y_i{item.Index}_d{d}_p{first}{second}");
                    pairVars++;
                    Add(cover, first, y);
                    Add(cover, second, y);
                }

                for (var k = 0; k < day.Periods.Count; k++)
                {
                    var cell = x[item.Index][d][k];
                    if (cell is null)
                    {
                        continue;
                    }

                    var terms = cover.TryGetValue(day.Periods[k].Period, out var ys) ? new List<BoolVar>(ys) : new List<BoolVar>();
                    if (singlesAllowed)
                    {
                        var z = model.NewBoolVar($"z_i{item.Index}_d{d}_p{day.Periods[k].Period}");
                        singleVars++;
                        singles.Add(z);
                        dayZ[day.Periods[k].Period] = z;
                        terms.Add(z);
                        penalties.Add(new PenaltyTerm(z, singleWeight, "H5/B1", true, $"{item.Section.DisplayName} · {item.Name} single on {day.Day.ToKey()} P{day.Periods[k].Period}"));
                    }

                    // x == Σ y(covering pair) + z — with no terms this forces x = 0 (H5: never a lone single).
                    model.Add(cell == LinearExpr.Sum(terms));
                }

                if (singlesAllowed)
                {
                    // Two singles of the same item filling one slot pair are really a block: force the y encoding,
                    // so every z is a genuine lone single and the penalty count is exact.
                    foreach (var (first, second) in day.SlotPairs)
                    {
                        if (dayZ.TryGetValue(first, out var za) && dayZ.TryGetValue(second, out var zb))
                        {
                            model.Add(za + zb <= 1);
                        }
                    }
                }
            }

            if (singlesAllowed && item.PeriodsPerWeek % 2 == 1)
            {
                // Redundant but helpful: 2·Σy + Σz = ppw (odd) ⇒ at least one single. CP-SAT does not derive parity itself.
                model.Add(LinearExpr.Sum(singles) >= 1);
            }
        }

        // ---------------------------------------------------------------- H8 weekly totals and H7 daily max
        foreach (var item in items)
        {
            var all = AllCells(x, item).ToList();
            model.Add(LinearExpr.Sum(all) == item.PeriodsPerWeek);
            for (var d = 0; d < days.Count; d++)
            {
                var dayCells = x[item.Index][d].Where(v => v is not null).Cast<BoolVar>().ToList();
                if (dayCells.Count > item.PeriodsPerDay)
                {
                    model.Add(LinearExpr.Sum(dayCells) <= item.PeriodsPerDay);
                }
            }
        }

        // ---------------------------------------------------------------- H3 one lesson per section per teaching slot
        foreach (var section in problem.Sections)
        {
            var sectionItems = problem.ItemsFor(section).ToList();
            var complete = sectionItems.Sum(i => i.PeriodsPerWeek) == problem.Bell.WeeklyTeachingSlots;
            for (var d = 0; d < days.Count; d++)
            {
                for (var k = 0; k < days[d].Periods.Count; k++)
                {
                    var lits = sectionItems.Select(i => x[i.Index][d][k]).Where(v => v is not null).Cast<ILiteral>().ToList();
                    if (complete)
                    {
                        model.AddExactlyOne(lits);
                    }
                    else if (lits.Count > 1)
                    {
                        model.AddAtMostOne(lits);
                    }
                }
            }
        }

        // ---------------------------------------------------------------- H1 no teacher double-booking (cross-section)
        foreach (var teacher in items.Where(i => i.BooksTeacher).GroupBy(i => i.TeacherCode!, StringComparer.OrdinalIgnoreCase))
        {
            var teacherItems = teacher.ToList();
            for (var d = 0; d < days.Count; d++)
            {
                for (var k = 0; k < days[d].Periods.Count; k++)
                {
                    var lits = teacherItems.Select(i => x[i.Index][d][k]).Where(v => v is not null).Cast<BoolVar>().ToList();
                    if (lits.Count <= 1)
                    {
                        continue;
                    }

                    teacherConstraints++;
                    if (relaxation.TeacherClash)
                    {
                        var clash = ExactExcess(model, lits, 1, $"clash_{teacher.Key}_d{d}_p{days[d].Periods[k].Period}");
                        auxVars++;
                        penalties.Add(new PenaltyTerm(clash, Relaxation.TeacherClashWeight, "H1", true, $"Teacher {teacher.Key} {days[d].Day.ToKey()} P{days[d].Periods[k].Period}"));
                    }
                    else
                    {
                        model.AddAtMostOne(lits);
                    }
                }
            }
        }

        // ---------------------------------------------------------------- day-usage indicators u[i,d] (for L1 and G2)
        var used = new Dictionary<(int Item, int Day), BoolVar>();
        BoolVar? DayUsed(LessonItem item, int d)
        {
            if (used.TryGetValue((item.Index, d), out var existing))
            {
                return existing;
            }

            var cells = x[item.Index][d].Where(v => v is not null).Cast<BoolVar>().ToList();
            if (cells.Count == 0)
            {
                return null;
            }

            var u = model.NewBoolVar($"u_i{item.Index}_d{d}");
            auxVars++;
            foreach (var c in cells)
            {
                model.AddImplication(c, u);
            }

            model.Add(u <= LinearExpr.Sum(cells));
            used[(item.Index, d)] = u;
            return u;
        }

        foreach (var section in problem.Sections)
        {
            var sectionItems = problem.ItemsFor(section).ToList();

            // L1 (and therefore B4): items sharing the first word never share a day.
            var l1Soft = rules.L1SoftCurriculumKeys.Contains(section.CurriculumKey);
            foreach (var group in sectionItems.GroupBy(i => i.FirstWordKey).Where(g => g.Count() > 1))
            {
                for (var d = 0; d < days.Count; d++)
                {
                    var us = group.Select(i => DayUsed(i, d)).Where(u => u is not null).Cast<BoolVar>().ToList();
                    if (us.Count <= 1)
                    {
                        continue;
                    }

                    if (l1Soft || relaxation.SameFirstWordDay)
                    {
                        var excess = ExactExcess(model, us, 1, $"l1_{section.Id}_{group.Key}_d{d}");
                        auxVars++;
                        var weight = relaxation.SameFirstWordDay ? Relaxation.SameFirstWordWeight : rules.Weights.L1Relaxed;
                        penalties.Add(new PenaltyTerm(excess, weight, "L1", relaxation.SameFirstWordDay || l1Soft, $"{section.DisplayName} '{group.Key}…' items on {days[d].Day.ToKey()}"));
                    }
                    else
                    {
                        model.Add(LinearExpr.Sum(us) <= 1);
                    }
                }
            }

            // G2: Class 11 & 12 — Games and Library (and the combined Games / Library group) never on the same day.
            if (rules.G2CurriculumKeys.Contains(section.CurriculumKey))
            {
                var family = sectionItems.Where(i => i.IsGamesOrLibrary).ToList();
                for (var d = 0; d < days.Count && family.Count > 1; d++)
                {
                    var us = family.Select(i => DayUsed(i, d)).Where(u => u is not null).Cast<BoolVar>().ToList();
                    if (us.Count <= 1)
                    {
                        continue;
                    }

                    if (relaxation.GamesLibrarySameDay)
                    {
                        var excess = ExactExcess(model, us, 1, $"g2_{section.Id}_d{d}");
                        auxVars++;
                        penalties.Add(new PenaltyTerm(excess, Relaxation.GamesLibraryWeight, "G2", true, $"{section.DisplayName} Games & Library on {days[d].Day.ToKey()}"));
                    }
                    else
                    {
                        model.Add(LinearExpr.Sum(us) <= 1);
                    }
                }
            }
        }

        // ---------------------------------------------------------------- L2 no break-crossing, L3 max consecutive
        foreach (var item in items)
        {
            for (var d = 0; d < days.Count; d++)
            {
                var day = days[d];
                for (var k = 0; k + 1 < day.Periods.Count; k++)
                {
                    if (!day.LastPeriodsBeforeBreak.Contains(day.Periods[k].Period))
                    {
                        continue;
                    }

                    var before = x[item.Index][d][k];
                    var after = x[item.Index][d][k + 1];
                    if (before is not null && after is not null)
                    {
                        model.Add(before + after <= 1); // L2
                    }
                }

                var window = rules.MaxConsecutiveSameSubject + 1;
                if (item.PeriodsPerDay < window)
                {
                    continue; // H7 already makes a run longer than MaxConsecutive impossible.
                }

                foreach (var segment in day.Segments)
                {
                    for (var s = 0; s + window <= segment.Count; s++)
                    {
                        var run = segment.Skip(s).Take(window).Select(p => Cell(x, item, d, day, p)).Where(v => v is not null).Cast<BoolVar>().ToList();
                        if (run.Count == window)
                        {
                            model.Add(LinearExpr.Sum(run) <= rules.MaxConsecutiveSameSubject); // L3
                        }
                    }
                }
            }
        }

        // ---------------------------------------------------------------- Tier 3 soft preferences
        if (rules.EnableSoftObjective)
        {
            var weights = rules.Weights;
            foreach (var item in items.Where(i => i.IsMathematics && i.Kind != LessonKind.ReservedUnstaffed))
            {
                for (var d = 0; d < days.Count; d++)
                {
                    var day = days[d];
                    var monToThu = day.Day <= SchoolDay.Thursday;
                    for (var k = 0; k < day.Periods.Count; k++)
                    {
                        var cell = x[item.Index][d][k];
                        if (cell is not null && !(monToThu && day.MorningPeriods.Contains(day.Periods[k].Period)))
                        {
                            penalties.Add(new PenaltyTerm(cell, weights.MathOutsideMorning, "PR-MATH", false, $"{item.Section.DisplayName} Mathematics {day.Day.ToKey()} P{day.Periods[k].Period}"));
                        }
                    }
                }
            }

            foreach (var section in problem.Sections)
            {
                var sectionItems = problem.ItemsFor(section).ToList();
                var games = sectionItems.Where(i => i.IsGames).ToList();
                var library = sectionItems.Where(i => i.IsLibrary).ToList();
                for (var d = 0; d < days.Count; d++)
                {
                    var day = days[d];
                    AddPerDayExcess(model, penalties, x, games, d, weights.GamesAboveOnePerDay, "DB-GAMES-1/DAY", $"{section.DisplayName} Games {day.Day.ToKey()}", ref auxVars);
                    AddPerDayExcess(model, penalties, x, library, d, weights.LibraryAboveOnePerDay, "DB-LIBRARY-1/DAY", $"{section.DisplayName} Library {day.Day.ToKey()}", ref auxVars);

                    foreach (var segment in day.Segments)
                    {
                        for (var s = 0; s + 3 <= segment.Count; s++)
                        {
                            var lits = segment.Skip(s).Take(3).SelectMany(p => games.Select(g => Cell(x, g, d, day, p))).Where(v => v is not null).Cast<BoolVar>().ToList();
                            if (lits.Count <= 2)
                            {
                                continue;
                            }

                            var e = ExactExcess(model, lits, 2, $"g3_{section.Id}_d{d}_s{segment[s]}");
                            auxVars++;
                            penalties.Add(new PenaltyTerm(e, weights.GamesConsecutiveAboveTwo, "DB-GAMES-MAX2-CONSEC", false, $"{section.DisplayName} {day.Day.ToKey()} from P{segment[s]}"));
                        }
                    }
                }
            }
        }

        if (penalties.Count > 0)
        {
            var objective = LinearExpr.NewBuilder(penalties.Count);
            foreach (var p in penalties)
            {
                objective.AddTerm(p.Variable, p.Weight);
            }

            model.Minimize(objective);
        }

        return new BuiltModel(model, x, penalties, new ModelStatistics(items.Count, cellVars, pairVars, singleVars, teacherConstraints, auxVars));
    }

    /// <summary>
    /// Exact slack: v = max(0, Σ lits − allowed). Using equality (not just ≥) keeps the objective equal to the true
    /// penalty of the returned timetable even when the search stops before proving optimality.
    /// </summary>
    private static IntVar ExactExcess(CpModel model, IReadOnlyList<BoolVar> lits, int allowed, string name)
    {
        var v = model.NewIntVar(0, Math.Max(0, lits.Count - allowed), name);
        model.AddMaxEquality(v, new[] { LinearExpr.Sum(lits) - allowed, LinearExpr.Constant(0) });
        return v;
    }

    private static void AddPerDayExcess(CpModel model, List<PenaltyTerm> penalties, BoolVar?[][][] x, IReadOnlyList<LessonItem> items, int d, int weight, string ruleId, string context, ref int auxVars)
    {
        var cells = items.SelectMany(i => x[i.Index][d]).Where(v => v is not null).Cast<BoolVar>().ToList();
        if (cells.Count <= 1)
        {
            return;
        }

        var excess = ExactExcess(model, cells, 1, $"excess_{ruleId}_{auxVars}");
        auxVars++;
        penalties.Add(new PenaltyTerm(excess, weight, ruleId, false, context));
    }

    private static BoolVar? Cell(BoolVar?[][][] x, LessonItem item, int dayIndex, DaySchedule day, int period)
    {
        for (var k = 0; k < day.Periods.Count; k++)
        {
            if (day.Periods[k].Period == period)
            {
                return x[item.Index][dayIndex][k];
            }
        }

        return null;
    }

    private static IEnumerable<BoolVar> AllCells(BoolVar?[][][] x, LessonItem item) =>
        x[item.Index].SelectMany(d => d).Where(v => v is not null).Cast<BoolVar>();

    private static void Add(Dictionary<int, List<BoolVar>> map, int key, BoolVar value)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = new List<BoolVar>();
            map[key] = list;
        }

        list.Add(value);
    }
}
