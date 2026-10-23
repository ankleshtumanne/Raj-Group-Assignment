using System.Diagnostics;
using Rdpl.Timetable.Configuration;
using Rdpl.Timetable.Constraints;
using Rdpl.Timetable.Models;

namespace Rdpl.Timetable.Solver;

public sealed record WarmStartInfo(bool Complete, int SectionsSolved, IReadOnlyList<string> SectionsWithoutSolution, long WallTimeMs, string Strategy);

/// <summary>
/// Builds a CP-SAT solution hint by sequential decomposition.
///
/// Sections are solved one at a time with the SAME rules as the full model (each sub-model is built by
/// <see cref="TimetableModelBuilder"/>), most teacher-constrained section first. After each section, the cells its
/// booked teachers occupy are removed from the allowed cells of those teachers' items in later sections, so the
/// combined result never double-books a teacher (H1). If every section solves, the combination satisfies every
/// hard constraint of the full model; it is still only a hint — the joint model may improve it, and the result is
/// independently validated afterwards like any other solution.
/// </summary>
internal static class WarmStartBuilder
{
    public const string StrategyDescription = "sequential per-section CP-SAT (most teacher-constrained first, earlier teacher bookings blocked)";

    public static (IReadOnlyList<ScheduledCell> Cells, WarmStartInfo Info) Build(SchedulingProblem problem, SolverOptions options, TimeSpan budget, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var busy = new Dictionary<string, HashSet<(SchoolDay, int)>>(StringComparer.OrdinalIgnoreCase);
        var cells = new List<ScheduledCell>();
        var failed = new List<string>();
        var solved = 0;

        var teacherDemand = problem.Items.Where(i => i.BooksTeacher)
            .GroupBy(i => i.TeacherCode!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.PeriodsPerWeek), StringComparer.OrdinalIgnoreCase);
        var order = problem.Sections
            .OrderByDescending(s => problem.ItemsFor(s).Where(i => i.BooksTeacher).Sum(i => teacherDemand[i.TeacherCode!]))
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();

        for (var n = 0; n < order.Count; n++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var section = order[n];
            var left = budget - clock.Elapsed;
            if (left <= TimeSpan.FromSeconds(1))
            {
                failed.AddRange(order.Skip(n).Select(s => s.DisplayName));
                break;
            }

            var perSection = Math.Max(1, Math.Min(options.WarmStartSecondsPerSection, (int)Math.Floor(left.TotalSeconds / (order.Count - n))));
            var source = problem.ItemsFor(section).ToList();
            var local = source.Select((item, k) => new LessonItem(
                k, item.Section, item.Name, item.CurriculumType, item.Kind, item.PeriodsPerWeek, item.PeriodsPerDay,
                item.TeacherCode, item.TeacherName, item.Instructors, item.UnstaffedReason,
                item.BooksTeacher && busy.TryGetValue(item.TeacherCode!, out var blocked)
                    ? item.AllowedSlots.Where(c => !blocked.Contains(c)).ToHashSet()
                    : item.AllowedSlots)).ToList();
            var sub = new SchedulingProblem(problem.Bell, new[] { section }, local, problem.Teachers, problem.Rules);
            var outcome = TimetableSolver.SolveDirect(sub, options with { TimeLimitSeconds = perSection, WarmStart = false }, Relaxation.None);
            if (!outcome.HasSolution)
            {
                failed.Add(section.DisplayName);
                continue;
            }

            solved++;
            foreach (var c in outcome.Cells)
            {
                var item = source[c.ItemIndex];
                cells.Add(new ScheduledCell(item.Index, c.Day, c.Period));
                if (item.BooksTeacher)
                {
                    if (!busy.TryGetValue(item.TeacherCode!, out var set))
                    {
                        set = new HashSet<(SchoolDay, int)>();
                        busy[item.TeacherCode!] = set;
                    }

                    set.Add((c.Day, c.Period));
                }
            }
        }

        clock.Stop();
        return (cells, new WarmStartInfo(failed.Count == 0, solved, failed, clock.ElapsedMilliseconds, StrategyDescription));
    }
}
