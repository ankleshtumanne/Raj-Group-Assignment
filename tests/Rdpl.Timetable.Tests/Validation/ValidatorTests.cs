using Rdpl.Timetable.Models;
using Rdpl.Timetable.Solver;
using Rdpl.Timetable.Tests.TestSupport;
using Rdpl.Timetable.Validation;

namespace Rdpl.Timetable.Tests.Validation;

/// <summary>
/// The validator must catch violations on its own — it is the evidence that a solver status of
/// OPTIMAL/FEASIBLE is not taken on trust. These tests feed it hand-made (bad) timetables.
/// </summary>
public sealed class ValidatorTests
{
    private static readonly SchedulingProblem Problem = new Synth(Synth.SmallBell(days: 2))
        .Section("Class 11", "Lily").Section("Class 11", "Lavender")
        .Subject("Class 11", "Accounts", 2, 2).Subject("Class 11", "English Language", 2, 2)
        .Subject("Class 11", "English Literature", 1, 1).Subject("Class 11", "Games", 1, 1)
        .Subject("Class 11", "Library", 1, 1).Subject("Class 11", "Economics", 1, 1)
        .Assign("Grade 11 - Lily", "Accounts", "T1").Assign("Grade 11 - Lavender", "Accounts", "T1")
        .Assign("Grade 11 - Lily", "English Language", "T2").Assign("Grade 11 - Lavender", "English Language", "T3")
        .Assign("Grade 11 - Lily", "English Literature", "T2").Assign("Grade 11 - Lavender", "English Literature", "T3")
        .Assign("Grade 11 - Lily", "Games", "T4").Assign("Grade 11 - Lavender", "Games", "T4")
        .Assign("Grade 11 - Lily", "Library", "T5").Assign("Grade 11 - Lavender", "Library", "T5")
        .Assign("Grade 11 - Lily", "Economics", "T6").Assign("Grade 11 - Lavender", "Economics", "T6")
        .Normalize().Problem;

    private static ScheduledCell Cell(string section, string item, SchoolDay day, int period) =>
        new(Problem.Items.Single(i => i.Section.DisplayName == section && i.Name == item).Index, day, period);

    [Fact]
    public void Empty_timetable_fails_H3_and_H8()
    {
        var report = TimetableValidator.Validate(Problem, Array.Empty<ScheduledCell>());
        Assert.False(report.HardConstraintsSatisfied);
        Assert.Contains(report.HardViolations, v => v.RuleId == "H3");
        Assert.Contains(report.HardViolations, v => v.RuleId == "H8");
    }

    [Fact]
    public void Detects_teacher_double_booking()
    {
        var cells = new[]
        {
            Cell("Grade 11 - Lily", "Accounts", SchoolDay.Monday, 1),
            Cell("Grade 11 - Lavender", "Accounts", SchoolDay.Monday, 1),
        };
        var report = TimetableValidator.Validate(Problem, cells);
        Assert.Contains(report.HardViolations, v => v.RuleId == "H1" && v.Message.Contains("T1", StringComparison.Ordinal));
    }

    [Fact]
    public void Detects_two_lessons_in_one_section_slot()
    {
        var cells = new[]
        {
            Cell("Grade 11 - Lily", "Accounts", SchoolDay.Monday, 1),
            Cell("Grade 11 - Lily", "Economics", SchoolDay.Monday, 1),
        };
        var report = TimetableValidator.Validate(Problem, cells);
        Assert.Contains(report.HardViolations, v => v.RuleId == "H3" && v.Message.StartsWith("2 lessons", StringComparison.Ordinal));
    }

    [Fact]
    public void Detects_lessons_outside_teaching_periods()
    {
        var report = TimetableValidator.Validate(Problem, new[] { Cell("Grade 11 - Lily", "Accounts", SchoolDay.Monday, 9) });
        Assert.Contains(report.HardViolations, v => v.RuleId == "H4");
    }

    [Fact]
    public void Detects_games_in_first_pair_and_games_library_same_day()
    {
        var cells = new[]
        {
            Cell("Grade 11 - Lily", "Games", SchoolDay.Monday, 1),
            Cell("Grade 11 - Lily", "Library", SchoolDay.Monday, 4),
        };
        var report = TimetableValidator.Validate(Problem, cells);
        Assert.Contains(report.HardViolations, v => v.RuleId == "G1");
        Assert.Contains(report.HardViolations, v => v.RuleId == "G2");
    }

    [Fact]
    public void Detects_same_first_word_on_same_day_and_lone_block_single()
    {
        var cells = new[]
        {
            Cell("Grade 11 - Lily", "English Language", SchoolDay.Monday, 3),
            Cell("Grade 11 - Lily", "English Literature", SchoolDay.Monday, 4),
        };
        var report = TimetableValidator.Validate(Problem, cells);
        Assert.Contains(report.HardViolations, v => v.RuleId == "L1");
        Assert.Contains(report.HardViolations, v => v.RuleId == "H5/B1");
    }

    [Fact]
    public void Detects_daily_maximum_and_break_crossing()
    {
        // Economics (ppd 1) twice on Monday: H7. Periods 2 and 3 straddle the break after period 2: L2.
        var cells = new[]
        {
            Cell("Grade 11 - Lily", "Economics", SchoolDay.Monday, 2),
            Cell("Grade 11 - Lily", "Economics", SchoolDay.Monday, 3),
        };
        var report = TimetableValidator.Validate(Problem, cells);
        Assert.Contains(report.HardViolations, v => v.RuleId == "H7");
        Assert.Contains(report.HardViolations, v => v.RuleId == "L2");
    }
}
