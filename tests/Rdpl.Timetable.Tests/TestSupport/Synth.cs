using Rdpl.Timetable.Configuration;
using Rdpl.Timetable.Data;
using Rdpl.Timetable.Models;
using Rdpl.Timetable.Normalization;
using Rdpl.Timetable.Solver;

namespace Rdpl.Timetable.Tests.TestSupport;

/// <summary>
/// Builds small SYNTHETIC school datasets (clearly not client data) so each rule can be tested in isolation through
/// the same pipeline as the real dataset: <see cref="ProblemBuilder"/> → CP-SAT → <see cref="Validation.TimetableValidator"/>.
/// </summary>
internal sealed class Synth
{
    private readonly List<Section> _sections = new();
    private readonly Dictionary<string, List<CurriculumItem>> _curricula = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AssignmentRow> _rows = new();
    private readonly List<TeacherAvailability> _availability = new();
    private readonly List<DayRestriction> _restrictions = new();
    private readonly List<FixedSlotRule> _fixedSlots = new();
    private int _row = 2;

    public Synth(BellSchedule bell) => Bell = bell;

    public BellSchedule Bell { get; }

    /// <summary>A small week: <paramref name="days"/> days × 4 periods, short break after period 2 → pairs (1,2) and (3,4).</summary>
    public static BellSchedule SmallBell(int days = 2, int periodsPerDay = 4)
    {
        var list = new List<DaySchedule>();
        for (var d = 1; d <= days; d++)
        {
            var periods = Enumerable.Range(1, periodsPerDay)
                .Select(p => new TeachingPeriod(p, $"{(p + 1) / 2}{(p % 2 == 1 ? "A" : "B")}", new TimeOnly(7 + p, 0), new TimeOnly(7 + p, 40)))
                .ToList();
            var breaks = new HashSet<int>(Enumerable.Range(1, periodsPerDay - 1).Where(p => p % 2 == 0));
            list.Add(new DaySchedule((SchoolDay)d, periods, breaks, new[] { 1, 2 }));
        }

        return new BellSchedule("synthetic", list);
    }

    public Synth Section(string curriculumKey, string name)
    {
        var number = curriculumKey.Replace("Class ", string.Empty, StringComparison.Ordinal);
        _sections.Add(new Section($"class-{number}-{name.ToLowerInvariant()}", $"Grade {number} - {name}", number, name, curriculumKey, 40));
        return this;
    }

    public Synth PrePrimarySection(string grade, string name)
    {
        _sections.Add(new Section($"{grade.ToLowerInvariant().Replace(' ', '-')}-{name.ToLowerInvariant()}", $"{grade} - {name}", grade, name, grade, 35));
        return this;
    }

    public Synth Subject(string curriculumKey, string name, int ppd, int ppw) => Item(curriculumKey, name, CurriculumItemType.Subject, ppd, ppw);

    public Synth Activity(string curriculumKey, string name, int ppd, int ppw) => Item(curriculumKey, name, CurriculumItemType.ActivityGroup, ppd, ppw);

    /// <summary>Section-level assignment row (section display name, e.g. "Grade 5 - Lily").</summary>
    public Synth Assign(string sectionDisplayName, string subject, string teacherCode, int? workload = 5, string? teacherName = null)
    {
        _rows.Add(new AssignmentRow("synthetic.xlsx", "Section Assignments", _row++, sectionDisplayName, string.Empty, "Subject", subject, teacherCode, teacherName ?? $"Teacher {teacherCode}", workload, "ACTIVE", "SYNTHETIC TEST DATA"));
        return this;
    }

    public Synth OffDay(string teacherCode, params SchoolDay[] days)
    {
        _availability.Add(new TeacherAvailability(teacherCode, days.ToHashSet(), new HashSet<(SchoolDay, int)>()));
        return this;
    }

    public Synth Unavailable(string teacherCode, params (SchoolDay Day, int Period)[] cells)
    {
        _availability.Add(new TeacherAvailability(teacherCode, new HashSet<SchoolDay>(), cells.ToHashSet()));
        return this;
    }

    public Synth OnlyOn(string curriculumKey, string item, params SchoolDay[] days)
    {
        _restrictions.Add(new DayRestriction(curriculumKey, item, days.ToHashSet()));
        return this;
    }

    public Synth Fixed(string curriculumKey, string item, params (SchoolDay Day, int? Period)[] slots)
    {
        _fixedSlots.Add(new FixedSlotRule(curriculumKey, item, slots));
        return this;
    }

    public LoadedDataset Dataset() => new(
        _sections,
        Bell,
        Bell.WeeklyTeachingSlots,
        _curricula.ToDictionary(kv => kv.Key, kv => new ClassCurriculum(kv.Key, kv.Value.Sum(i => i.PeriodsPerWeek), kv.Value), StringComparer.OrdinalIgnoreCase),
        null,
        _rows,
        false,
        "synthetic.xlsx",
        _availability,
        _restrictions,
        _fixedSlots,
        Array.Empty<SubjectAlias>(),
        new Dictionary<string, IReadOnlyList<string>>(),
        Array.Empty<DataConflict>(),
        Array.Empty<string>());

    public NormalizationResult Normalize(RuleOptions? rules = null) => ProblemBuilder.Build(Dataset(), rules ?? new RuleOptions());

    public TimetableRun Run(RuleOptions? rules = null, int timeLimitSeconds = 20) =>
        TimetableEngine.Run(Dataset(), rules ?? new RuleOptions(), Fast(timeLimitSeconds), solve: true);

    public static SolverOptions Fast(int timeLimitSeconds = 20) => new()
    {
        TimeLimitSeconds = timeLimitSeconds,
        Workers = Math.Clamp(Environment.ProcessorCount, 1, 8),
        RandomSeed = 7,
    };

    private Synth Item(string key, string name, CurriculumItemType type, int ppd, int ppw)
    {
        if (!_curricula.TryGetValue(key, out var list))
        {
            list = new List<CurriculumItem>();
            _curricula[key] = list;
        }

        list.Add(new CurriculumItem(key, name, type, ppd, ppw, list.Count + 1));
        return this;
    }
}

internal static class RunExtensions
{
    /// <summary>(item, day, period) of every placed cell of the strict solution.</summary>
    public static IReadOnlyList<(LessonItem Item, SchoolDay Day, int Period)> Cells(this TimetableRun run)
    {
        Assert.NotNull(run.Strict);
        Assert.True(run.Strict!.HasSolution, $"Expected a solution but solver status was {run.Strict.Status}.");
        return run.Strict.Cells.Select(c => (run.Normalization.Problem.Items[c.ItemIndex], c.Day, c.Period)).ToList();
    }

    public static IEnumerable<(LessonItem Item, SchoolDay Day, int Period)> Of(this IEnumerable<(LessonItem Item, SchoolDay Day, int Period)> cells, string section, string item) =>
        cells.Where(c => c.Item.Section.DisplayName == section && c.Item.Name == item);
}
