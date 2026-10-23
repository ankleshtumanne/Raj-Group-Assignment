using System.Diagnostics;
using System.Globalization;
using Google.OrTools.Sat;
using Rdpl.Timetable.Configuration;
using Rdpl.Timetable.Constraints;
using Rdpl.Timetable.Models;

namespace Rdpl.Timetable.Solver;

/// <summary>One taught (or reserved) cell of the generated week.</summary>
public sealed record ScheduledCell(int ItemIndex, SchoolDay Day, int Period);

public sealed record PenaltySummary(string RuleId, bool IsRelaxation, long Count, long Weighted, IReadOnlyList<string> Examples);

public sealed record SolveOutcome(
    string Status,
    bool HasSolution,
    long WallTimeMs,
    double SolverWallTimeSeconds,
    int TimeLimitSeconds,
    double? ObjectiveValue,
    double? BestObjectiveBound,
    IReadOnlyList<ScheduledCell> Cells,
    IReadOnlyList<PenaltySummary> Penalties,
    ModelStatistics Statistics)
{
    /// <summary>"CP-SAT" when the joint model returned the timetable; "WARM_START" when the joint search returned nothing and the complete warm-start timetable is used.</summary>
    public string SolutionSource { get; init; } = "CP-SAT";

    public WarmStartInfo? WarmStart { get; init; }
}

/// <summary>Runs CP-SAT on a built model and extracts the timetable cells.</summary>
public static class TimetableSolver
{
    /// <summary>
    /// Full solve. For the real (non-diagnostic) model with more than one section this first builds a warm-start
    /// hint (see <see cref="WarmStartBuilder"/>) within <see cref="SolverOptions.WarmStartBudgetFraction"/> of the
    /// time limit, then solves the joint model with the hint for the remaining time. The total stays within
    /// <see cref="SolverOptions.TimeLimitSeconds"/>.
    /// </summary>
    public static SolveOutcome Solve(SchedulingProblem problem, SolverOptions options, Relaxation relaxation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(relaxation);
        options.Validate();

        if (!options.WarmStart || relaxation.Any || problem.Sections.Count < 2)
        {
            return SolveDirect(problem, options, relaxation);
        }

        var clock = Stopwatch.StartNew();
        var budget = TimeSpan.FromSeconds(options.TimeLimitSeconds * options.WarmStartBudgetFraction);
        var (hintCells, warmInfo) = WarmStartBuilder.Build(problem, options, budget, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var remaining = Math.Max(1, options.TimeLimitSeconds - (int)Math.Ceiling(clock.Elapsed.TotalSeconds));
        var outcome = SolveDirect(problem, options with { TimeLimitSeconds = remaining }, relaxation, hintCells);
        clock.Stop();

        if (!outcome.HasSolution && warmInfo.Complete && outcome.Status != "INFEASIBLE")
        {
            // The joint search stopped before re-finding a solution, but the warm start is a complete timetable built
            // with the same hard rules. Return it (it is validated independently like any other result).
            outcome = outcome with
            {
                Status = "FEASIBLE",
                HasSolution = true,
                Cells = hintCells,
                ObjectiveValue = null,
                BestObjectiveBound = null,
                SolutionSource = "WARM_START",
            };
        }

        return outcome with { WallTimeMs = clock.ElapsedMilliseconds, TimeLimitSeconds = options.TimeLimitSeconds, WarmStart = warmInfo };
    }

    /// <summary>Single CP-SAT run on the joint model, optionally with solution hints (no warm start).</summary>
    internal static SolveOutcome SolveDirect(SchedulingProblem problem, SolverOptions options, Relaxation relaxation, IReadOnlyCollection<ScheduledCell>? hint = null)
    {
        options.Validate();
        var clock = Stopwatch.StartNew();
        var built = TimetableModelBuilder.Build(problem, relaxation);
        var days = problem.Bell.Days;

        if (hint is { Count: > 0 })
        {
            var set = hint.Select(c => (c.ItemIndex, c.Day, c.Period)).ToHashSet();
            foreach (var item in problem.Items)
            {
                for (var d = 0; d < days.Count; d++)
                {
                    for (var k = 0; k < days[d].Periods.Count; k++)
                    {
                        if (built.X[item.Index][d][k] is { } v)
                        {
                            built.Model.AddHint(v, set.Contains((item.Index, days[d].Day, days[d].Periods[k].Period)));
                        }
                    }
                }
            }
        }

        var parameters = string.Create(CultureInfo.InvariantCulture,
            $"max_time_in_seconds:{options.TimeLimitSeconds} num_workers:{options.Workers} random_seed:{options.RandomSeed} log_search_progress:false");
        if (hint is { Count: > 0 })
        {
            parameters += " repair_hint:true";
        }

        using var solver = new CpSolver { StringParameters = parameters };
        var status = solver.Solve(built.Model);
        clock.Stop();

        var hasSolution = status is CpSolverStatus.Optimal or CpSolverStatus.Feasible;
        var cells = new List<ScheduledCell>();
        var penalties = new List<PenaltySummary>();
        if (hasSolution)
        {
            foreach (var item in problem.Items)
            {
                for (var d = 0; d < days.Count; d++)
                {
                    for (var k = 0; k < days[d].Periods.Count; k++)
                    {
                        var v = built.X[item.Index][d][k];
                        if (v is not null && solver.BooleanValue(v))
                        {
                            cells.Add(new ScheduledCell(item.Index, days[d].Day, days[d].Periods[k].Period));
                        }
                    }
                }
            }

            foreach (var group in built.Penalties.GroupBy(p => (p.RuleId, p.IsRelaxation)))
            {
                long count = 0, weighted = 0;
                var examples = new List<string>();
                foreach (var term in group)
                {
                    var value = solver.Value(term.Variable);
                    if (value <= 0)
                    {
                        continue;
                    }

                    count += value;
                    weighted += value * term.Weight;
                    if (examples.Count < 25)
                    {
                        examples.Add(value == 1 ? term.Context : $"{term.Context} (×{value})");
                    }
                }

                if (count > 0)
                {
                    penalties.Add(new PenaltySummary(group.Key.RuleId, group.Key.IsRelaxation, count, weighted, examples));
                }
            }
        }

        return new SolveOutcome(
            StatusName(status),
            hasSolution,
            clock.ElapsedMilliseconds,
            solver.WallTime(),
            options.TimeLimitSeconds,
            hasSolution && built.Penalties.Count > 0 ? solver.ObjectiveValue : hasSolution ? 0 : null,
            hasSolution && built.Penalties.Count > 0 ? solver.BestObjectiveBound : null,
            cells,
            penalties.OrderByDescending(p => p.IsRelaxation).ThenByDescending(p => p.Weighted).ToList(),
            built.Statistics);
    }

    public static string StatusName(CpSolverStatus status) => status switch
    {
        CpSolverStatus.Optimal => "OPTIMAL",
        CpSolverStatus.Feasible => "FEASIBLE",
        CpSolverStatus.Infeasible => "INFEASIBLE",
        CpSolverStatus.ModelInvalid => "MODEL_INVALID",
        _ => "UNKNOWN",
    };
}
