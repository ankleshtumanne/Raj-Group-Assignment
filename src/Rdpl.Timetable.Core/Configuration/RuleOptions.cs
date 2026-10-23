namespace Rdpl.Timetable.Configuration;

public enum ZeroWorkloadPolicy
{
    /// <summary>Default — assessment Tier-1 H10 says "Zero-workload assignments excluded".</summary>
    Exclude,

    /// <summary>What-if: keep the teacher link and take demand from the curriculum (still reported).</summary>
    TreatAsAssigned,
}

public enum UnstaffedPolicy
{
    /// <summary>
    /// Default: keep the curriculum item in the model as a RESERVED_UNSTAFFED block with no teacher,
    /// so every section still gets its 52 slots (H3/H8) and the slot is ready once a teacher is assigned.
    /// </summary>
    ReserveSlot,

    /// <summary>Drop the item entirely; affected sections then have fewer than 52 periods and H3 is relaxed to AtMostOne for them.</summary>
    Exclude,
}

/// <summary>Weights of the soft objective (Tier 3) and of diagnostic relaxations.</summary>
public sealed record SoftWeights
{
    /// <summary>PR-MATH: per Mathematics period outside periods 1–4 of Mon–Thu (scheduling-rules.json penaltyWeight 10).</summary>
    public int MathOutsideMorning { get; init; } = 10;

    /// <summary>DB-RULE max_one_games_per_day (penaltyWeight 8) — per Games period above 1 per day.</summary>
    public int GamesAboveOnePerDay { get; init; } = 8;

    /// <summary>DB-RULE max_one_library_per_day (penaltyWeight 8).</summary>
    public int LibraryAboveOnePerDay { get; init; } = 8;

    /// <summary>DB-RULE max_two_consecutive_games (penaltyWeight 8) — per 3-slot window exceeding 2.</summary>
    public int GamesConsecutiveAboveTwo { get; init; } = 8;

    /// <summary>H5 in profile "pragmatic": per lone single period of a block subject (minimised).</summary>
    public int LoneBlockSingle { get; init; } = 300;

    /// <summary>Used only when L1 is relaxed for a grade (profile "pragmatic") — per extra same-first-word item on a day.</summary>
    public int L1Relaxed { get; init; } = 500;
}

/// <summary>
/// Which rules are enforced and how. Defaults = profile "strict": every assessment Tier-1/Tier-2 rule is hard.
/// Any relaxation must be switched on explicitly and is echoed in the output under "rulesApplied".
/// </summary>
public sealed record RuleOptions
{
    public string Profile { get; init; } = "strict";

    /// <summary>
    /// H5/B1. Strict (false): block subjects (ppd ≥ 2) appear only as full slot pairs — impossible for an odd
    /// periodsPerWeek, and impossible when a section has too few single-period subjects to fill half-pairs.
    /// When true ("pragmatic"), a block subject may appear as a lone single, each one penalised with
    /// <see cref="SoftWeights.LoneBlockSingle"/>; CP-SAT minimises their number. This is what the production
    /// engine does in force-fill pass A ("block subjects can be placed as singles"), but optimally.
    /// </summary>
    public bool BlockSinglesSoft { get; init; }

    /// <summary>Curriculum keys (e.g. "Class 11") for which L1 is a heavily penalised soft rule instead of hard.</summary>
    public IReadOnlySet<string> L1SoftCurriculumKeys { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>G2 applies to these curriculum keys.</summary>
    public IReadOnlySet<string> G2CurriculumKeys { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Class 11", "Class 12" };

    /// <summary>L3: maximum run of consecutive slots with the same subject (breaks interrupt the run).</summary>
    public int MaxConsecutiveSameSubject { get; init; } = 3;

    public bool EnableSoftObjective { get; init; } = true;

    public SoftWeights Weights { get; init; } = new();

    public ZeroWorkloadPolicy ZeroWorkloadPolicy { get; init; } = ZeroWorkloadPolicy.Exclude;

    public UnstaffedPolicy UnstaffedPolicy { get; init; } = UnstaffedPolicy.ReserveSlot;

    /// <summary>P4: exclude Pre-Nursery/Nursery/Junior KG/Senior KG (also: they have no curriculum in the dataset).</summary>
    public bool ExcludePrePrimary { get; init; } = true;

    public bool ApplySubjectAliases { get; init; } = true;

    public IReadOnlySet<string> PlaceholderTeacherCodes { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "UNASSIGNED-TT" };

    public static RuleOptions ForProfile(string? profile) => (profile ?? "strict").Trim().ToLowerInvariant() switch
    {
        "strict" => new RuleOptions { Profile = "strict" },
        "pragmatic" => new RuleOptions
        {
            Profile = "pragmatic",
            BlockSinglesSoft = true,
            L1SoftCurriculumKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Class 11", "Class 12" },
        },
        _ => throw new ArgumentException($"Unknown rule profile '{profile}'. Use 'strict' or 'pragmatic'.", nameof(profile)),
    };
}

/// <summary>CP-SAT run parameters (bounded so an API caller cannot request unbounded work).</summary>
public sealed record SolverOptions
{
    public const int MaxTimeLimitSeconds = 1800;
    public const int MaxWorkers = 64;

    public int TimeLimitSeconds { get; init; } = 120;

    public int Workers { get; init; } = 8;

    public int RandomSeed { get; init; } = 42;

    /// <summary>When strict solving is infeasible, run a relaxed "diagnostic" solve that pinpoints the violations.</summary>
    public bool RunDiagnosticOnInfeasible { get; init; } = true;

    /// <summary>
    /// Warm start: solve the sections one at a time (most teacher-constrained first, each blocking the cells its
    /// teachers already use) and pass the combined timetable to the full joint model as a CP-SAT solution hint.
    /// The full model still decides; the hint only gives the search a good starting point. Uses at most
    /// <see cref="WarmStartBudgetFraction"/> of the time limit.
    /// </summary>
    public bool WarmStart { get; init; } = true;

    public int WarmStartSecondsPerSection { get; init; } = 3;

    public double WarmStartBudgetFraction { get; init; } = 0.5;

    public void Validate()
    {
        if (WarmStartSecondsPerSection is < 1 or > 60)
        {
            throw new ArgumentOutOfRangeException(nameof(WarmStartSecondsPerSection), "WarmStartSecondsPerSection must be between 1 and 60.");
        }

        if (WarmStartBudgetFraction is < 0.05 or > 0.9)
        {
            throw new ArgumentOutOfRangeException(nameof(WarmStartBudgetFraction), "WarmStartBudgetFraction must be between 0.05 and 0.9.");
        }

        if (TimeLimitSeconds is < 1 or > MaxTimeLimitSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(TimeLimitSeconds), $"TimeLimitSeconds must be between 1 and {MaxTimeLimitSeconds}.");
        }

        if (Workers is < 1 or > MaxWorkers)
        {
            throw new ArgumentOutOfRangeException(nameof(Workers), $"Workers must be between 1 and {MaxWorkers}.");
        }
    }
}
