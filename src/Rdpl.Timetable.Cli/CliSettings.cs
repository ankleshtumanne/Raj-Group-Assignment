using System.Globalization;
using System.Text.Json;
using Rdpl.Timetable.Configuration;
using Rdpl.Timetable.Data;

namespace Rdpl.Timetable.Cli;

/// <summary>
/// Console settings. Precedence: command line > environment (TIMETABLE_*) > settings JSON > defaults.
/// No secrets are needed by the console host; nothing sensitive is read or logged.
/// </summary>
internal sealed record CliSettings
{
    public string AllowedRoot { get; init; } = Directory.GetCurrentDirectory();

    public string DataRoot { get; init; } = "data/client";

    public string? AssignmentsWorkbook { get; init; } = "data/dummy/TEACHER_CLASS_ALLOCATION.DUMMY.xlsx";

    public string AssignmentsSheet { get; init; } = AssignmentWorkbookLoader.DefaultSheetName;

    public string ConfigRoot { get; init; } = "config";

    public string OutputRoot { get; init; } = "output";

    public string? OutputFile { get; init; }

    public string Profile { get; init; } = "strict";

    public int TimeLimitSeconds { get; init; } = 120;

    public int Workers { get; init; } = 8;

    public int RandomSeed { get; init; } = 42;

    public bool RunDiagnosticOnInfeasible { get; init; } = true;

    public ZeroWorkloadPolicy ZeroWorkloadPolicy { get; init; } = ZeroWorkloadPolicy.Exclude;

    public UnstaffedPolicy UnstaffedPolicy { get; init; } = UnstaffedPolicy.ReserveSlot;

    public bool ExcludePrePrimary { get; init; } = true;

    public bool ApplySubjectAliases { get; init; } = true;

    public bool SoftObjective { get; init; } = true;

    public bool WarmStart { get; init; } = true;

    public RuleOptions ToRuleOptions() => RuleOptions.ForProfile(Profile) with
    {
        ZeroWorkloadPolicy = ZeroWorkloadPolicy,
        UnstaffedPolicy = UnstaffedPolicy,
        ExcludePrePrimary = ExcludePrePrimary,
        ApplySubjectAliases = ApplySubjectAliases,
        EnableSoftObjective = SoftObjective,
    };

    public SolverOptions ToSolverOptions() => new()
    {
        TimeLimitSeconds = TimeLimitSeconds,
        Workers = Workers,
        RandomSeed = RandomSeed,
        RunDiagnosticOnInfeasible = RunDiagnosticOnInfeasible,
        WarmStart = WarmStart,
    };

    public DatasetPaths ToDatasetPaths() => new()
    {
        AllowedRoot = AllowedRoot,
        DataRoot = DataRoot,
        AssignmentsWorkbook = AssignmentsWorkbook,
        AssignmentsSheet = AssignmentsSheet,
        ConfigRoot = ConfigRoot,
    };

    public static CliSettings Resolve(IReadOnlyDictionary<string, string?> args)
    {
        var settings = new CliSettings();
        var allowedRoot = Environment.GetEnvironmentVariable("TIMETABLE_ALLOWED_ROOT");
        if (!string.IsNullOrWhiteSpace(allowedRoot))
        {
            settings = settings with { AllowedRoot = Path.GetFullPath(allowedRoot) };
        }

        var settingsFile = args.GetValueOrDefault("settings") ?? "config/timetable.settings.json";
        var settingsPath = SafeFileAccess.ResolveInsideRoot(settings.AllowedRoot, settingsFile);
        if (File.Exists(settingsPath))
        {
            settings = ApplyJson(settings, settingsPath);
        }
        else if (args.ContainsKey("settings"))
        {
            throw new DataInputException($"Settings file '{settingsFile}' not found.");
        }

        settings = Apply(settings, key => Environment.GetEnvironmentVariable("TIMETABLE_" + key.ToUpperInvariant().Replace('-', '_')));
        return Apply(settings, key => args.TryGetValue(key, out var v) ? v ?? "true" : null);
    }

