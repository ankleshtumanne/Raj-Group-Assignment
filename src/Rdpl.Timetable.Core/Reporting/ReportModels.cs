using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rdpl.Timetable.Reporting;

/// <summary>Top-level output document (shape follows Candidate_Timetable_Assessment.md "Expected Output", plus diagnostics).</summary>
public sealed record TimetableReport
{
    public required bool Success { get; init; }

    public required string Outcome { get; init; }

    public required DataSourceInfo DataSource { get; init; }

    public required RulesAppliedInfo RulesApplied { get; init; }

    public required SolverInfo Solver { get; init; }

    public required SummaryInfo Summary { get; init; }

    public required IReadOnlyList<DataConflictDto> DataConflicts { get; init; }

    public required IReadOnlyList<SchedulingConflictDto> SchedulingConflicts { get; init; }

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<SectionLessonDto>>>? SectionTimetables { get; init; }

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<TeacherLessonDto>>>? TeacherTimetables { get; init; }

    public ValidationInfo? Validation { get; init; }

    public DiagnosticInfo? Diagnostic { get; init; }

    public required IReadOnlyList<RuleCoverageDto> RuleCoverage { get; init; }

    public required IReadOnlyList<string> Assumptions { get; init; }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}

public sealed record DataSourceInfo(string Kind, string? AssignmentsWorkbook, string? Warning, IReadOnlyList<string> FilesRead, IReadOnlyList<string> MissingFiles);

public sealed record RulesAppliedInfo(
    string Profile,
    bool BlockSinglesSoft,
    IReadOnlyList<string> L1SoftFor,
    IReadOnlyList<string> G2AppliesTo,
    int MaxConsecutiveSameSubject,
    string ZeroWorkloadPolicy,
    string UnstaffedPolicy,
    bool ExcludePrePrimary,
    bool ApplySubjectAliases,
    bool SoftObjective);

public sealed record SolverInfo(
    string Engine,
    string Status,
    long WallTimeMs,
    double? SolverWallTimeSeconds,
    int TimeLimitSeconds,
    double? ObjectiveValue,
    double? BestObjectiveBound,
    int Workers,
    int RandomSeed,
    string? SkippedReason,
    ModelStatsDto? Model)
{
    /// <summary>"CP-SAT" or "WARM_START" (see TimetableSolver).</summary>
    public string? SolutionSource { get; init; }

    public WarmStartDto? WarmStart { get; init; }

    /// <summary>Plain-language meaning of <see cref="Status"/>.</summary>
    public string? StatusMeaning { get; init; }
}

public sealed record WarmStartDto(bool Complete, int SectionsSolved, IReadOnlyList<string> SectionsWithoutSolution, long WallTimeMs, string Strategy);

public sealed record ModelStatsDto(int Items, int CellVariables, int PairVariables, int SingleVariables, int TeacherSlotConstraints, int AuxiliaryVariables);

public sealed record SummaryInfo(
    int SectionsTotal,
    int SectionsInScope,
    int SectionsScheduled,
    int SectionsExcluded,
    int TotalSlotsScheduled,
    int ReservedUnstaffedSlots,
    int TotalCellsFilled,
    int TeachersInvolved,
    int AssignmentRows,
    int UnassignedPlaceholderRows,
    int ZeroWorkloadRows,
    int OrphanAssignmentRows,
    int UnresolvedSectionRows,
    int SubjectItemsStaffed,
    int SubjectItemsUnstaffed,
    int ActivityItems,
    IReadOnlyDictionary<string, int> DataConflictCounts);

public sealed record DataConflictDto(string Type, string Severity, string Message, string? Section, string? Subject, string? TeacherCode, string? Source, string? RuleId);

public sealed record SchedulingConflictDto(string Code, string RuleId, string Message, bool ProvesInfeasibility, IReadOnlyList<string>? Sections, string? TeacherCode);

public sealed record SectionLessonDto(
    int Period,
    string Slot,
    string Start,
    string End,
    string Subject,
    string Type,
    string? TeacherCode,
    string? TeacherName,
    IReadOnlyList<string>? Instructors,
    string? Note);

public sealed record TeacherLessonDto(int Period, string Slot, string Section, string Subject);

public sealed record ValidationInfo(
    string Scope,
    bool HardConstraintsSatisfied,
    int HardViolationCount,
    IReadOnlyList<ViolationDto> HardViolations,
    IReadOnlyList<ViolationDto> RelaxedRuleViolations,
    long SoftConstraintPenalty,
    IReadOnlyDictionary<string, long> SoftPenaltyByRule,
    bool? ObjectiveMatchesIndependentCheck,
    IReadOnlyList<PenaltyDto> ViolatedSoftConstraints,
    IReadOnlyList<string> ChecksPerformed);

public sealed record ViolationDto(string RuleId, string Message, string? Section, string? Day, int? Period);

public sealed record PenaltyDto(string RuleId, long Count, long WeightedPenalty, IReadOnlyList<string> Examples);

public sealed record DiagnosticInfo(
    string Purpose,
    SolverInfo Solver,
    IReadOnlyList<PenaltyDto> RelaxedHardRuleViolations,
    ValidationInfo? Validation,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<SectionLessonDto>>>? SectionTimetables);

public sealed record RuleCoverageDto(string RuleId, string Tier, string Status, string Implementation);
