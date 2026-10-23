using Rdpl.Timetable.Configuration;

namespace Rdpl.Timetable.Models;

public enum LessonKind
{
    /// <summary>Curriculum subject with a formally assigned teacher (teacher is booked — rule H1).</summary>
    Subject,

    /// <summary>Activity group: instructors are NOT booked (rules T6 / AC8).</summary>
    Activity,

    /// <summary>
    /// Curriculum subject without a schedulable teacher (UNASSIGNED-TT, zero workload, or no row).
    /// Its slots are reserved so the week stays complete (H3/H8), but it is NOT a lesson and carries
    /// no teacher (DATA-1). Reported in dataConflicts.
    /// </summary>
    ReservedUnstaffed,
}

/// <summary>
/// A normalized unit of weekly demand: one curriculum item for one section, with its resolved teacher.
/// This is what the CP-SAT model schedules.
/// </summary>
public sealed class LessonItem
{
    public LessonItem(
        int index,
        Section section,
        string name,
        CurriculumItemType curriculumType,
        LessonKind kind,
        int periodsPerWeek,
        int periodsPerDay,
        string? teacherCode,
        string? teacherName,
        IReadOnlyList<TeacherInfo> instructors,
        string? unstaffedReason,
        IReadOnlySet<(SchoolDay Day, int Period)> allowedSlots)
    {
        if (periodsPerWeek <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(periodsPerWeek), "H10: items with periodsPerWeek <= 0 must be excluded before modelling.");
        }

        if (periodsPerDay <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(periodsPerDay));
        }

        Index = index;
        Section = section;
        Name = name;
        CurriculumType = curriculumType;
        Kind = kind;
        PeriodsPerWeek = periodsPerWeek;
        PeriodsPerDay = periodsPerDay;
        TeacherCode = teacherCode;
        TeacherName = teacherName;
        Instructors = instructors;
        UnstaffedReason = unstaffedReason;
        AllowedSlots = allowedSlots;
        FirstWordKey = SubjectNames.FirstWordKey(name);
        IsGames = SubjectNames.HasToken(name, "games");
        IsLibrary = SubjectNames.HasToken(name, "library");
        IsMathematics = SubjectNames.IsMathematics(name);
    }

    public int Index { get; }

    public Section Section { get; }

    public string Name { get; }

    public CurriculumItemType CurriculumType { get; }

    public LessonKind Kind { get; }

    public int PeriodsPerWeek { get; }

    public int PeriodsPerDay { get; }

    /// <summary>Engine rule A1/B1: periodsPerDay ≥ 2 makes this a block subject placed in slot pairs.</summary>
    public bool IsBlock => PeriodsPerDay >= 2;

    public string? TeacherCode { get; }

    public string? TeacherName { get; }

    /// <summary>Activity instructors (informational only — T6).</summary>
    public IReadOnlyList<TeacherInfo> Instructors { get; }

    public string? UnstaffedReason { get; }

    /// <summary>(day, period) cells this item may use after H2 / H6 / H9 / G1 filtering.</summary>
    public IReadOnlySet<(SchoolDay Day, int Period)> AllowedSlots { get; }

    /// <summary>Rule L1/B4 key: first token after trim, case-insensitive.</summary>
    public string FirstWordKey { get; }

    public bool IsGames { get; }

    public bool IsLibrary { get; }

    public bool IsGamesOrLibrary => IsGames || IsLibrary;

    public bool IsMathematics { get; }

    /// <summary>Only regular subjects with a real teacher occupy that teacher (H1). Activities and reserved slots do not.</summary>
    public bool BooksTeacher => Kind == LessonKind.Subject && TeacherCode is not null;

    public override string ToString() => $"{Section.DisplayName} · {Name} ({Kind}, {PeriodsPerWeek}/wk, ≤{PeriodsPerDay}/day, teacher={TeacherCode ?? "-"})";
}

/// <summary>The fully normalized problem handed to the constraint model builder.</summary>
public sealed class SchedulingProblem
{
    public SchedulingProblem(
        BellSchedule bell,
        IReadOnlyList<Section> sections,
        IReadOnlyList<LessonItem> items,
        IReadOnlyDictionary<string, TeacherInfo> teachers,
        RuleOptions rules)
    {
        Bell = bell;
        Sections = sections;
        Items = items;
        Teachers = teachers;
        Rules = rules;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Index != i)
            {
                throw new ArgumentException("LessonItem.Index must equal its position in Items.", nameof(items));
            }
        }
    }

    public BellSchedule Bell { get; }

    public IReadOnlyList<Section> Sections { get; }

    public IReadOnlyList<LessonItem> Items { get; }

    public IReadOnlyDictionary<string, TeacherInfo> Teachers { get; }

    public RuleOptions Rules { get; }

    public IEnumerable<LessonItem> ItemsFor(Section section) => Items.Where(i => i.Section.Id == section.Id);
}

/// <summary>Name helpers shared by the model builder and the independent validator.</summary>
public static class SubjectNames
{
    public static string Normalize(string? name) =>
        string.Join(' ', (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    /// <summary>L1: "first word = first token after trim, case-insensitive".</summary>
    public static string FirstWordKey(string name)
    {
        var tokens = name.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length == 0 ? string.Empty : tokens[0].ToLowerInvariant();
    }

    /// <summary>True if any '/'-separated option of the name equals the token (e.g. "Games / Library" has "games").</summary>
    public static bool HasToken(string name, string token) =>
        name.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(o => string.Equals(o, token, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> Options(string name) =>
        name.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool IsMathematics(string name)
    {
        var n = Normalize(name);
        return n == "mathematics" || n.StartsWith("mathematics ", StringComparison.Ordinal) || n == "maths" || n == "math";
    }
}
