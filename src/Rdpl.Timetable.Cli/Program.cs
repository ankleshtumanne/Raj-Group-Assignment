using System.Globalization;
using Rdpl.Timetable;
using Rdpl.Timetable.Cli;
using Rdpl.Timetable.Data;
using Rdpl.Timetable.Reporting;

// Exit codes: 0 timetable generated and independently validated · 2 infeasible / no timetable · 3 solution failed validation
//             4 input data error · 64 usage error · 1 unexpected error.
const string Usage = """
    rdpl-timetable — full-school CP-SAT timetable solver

    Commands
      generate                     Load data, solve, validate, write the JSON report.
      validate-data                Load + normalize + pre-solve analysis only (no solver).
      generate-dummy-assignments   Write the clearly-labelled DUMMY section-level workbook.

    Options (also settable via env TIMETABLE_<NAME>, e.g. TIMETABLE_TIME_LIMIT=300, or config/timetable.settings.json)
      --data-root <dir>            Client package folder              (default data/client)
      --assignments <xlsx|none>    Section assignments workbook       (default data/dummy/TEACHER_CLASS_ALLOCATION.DUMMY.xlsx)
      --assignments-sheet <name>   Sheet name                         (default "Section Assignments")
      --config-root <dir>          Aliases / availability / overrides (default config)
      --out <file>                 Report file inside the output folder (default output/timetable-<profile>.json)
      --profile strict|pragmatic   Rule profile                       (default strict)
      --time-limit <s>             CP-SAT time limit, 1–1800          (default 120)
      --workers <n>                CP-SAT workers, 1–64               (default 8)
      --seed <n>                   Random seed                        (default 42)
      --zero-workload Exclude|TreatAsAssigned
      --unstaffed ReserveSlot|Exclude
      --no-diagnostic              Do not run the relaxed diagnostic solve when infeasible
      --no-aliases                 Disable config/subject-aliases.json
      --no-soft                    Disable the soft objective
      --no-warm-start              Skip the sequential per-section warm-start hint
      --settings <file>            Settings JSON (default config/timetable.settings.json)
    """;

try
{
    if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
    {
        Console.WriteLine(Usage);
        return args.Length == 0 ? 64 : 0;
    }

    var command = args[0].Trim().ToLowerInvariant();
    var options = ParseOptions(args.Skip(1).ToArray());
    var settings = CliSettings.Resolve(options);

    switch (command)
    {
        case "generate":
        case "validate-data":
        {
            var solve = command == "generate";
            var dataset = DatasetLoader.Load(settings.ToDatasetPaths());
            var rules = settings.ToRuleOptions();
            var solver = settings.ToSolverOptions();
            if (dataset.AssignmentsAreDummy)
            {
                Console.Error.WriteLine("WARNING: assignments workbook is DUMMY data (derived from class-level TEACHER_CLASS_ASSIGNMENTS.md). Output is NOT a client timetable.");
            }

            Console.WriteLine($"Loaded {dataset.Sections.Count} sections, {dataset.Curricula.Count} curricula, {dataset.AssignmentRows.Count} assignment rows. Profile '{rules.Profile}', time limit {solver.TimeLimitSeconds}s.");
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            var run = TimetableEngine.Run(dataset, rules, solver, solve, cts.Token);
            var defaultName = solve ? $"timetable-{rules.Profile}.json" : "data-validation.json";
            var outputRoot = SafeFileAccess.ResolveInsideRoot(settings.AllowedRoot, settings.OutputRoot);
            var outputPath = SafeFileAccess.ResolveInsideRoot(outputRoot, settings.OutputFile ?? defaultName);
            if (!outputPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("--out must be a .json file.");
            }

            SafeFileAccess.WriteAllTextAtomic(outputPath, run.Report.ToJson());
            PrintSummary(run.Report, Path.GetRelativePath(settings.AllowedRoot, outputPath));
            return run.Report.Outcome switch
            {
                "TIMETABLE_GENERATED" or "DATA_VALIDATION_ONLY" => 0,
                "VALIDATION_FAILED" => 3,
                _ => 2,
            };
        }

        case "generate-dummy-assignments":
        {
            var paths = settings.ToDatasetPaths();
            var dataRoot = SafeFileAccess.ResolveInsideRoot(settings.AllowedRoot, paths.DataRoot);
            var sectionsPath = new[] { "datasets", "candidate-data", "." }.Select(f => Path.Combine(dataRoot, f, "sections.json")).FirstOrDefault(File.Exists)
                               ?? throw new DataInputException("sections.json not found.");
            var mdPath = Path.Combine(dataRoot, "TEACHER_CLASS_ASSIGNMENTS.md");
            var sections = JsonInputLoader.LoadSections(sectionsPath);
            var classLevel = ClassAssignmentMarkdownParser.Parse(SafeFileAccess.ReadAllText(mdPath, SafeFileAccess.MaxMarkdownBytes));
            var target = SafeFileAccess.ResolveInsideRoot(settings.AllowedRoot, options.GetValueOrDefault("out") ?? "data/dummy/TEACHER_CLASS_ALLOCATION.DUMMY.xlsx");
            if (!Path.GetFileName(target).Contains("DUMMY", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The dummy workbook file name must contain 'DUMMY' so it can never be mistaken for client data.");
            }

            var summary = DummyAssignmentGenerator.Generate(sections, classLevel, target, settings.ToRuleOptions().PlaceholderTeacherCodes);
            Console.WriteLine($"DUMMY workbook written: {Path.GetRelativePath(settings.AllowedRoot, target)}");
            Console.WriteLine($"  rows={summary.Rows} placeholderRows={summary.PlaceholderRows} zeroWorkloadRows={summary.ZeroWorkloadRows} roundRobinGroups={summary.RoundRobinGroups}");
            Console.WriteLine("  (client brief: 587 rows / 92 placeholders / 142 zero-workload — the dummy is NOT expected to match)");
            return 0;
        }

        default:
            Console.Error.WriteLine($"Unknown command '{command}'.");
            Console.Error.WriteLine(Usage);
            return 64;
    }
}
catch (DataInputException ex)
{
    Console.Error.WriteLine($"INPUT ERROR: {ex.Message}");
    return 4;
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"USAGE ERROR: {ex.Message}");
    return 64;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 1;
}
catch (Exception ex)
{
    // Unexpected: show type + message only (no stack trace / internal paths unless DEBUG is requested).
    Console.Error.WriteLine($"UNEXPECTED ERROR: {ex.GetType().Name}: {ex.Message}");
    if (Environment.GetEnvironmentVariable("TIMETABLE_DEBUG") == "1")
    {
        Console.Error.WriteLine(ex);
    }

    return 1;
}

