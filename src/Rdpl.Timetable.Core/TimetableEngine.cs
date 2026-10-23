using System.Diagnostics;
using Rdpl.Timetable.Configuration;
using Rdpl.Timetable.Constraints;
using Rdpl.Timetable.Data;
using Rdpl.Timetable.Models;
using Rdpl.Timetable.Normalization;
using Rdpl.Timetable.Reporting;
using Rdpl.Timetable.Solver;
using Rdpl.Timetable.Validation;

namespace Rdpl.Timetable;

/// <summary>Everything produced by one run, for hosts that need more than the JSON report.</summary>
public sealed record TimetableRun(TimetableReport Report, NormalizationResult Normalization, IReadOnlyList<SchedulingConflict> PreSolveFindings, SolveOutcome? Strict, SolveOutcome? Diagnostic);

/// <summary>
/// End-to-end pipeline: normalize → pre-solve analysis → strict CP-SAT solve → independent validation →
/// (if infeasible) diagnostic relaxed solve → report.
/// </summary>
public static class TimetableEngine
{
    public const string EngineName = "Google OR-Tools CP-SAT";

    public static TimetableRun Run(LoadedDataset dataset, RuleOptions rules, SolverOptions solverOptions, CancellationToken cancellationToken = default)
        => Run(dataset, rules, solverOptions, solve: true, cancellationToken);

    /// <param name="solve">false = load, normalize and analyse only ("validate-data"); the solver is not run.</param>
    public static TimetableRun Run(LoadedDataset dataset, RuleOptions rules, SolverOptions solverOptions, bool solve, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(solverOptions);
        solverOptions.Validate();
        var total = Stopwatch.StartNew();

        var normalization = ProblemBuilder.Build(dataset, rules);
        var problem = normalization.Problem;
        var dataConflicts = dataset.Conflicts.Concat(normalization.Conflicts).ToList();
        var findings = PreSolveAnalyzer.Analyze(problem);
        var schedulingConflicts = findings.ToList();
        cancellationToken.ThrowIfCancellationRequested();

        SolveOutcome? strict = null;
        SolveOutcome? diagnostic = null;
        ValidationReport? strictValidation = null;
        ValidationReport? diagnosticValidation = null;
        string? skippedReason = null;

        if (!solve)
        {
            skippedReason = "Data validation only (validate-data); the solver was not run.";
        }
        else if (problem.Items.Count == 0)
        {
            skippedReason = "Nothing to schedule: no section is in scope.";
        }
        else if (findings.Any(f => f.ProvesInfeasibility))
        {
            skippedReason = $"Skipped: pre-solve analysis proved the strict model INFEASIBLE ({findings.Count(f => f.ProvesInfeasibility)} blocking finding(s) in schedulingConflicts).";
        }
        else
        {
            strict = TimetableSolver.Solve(problem, solverOptions, Relaxation.None, cancellationToken);
            if (strict.HasSolution)
            {
                strictValidation = TimetableValidator.Validate(problem, strict.Cells);
            }
            else if (strict.Status == "INFEASIBLE")
            {
                schedulingConflicts.Add(new SchedulingConflict("SOLVER_INFEASIBLE", "ALL",
                    "CP-SAT proved the strict model INFEASIBLE although no single pre-solve counting check failed. The cause is an interaction of rules (typically shared teachers + L1 + block pairs); see the diagnostic solve for the smallest set of violations.",
                    true));
            }
            else if (strict.Status == "UNKNOWN")
            {
                schedulingConflicts.Add(new SchedulingConflict("SOLVER_TIMEOUT", "ALL",
                    $"No solution found within the {solverOptions.TimeLimitSeconds}s time limit. Feasibility is unproven; increase Solver.TimeLimitSeconds.",
                    false));
            }
            else
            {
                schedulingConflicts.Add(new SchedulingConflict("MODEL_INVALID", "ALL", $"CP-SAT returned {strict.Status}; this indicates a modelling bug.", false));
            }
        }

        var strictInfeasible = solve && problem.Items.Count > 0 && (strict is null || strict.Status == "INFEASIBLE");
        if (strictInfeasible && solverOptions.RunDiagnosticOnInfeasible)
        {
            cancellationToken.ThrowIfCancellationRequested();
            diagnostic = TimetableSolver.Solve(problem, solverOptions, Relaxation.Diagnostic, cancellationToken);
            if (diagnostic.HasSolution)
            {
                diagnosticValidation = TimetableValidator.Validate(problem, diagnostic.Cells);
            }
        }

        total.Stop();
        var report = ReportBuilder.Build(dataset, rules, solverOptions, normalization, dataConflicts, schedulingConflicts,
            strict, strictValidation, diagnostic, diagnosticValidation, skippedReason, total.ElapsedMilliseconds);
        return new TimetableRun(report, normalization, findings, strict, diagnostic);
    }
}
