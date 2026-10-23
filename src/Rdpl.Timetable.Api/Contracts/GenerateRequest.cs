using Rdpl.Timetable.Api.Options;
using Rdpl.Timetable.Configuration;

namespace Rdpl.Timetable.Api.Contracts;

/// <summary>
/// Body of POST /api/timetable/generate and /load-data. Every field is optional and bounded.
/// There is deliberately no field for file paths: the API only ever reads server-configured files.
/// </summary>
public sealed record GenerateRequest
{
    /// <summary>"strict" (every Tier-1/Tier-2 rule hard) or "pragmatic" (H5 soft, L1 soft for Class 11/12).</summary>
    public string? Profile { get; init; }

    public int? TimeLimitSeconds { get; init; }

    public int? Workers { get; init; }

    public int? RandomSeed { get; init; }

    /// <summary>"Exclude" (default, H10) or "TreatAsAssigned".</summary>
    public string? ZeroWorkloadPolicy { get; init; }

    /// <summary>"ReserveSlot" (default) or "Exclude".</summary>
    public string? UnstaffedPolicy { get; init; }

    public bool? SoftObjective { get; init; }

    public bool? RunDiagnosticOnInfeasible { get; init; }
}

/// <summary>Validated, normalized form of <see cref="GenerateRequest"/>.</summary>
public sealed record RunSettings(RuleOptions Rules, SolverOptions Solver);

public static class GenerateRequestValidator
{
    private static readonly string[] Profiles = { "strict", "pragmatic" };

    /// <summary>Returns validation errors (field → messages) or the resolved settings.</summary>
    public static (RunSettings? Settings, Dictionary<string, string[]> Errors) Validate(GenerateRequest? request, TimetableApiOptions options)
    {
        request ??= new GenerateRequest();
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var profile = (request.Profile ?? "strict").Trim().ToLowerInvariant();
        if (!Profiles.Contains(profile))
        {
            errors[nameof(GenerateRequest.Profile)] = new[] { "Must be 'strict' or 'pragmatic'." };
        }

        var maxLimit = Math.Min(options.MaxTimeLimitSeconds, SolverOptions.MaxTimeLimitSeconds);
        var timeLimit = request.TimeLimitSeconds ?? options.DefaultTimeLimitSeconds;
        if (timeLimit < 1 || timeLimit > maxLimit)
        {
            errors[nameof(GenerateRequest.TimeLimitSeconds)] = new[] { $"Must be between 1 and {maxLimit}." };
        }

        var maxWorkers = Math.Min(options.MaxWorkers, SolverOptions.MaxWorkers);
        var workers = request.Workers ?? options.DefaultWorkers;
        if (workers < 1 || workers > maxWorkers)
        {
            errors[nameof(GenerateRequest.Workers)] = new[] { $"Must be between 1 and {maxWorkers}." };
        }

        var seed = request.RandomSeed ?? options.RandomSeed;
        if (seed < 0)
        {
            errors[nameof(GenerateRequest.RandomSeed)] = new[] { "Must be zero or positive." };
        }

        var zero = ParseEnum(request.ZeroWorkloadPolicy, ZeroWorkloadPolicy.Exclude, nameof(GenerateRequest.ZeroWorkloadPolicy), errors);
        var unstaffed = ParseEnum(request.UnstaffedPolicy, UnstaffedPolicy.ReserveSlot, nameof(GenerateRequest.UnstaffedPolicy), errors);

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        var rules = RuleOptions.ForProfile(profile) with
        {
            ZeroWorkloadPolicy = zero,
            UnstaffedPolicy = unstaffed,
            EnableSoftObjective = request.SoftObjective ?? true,
        };
        var solver = new SolverOptions
        {
            TimeLimitSeconds = timeLimit,
            Workers = workers,
            RandomSeed = seed,
            RunDiagnosticOnInfeasible = request.RunDiagnosticOnInfeasible ?? true,
        };
        return (new RunSettings(rules, solver), errors);
    }

    private static T ParseEnum<T>(string? value, T fallback, string field, Dictionary<string, string[]> errors)
        where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (value.Length <= 40 && Enum.TryParse<T>(value.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        errors[field] = new[] { $"Must be one of: {string.Join(", ", Enum.GetNames<T>())}." };
        return fallback;
    }
}