static Dictionary<string, string?> ParseOptions(string[] tokens)
{
    var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < tokens.Length; i++)
    {
        var token = tokens[i];
        if (!token.StartsWith("--", StringComparison.Ordinal) || token.Length < 3)
        {
            throw new ArgumentException($"Unexpected argument '{token}'.");
        }

        var key = token[2..];
        if (key.Length > 40 || !key.All(c => char.IsLetterOrDigit(c) || c == '-'))
        {
            throw new ArgumentException("Invalid option name.");
        }

        string? value = null;
        if (i + 1 < tokens.Length && !tokens[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = tokens[++i];
            if (value.Length > 400)
            {
                throw new ArgumentException($"Value for --{key} is too long.");
            }
        }

        result[key] = value;
    }

    return result;
}

static void PrintSummary(TimetableReport report, string outputPath)
{
    var s = report.Summary;
    Console.WriteLine();
    Console.WriteLine($"Outcome   : {report.Outcome} (success={report.Success.ToString().ToLowerInvariant()})");
    Console.WriteLine($"Data      : {report.DataSource.Kind}");
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"Solver    : {report.Solver.Status} · wall {report.Solver.WallTimeMs} ms · limit {report.Solver.TimeLimitSeconds}s · objective {report.Solver.ObjectiveValue?.ToString(CultureInfo.InvariantCulture) ?? "-"}"));
    if (report.Solver.WarmStart is { } ws)
    {
        Console.WriteLine($"Warm start: {ws.SectionsSolved} sections in {ws.WallTimeMs} ms (complete={ws.Complete.ToString().ToLowerInvariant()}); solution source: {report.Solver.SolutionSource ?? "-"}");
    }

    if (report.Solver.SkippedReason is not null)
    {
        Console.WriteLine($"            {report.Solver.SkippedReason}");
    }

    Console.WriteLine($"Sections  : {s.SectionsScheduled}/{s.SectionsInScope} scheduled ({s.SectionsTotal} in sections.json, {s.SectionsExcluded} excluded)");
    Console.WriteLine($"Slots     : {s.TotalSlotsScheduled} lessons + {s.ReservedUnstaffedSlots} reserved (unstaffed) = {s.TotalCellsFilled}");
    Console.WriteLine($"Rows      : {s.AssignmentRows} assignment rows · {s.UnassignedPlaceholderRows} UNASSIGNED-TT · {s.ZeroWorkloadRows} zero-workload · {s.OrphanAssignmentRows} orphan");
    Console.WriteLine($"Items     : {s.SubjectItemsStaffed} staffed subjects · {s.SubjectItemsUnstaffed} unstaffed subjects · {s.ActivityItems} activities");
    if (report.Validation is { } v)
    {
        Console.WriteLine($"Validation: hardConstraintsSatisfied={v.HardConstraintsSatisfied.ToString().ToLowerInvariant()} · hard violations {v.HardViolationCount} · soft penalty {v.SoftConstraintPenalty} · objective matches independent check: {v.ObjectiveMatchesIndependentCheck?.ToString().ToLowerInvariant() ?? "n/a"}");
    }

    var blocking = report.SchedulingConflicts.Where(c => c.ProvesInfeasibility).ToList();
    if (blocking.Count > 0)
    {
        Console.WriteLine($"Blocking  : {blocking.Count} scheduling conflict(s); first ones:");
        foreach (var c in blocking.GroupBy(c => c.Code).SelectMany(g => g.Take(3)))
        {
            Console.WriteLine($"  [{c.Code}/{c.RuleId}] {c.Message}");
        }
    }

    if (report.Diagnostic is { } d)
    {
        Console.WriteLine($"Diagnostic: {d.Solver.Status} · relaxed-rule violations: {string.Join(", ", d.RelaxedHardRuleViolations.Select(p => $"{p.RuleId}×{p.Count}"))}");
    }

    Console.WriteLine($"Report    : {outputPath}");
}