    private static CliSettings Apply(CliSettings s, Func<string, string?> get)
    {
        string? V(string key) => get(key) is { Length: > 0 } v ? v.Trim() : null;
        return s with
        {
            DataRoot = V("data-root") ?? s.DataRoot,
            AssignmentsWorkbook = V("assignments") is { } a ? (a.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : a) : s.AssignmentsWorkbook,
            AssignmentsSheet = V("assignments-sheet") ?? s.AssignmentsSheet,
            ConfigRoot = V("config-root") ?? s.ConfigRoot,
            OutputRoot = V("output-root") ?? s.OutputRoot,
            OutputFile = V("out") ?? s.OutputFile,
            Profile = V("profile") ?? s.Profile,
            TimeLimitSeconds = V("time-limit") is { } t ? ParseInt(t, "time-limit") : s.TimeLimitSeconds,
            Workers = V("workers") is { } w ? ParseInt(w, "workers") : s.Workers,
            RandomSeed = V("seed") is { } r ? ParseInt(r, "seed") : s.RandomSeed,
            RunDiagnosticOnInfeasible = V("no-diagnostic") is { } nd ? !ParseBool(nd, "no-diagnostic") : s.RunDiagnosticOnInfeasible,
            ZeroWorkloadPolicy = V("zero-workload") is { } z ? ParseEnum<ZeroWorkloadPolicy>(z, "zero-workload") : s.ZeroWorkloadPolicy,
            UnstaffedPolicy = V("unstaffed") is { } u ? ParseEnum<UnstaffedPolicy>(u, "unstaffed") : s.UnstaffedPolicy,
            ExcludePrePrimary = V("include-pre-primary") is { } ip ? !ParseBool(ip, "include-pre-primary") : s.ExcludePrePrimary,
            ApplySubjectAliases = V("no-aliases") is { } na ? !ParseBool(na, "no-aliases") : s.ApplySubjectAliases,
            SoftObjective = V("no-soft") is { } ns ? !ParseBool(ns, "no-soft") : s.SoftObjective,
            WarmStart = V("no-warm-start") is { } nw ? !ParseBool(nw, "no-warm-start") : s.WarmStart,
        };
    }

    private static CliSettings ApplyJson(CliSettings s, string path)
    {
        using var doc = JsonDocument.Parse(SafeFileAccess.ReadAllText(path, SafeFileAccess.MaxJsonBytes), new JsonDocumentOptions { MaxDepth = 16, CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;
        string? Str(params string[] keys)
        {
            var e = root;
            foreach (var k in keys)
            {
                if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(k, out e))
                {
                    return null;
                }
            }

            return e.ValueKind switch
            {
                JsonValueKind.String => e.GetString(),
                JsonValueKind.Number => e.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null,
            };
        }

        var map = new Dictionary<string, string?>
        {
            ["data-root"] = Str("DataRoot"),
            ["assignments"] = Str("AssignmentsWorkbook"),
            ["config-root"] = Str("ConfigRoot"),
            ["output-root"] = Str("OutputRoot"),
            ["profile"] = Str("Rules", "Profile"),
            ["time-limit"] = Str("Solver", "TimeLimitSeconds"),
            ["workers"] = Str("Solver", "Workers"),
            ["seed"] = Str("Solver", "RandomSeed"),
            ["zero-workload"] = Str("Rules", "ZeroWorkloadPolicy"),
            ["unstaffed"] = Str("Rules", "UnstaffedPolicy"),
            ["no-diagnostic"] = Str("Rules", "RunDiagnosticOnInfeasible") is { } d ? (bool.Parse(d) ? "false" : "true") : null,
            ["include-pre-primary"] = Str("Rules", "ExcludePrePrimary") is { } e ? (bool.Parse(e) ? "false" : "true") : null,
            ["no-aliases"] = Str("Matching", "ApplySubjectAliases") is { } al ? (bool.Parse(al) ? "false" : "true") : null,
            ["no-warm-start"] = Str("Solver", "WarmStart") is { } ws ? (bool.Parse(ws) ? "false" : "true") : null,
        };
        return Apply(s, key => map.GetValueOrDefault(key));
    }

    private static int ParseInt(string value, string name) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : throw new ArgumentException($"--{name} must be an integer.");

    private static bool ParseBool(string value, string name) =>
        bool.TryParse(value, out var v) ? v : throw new ArgumentException($"--{name} must be true or false.");

    private static T ParseEnum<T>(string value, string name)
        where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var v) && Enum.IsDefined(v)
            ? v
            : throw new ArgumentException($"--{name} must be one of: {string.Join(", ", Enum.GetNames<T>())}.");
}
