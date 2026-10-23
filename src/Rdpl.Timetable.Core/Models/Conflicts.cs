namespace Rdpl.Timetable.Models;

public enum ConflictSeverity
{
    /// <summary>Informational: data was interpreted (e.g. alias applied) but nothing was dropped.</summary>
    Info,

    /// <summary>The row or item was excluded from scheduling, or the result is degraded.</summary>
    Warning,

    /// <summary>The input cannot be used at all (e.g. unreadable file, section without curriculum).</summary>
    Error,
}

/// <summary>A problem found in the INPUT data (reported in dataConflicts[]; never silently dropped).</summary>
public sealed record DataConflict(
    string Type,
    ConflictSeverity Severity,
    string Message,
    string? Section = null,
    string? Subject = null,
    string? TeacherCode = null,
    string? Source = null,
    string? RuleId = null);

/// <summary>A reason the scheduling problem cannot be solved (reported in schedulingConflicts[]).</summary>
public sealed record SchedulingConflict(
    string Code,
    string RuleId,
    string Message,
    bool ProvesInfeasibility,
    IReadOnlyList<string>? Sections = null,
    string? TeacherCode = null);

public static class DataConflictTypes
{
    public const string UnassignedTeacher = "UNASSIGNED_TEACHER";
    public const string ZeroWorkload = "ZERO_WORKLOAD";
    public const string MissingAssignment = "MISSING_ASSIGNMENT";
    public const string OrphanAssignment = "ORPHAN_ASSIGNMENT";
    public const string SectionNotFound = "SECTION_NOT_FOUND";
    public const string InvalidRow = "INVALID_ROW";
    public const string AmbiguousTeacher = "AMBIGUOUS_TEACHER";
    public const string PlaceholderSuperseded = "PLACEHOLDER_SUPERSEDED";
    public const string WorkloadMismatch = "WORKLOAD_MISMATCH";
    public const string SubjectAliasApplied = "SUBJECT_ALIAS_APPLIED";
    public const string AliasSupersededByPlaceholder = "ALIAS_SUPERSEDED_BY_PLACEHOLDER";
    public const string ActivityInstructorNotBooked = "ACTIVITY_INSTRUCTOR_NOT_BOOKED";
    public const string SectionExcludedPrePrimary = "SECTION_EXCLUDED_PRE_PRIMARY";
    public const string SectionExcludedNoCurriculum = "SECTION_EXCLUDED_NO_CURRICULUM";
    public const string CurriculumTotalMismatch = "CURRICULUM_TOTAL_MISMATCH";
    public const string ZeroCurriculumItem = "ZERO_CURRICULUM_ITEM";
    public const string TeacherNameMismatch = "TEACHER_NAME_MISMATCH";
    public const string DummyData = "DUMMY_DATA_IN_USE";
    public const string MissingInputFile = "MISSING_INPUT_FILE";
    public const string EmptyAvailability = "TEACHER_AVAILABILITY_EMPTY";
    public const string RoomRulesSkipped = "ROOM_RULES_SKIPPED";
    public const string UnknownOverride = "UNKNOWN_OVERRIDE_TARGET";
}
