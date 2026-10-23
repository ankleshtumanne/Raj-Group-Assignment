namespace Rdpl.Timetable.Models;

/// <summary>A class section from sections.json.</summary>
public sealed record Section(
    string Id,
    string DisplayName,
    string Grade,
    string SectionName,
    string CurriculumKey,
    int Capacity)
{
    private static readonly string[] PrePrimaryGrades = { "pre nursery", "pre-nursery", "nursery", "junior kg", "senior kg" };

    /// <summary>Rule P4: Pre-Nursery, Nursery, Junior KG and Senior KG are pre-primary.</summary>
    public bool IsPrePrimary => PrePrimaryGrades.Contains(Grade.Trim().ToLowerInvariant());

    /// <summary>Numeric class (1–12) when the curriculum key is "Class N", otherwise null.</summary>
    public int? ClassNumber => CurriculumKey.StartsWith("Class ", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(CurriculumKey.AsSpan(6), out var n) ? n : null;

    /// <summary>Engine rule P6 section order: Lily → Lavender → Lotus → Marigold → others.</summary>
    public int SectionOrder => SectionName.Trim().ToLowerInvariant() switch
    {
        "lily" => 0,
        "lavender" => 1,
        "lotus" => 2,
        "marigold" => 3,
        _ => 99,
    };
}

public enum CurriculumItemType
{
    Subject,
    ActivityGroup,
}

/// <summary>One row of CLASS_WISE_SUBJECTS.md for a class (curriculum key).</summary>
public sealed record CurriculumItem(
    string CurriculumKey,
    string Name,
    CurriculumItemType Type,
    int PeriodsPerDay,
    int PeriodsPerWeek,
    int SourceLine);

/// <summary>Curriculum of one class, with the weekly total the document itself declares.</summary>
public sealed record ClassCurriculum(string CurriculumKey, int? DeclaredTotalPeriodsPerWeek, IReadOnlyList<CurriculumItem> Items)
{
    public int ComputedTotal => Items.Sum(i => i.PeriodsPerWeek);
}

/// <summary>
/// One section-level teaching assignment row (TEACHER_CLASS_ALLOCATION.xlsx, sheet "Section Assignments").
/// Raw values are preserved so every row can be reported back verbatim in dataConflicts.
/// </summary>
public sealed record AssignmentRow(
    string SourceFile,
    string Sheet,
    int RowNumber,
    string SectionText,
    string ClassText,
    string ItemTypeText,
    string SubjectName,
    string TeacherCode,
    string TeacherName,
    int? WorkloadPerWeek,
    string StatusText,
    string DataOrigin)
{
    public bool IsDummy => DataOrigin.Contains("DUMMY", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Class-level assignment from TEACHER_CLASS_ASSIGNMENTS.md (used for the roster and for DUMMY generation).</summary>
public sealed record ClassLevelAssignment(
    string TeacherCode,
    string TeacherName,
    string ClassName,
    string ItemType,
    string SubjectName,
    int? PeriodsPerWeek,
    int SourceLine);

public sealed record TeacherInfo(string Code, string Name);

/// <summary>H2 input: a teacher's off-days and unavailable (day, period) cells.</summary>
public sealed record TeacherAvailability(string TeacherCode, IReadOnlySet<SchoolDay> OffDays, IReadOnlySet<(SchoolDay Day, int Period)> UnavailableSlots);

/// <summary>H6 input: an item may only be placed on these days.</summary>
public sealed record DayRestriction(string CurriculumKey, string ItemName, IReadOnlySet<SchoolDay> Days);

/// <summary>H9/AC1/AC2 input: an activity may only be placed in these cells; Period null = whole day.</summary>
public sealed record FixedSlotRule(string CurriculumKey, string ItemName, IReadOnlyList<(SchoolDay Day, int? Period)> Slots);

public sealed record SubjectAlias(string AssignmentSubject, IReadOnlyList<string> CurriculumItems, string Reason);
