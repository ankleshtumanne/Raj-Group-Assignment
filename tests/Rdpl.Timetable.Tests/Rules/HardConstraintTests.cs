using Rdpl.Timetable.Configuration;
using Rdpl.Timetable.Constraints;
using Rdpl.Timetable.Models;
using Rdpl.Timetable.Solver;
using Rdpl.Timetable.Tests.TestSupport;

namespace Rdpl.Timetable.Tests.Rules;

/// <summary>
/// One synthetic scenario per Tier-1 / Tier-2 rule. Each scenario is tight enough that a model WITHOUT the rule
/// would very likely violate it, and every solution is also re-checked by the independent validator.
/// </summary>
public sealed class HardConstraintTests
{
    private const string S1 = "Grade 5 - Lily";
    private const string S2 = "Grade 5 - Lavender";

    [Fact]
    public void H1_shared_teacher_is_never_double_booked_across_sections()
    {
        // 8 slots per section; T1 teaches Mathematics (4) in both sections = 8 = every slot of the week.
        var run = new Synth(Synth.SmallBell())
            .Section("Class 5", "Lily").Section("Class 5", "Lavender")
            .Subject("Class 5", "Mathematics", 2, 4).Subject("Class 5", "English", 2, 4)
            .Assign(S1, "Mathematics", "T1").Assign(S2, "Mathematics", "T1")
            .Assign(S1, "English", "T2").Assign(S2, "English", "T2")
            .Run();

        Assert.True(run.Report.Success, run.Report.Outcome);
        var booked = run.Cells().Where(c => c.Item.BooksTeacher).GroupBy(c => (c.Item.TeacherCode, c.Day, c.Period));
        Assert.All(booked, g => Assert.Single(g));
        Assert.True(run.Report.Validation!.HardConstraintsSatisfied);
    }

    [Fact]
    public void H1_teacher_overload_is_reported_as_infeasible_with_an_explanation()
    {
        var synth = new Synth(Synth.SmallBell())
            .Section("Class 5", "Lily").Section("Class 5", "Lavender")
            .Subject("Class 5", "Mathematics", 2, 4).Subject("Class 5", "English", 2, 4)
            .Assign(S1, "Mathematics", "T1").Assign(S2, "Mathematics", "T1")
            .Assign(S1, "English", "T1").Assign(S2, "English", "T3");

        var run = synth.Run();
        Assert.False(run.Report.Success);
        Assert.Equal("INFEASIBLE", run.Report.Outcome);
        var overload = Assert.Single(run.Report.SchedulingConflicts, c => c.Code == "TEACHER_OVERLOAD");
        Assert.True(overload.ProvesInfeasibility);
        Assert.Contains("12 periods required", overload.Message);
        Assert.Contains("max capacity is 8", overload.Message);

        // CP-SAT on its own (without the pre-solve analyser) must agree.
        var direct = TimetableSolver.Solve(synth.Normalize().Problem, Synth.Fast() with { WarmStart = false }, Relaxation.None);
        Assert.Equal("INFEASIBLE", direct.Status);
    }

    [Fact]
    public void H3_H8_every_slot_filled_once_and_weekly_totals_exact()
    {
        var run = new Synth(Synth.SmallBell(days: 3))
            .Section("Class 4", "Lily")
            .Subject("Class 4", "Mathematics", 2, 4).Subject("Class 4", "Science", 2, 4).Subject("Class 4", "Hindi", 2, 4)
            .Assign("Grade 4 - Lily", "Mathematics", "T1").Assign("Grade 4 - Lily", "Science", "T2").Assign("Grade 4 - Lily", "Hindi", "T3")
            .Run();

        var cells = run.Cells();
        Assert.Equal(12, cells.Count);
        Assert.All(cells.GroupBy(c => (c.Day, c.Period)), g => Assert.Single(g));
        Assert.All(cells.GroupBy(c => c.Item.Name), g => Assert.Equal(4, g.Count()));
    }

    [Fact]
    public void H7_daily_maximum_is_respected()
    {
        // ppd 1 subjects only: each may appear at most once per day.
        var run = new Synth(Synth.SmallBell(days: 2))
            .Section("Class 4", "Lily")
            .Subject("Class 4", "Communication", 1, 2).Subject("Class 4", "G.K./Moral", 1, 2)
            .Subject("Class 4", "Computer", 1, 2).Subject("Class 4", "Drawing", 1, 2)
            .Assign("Grade 4 - Lily", "Communication", "T1").Assign("Grade 4 - Lily", "G.K./Moral", "T2")
            .Assign("Grade 4 - Lily", "Computer", "T3").Assign("Grade 4 - Lily", "Drawing", "T4")
            .Run(new RuleOptions { EnableSoftObjective = false });

        Assert.True(run.Report.Success, run.Report.Outcome);
        Assert.All(run.Cells().GroupBy(c => (c.Item.Name, c.Day)), g => Assert.Single(g));
    }

    [Fact]
    public void H5_B1_block_subjects_occupy_full_consecutive_pairs()
    {
        var run = new Synth(Synth.SmallBell(days: 3))
            .Section("Class 4", "Lily")
            .Subject("Class 4", "Mathematics", 2, 6).Subject("Class 4", "Science", 2, 4)
            .Subject("Class 4", "Communication", 1, 1).Subject("Class 4", "G.K./Moral", 1, 1)
            .Assign("Grade 4 - Lily", "Mathematics", "T1").Assign("Grade 4 - Lily", "Science", "T2")
            .Assign("Grade 4 - Lily", "Communication", "T3").Assign("Grade 4 - Lily", "G.K./Moral", "T4")
            .Run();

        Assert.True(run.Report.Success, run.Report.Outcome);
        foreach (var group in run.Cells().Where(c => c.Item.IsBlock).GroupBy(c => (c.Item.Name, c.Day)))
        {
            var periods = group.Select(c => c.Period).OrderBy(p => p).ToList();
            Assert.Equal(2, periods.Count);
            Assert.True(periods.SequenceEqual(new[] { 1, 2 }) || periods.SequenceEqual(new[] { 3, 4 }), $"{group.Key}: {string.Join(",", periods)} is not a slot pair.");
        }
    }

    [Fact]
    public void H5_odd_block_count_is_proven_infeasible_in_strict_and_minimised_in_pragmatic()
    {
        Synth Build() => new Synth(Synth.SmallBell(days: 2))
            .Section("Class 3", "Lily")
            .Subject("Class 3", "Mathematics", 2, 3).Subject("Class 3", "Hindi", 2, 4).Subject("Class 3", "G.K./Moral", 1, 1)
            .Assign("Grade 3 - Lily", "Mathematics", "T1").Assign("Grade 3 - Lily", "Hindi", "T2").Assign("Grade 3 - Lily", "G.K./Moral", "T3");

        var strict = Build().Run();
        Assert.Equal("INFEASIBLE", strict.Report.Outcome);
        Assert.Contains(strict.Report.SchedulingConflicts, c => c.Code == "BLOCK_PARITY" && c.ProvesInfeasibility);

        var pragmatic = Build().Run(RuleOptions.ForProfile("pragmatic"));
        Assert.True(pragmatic.Report.Success, pragmatic.Report.Outcome);
        var single = Assert.Single(pragmatic.Report.Validation!.RelaxedRuleViolations);
        Assert.Equal("H5/B1", single.RuleId);
        Assert.Contains("1 lone single", single.Message);
        Assert.Equal("OPTIMAL", pragmatic.Report.Solver.Status);
    }

    [Fact]
    public void G1_games_and_library_never_in_first_pair()
    {
        var run = new Synth(Synth.SmallBell(days: 3))
            .Section("Class 7", "Lily")
            .Activity("Class 7", "Games / Library", 2, 4).Subject("Class 7", "Mathematics", 2, 6).Subject("Class 7", "Science", 2, 2)
            .Assign("Grade 7 - Lily", "Mathematics", "T1").Assign("Grade 7 - Lily", "Science", "T2")
            .Run();

        Assert.True(run.Report.Success, run.Report.Outcome);
        var games = run.Cells().Of("Grade 7 - Lily", "Games / Library").ToList();
        Assert.Equal(4, games.Count);
        Assert.All(games, c => Assert.True(c.Period > 2, $"Games / Library in period {c.Period}"));
    }

    [Fact]
    public void G2_class_11_games_and_library_not_on_the_same_day()
    {
        var run = new Synth(Synth.SmallBell(days: 2))
            .Section("Class 11", "Lily")
            .Subject("Class 11", "Games", 2, 2).Subject("Class 11", "Library", 2, 2).Subject("Class 11", "Accounts", 2, 4)
            .Assign("Grade 11 - Lily", "Games", "T1").Assign("Grade 11 - Lily", "Library", "T2").Assign("Grade 11 - Lily", "Accounts", "T3")
            .Run();

        Assert.True(run.Report.Success, run.Report.Outcome);
        var cells = run.Cells();
        var gamesDays = cells.Of("Grade 11 - Lily", "Games").Select(c => c.Day).ToHashSet();
        var libraryDays = cells.Of("Grade 11 - Lily", "Library").Select(c => c.Day).ToHashSet();
        Assert.Empty(gamesDays.Intersect(libraryDays));
    }

    [Fact]
    public void L1_same_first_word_subjects_never_share_a_day()
    {
        var run = new Synth(Synth.SmallBell(days: 2))
            .Section("Class 9", "Lily")
            .Subject("Class 9", "English Language", 2, 2).Subject("Class 9", "English Literature", 2, 2).Subject("Class 9", "Mathematics", 2, 4)
            .Assign("Grade 9 - Lily", "English Language", "T1").Assign("Grade 9 - Lily", "English Literature", "T1").Assign("Grade 9 - Lily", "Mathematics", "T2")
            .Run();

        Assert.True(run.Report.Success, run.Report.Outcome);
        var cells = run.Cells();
        var language = cells.Of("Grade 9 - Lily", "English Language").Select(c => c.Day).ToHashSet();
        var literature = cells.Of("Grade 9 - Lily", "English Literature").Select(c => c.Day).ToHashSet();
        Assert.Empty(language.Intersect(literature));
    }

    [Fact]
    public void L1_day_capacity_shortfall_is_explained()
    {
        var run = new Synth(Synth.SmallBell(days: 2))
            .Section("Class 9", "Lily")
            .Subject("Class 9", "English Language", 2, 4).Subject("Class 9", "English Literature", 2, 2).Subject("Class 9", "Mathematics", 2, 2)
            .Assign("Grade 9 - Lily", "English Language", "T1").Assign("Grade 9 - Lily", "English Literature", "T1").Assign("Grade 9 - Lily", "Mathematics", "T2")
            .Run();

        Assert.Equal("INFEASIBLE", run.Report.Outcome);
        var finding = Assert.Single(run.Report.SchedulingConflicts, c => c.Code == "L1_DAY_CAPACITY");
        Assert.Contains("need 3 separate days, but only 2 days exist", finding.Message);
    }

    [Fact]
    public void L1_is_enforced_by_the_cp_sat_model_itself()
    {
        // Without L1 this is feasible (Mon: EngLang + EngLit, Tue: EngLang + Maths). With L1 it is not.
        var problem = new Synth(Synth.SmallBell(days: 2))
            .Section("Class 9", "Lily")
            .Subject("Class 9", "English Language", 2, 4).Subject("Class 9", "English Literature", 2, 2).Subject("Class 9", "Mathematics", 2, 2)
            .Assign("Grade 9 - Lily", "English Language", "T1").Assign("Grade 9 - Lily", "English Literature", "T1").Assign("Grade 9 - Lily", "Mathematics", "T2")
            .Normalize().Problem;

        Assert.Equal("INFEASIBLE", TimetableSolver.Solve(problem, Synth.Fast() with { WarmStart = false }, Relaxation.None).Status);
        var relaxed = TimetableSolver.Solve(problem, Synth.Fast() with { WarmStart = false }, Relaxation.None with { SameFirstWordDay = true });
        Assert.True(relaxed.HasSolution);
    }

    [Fact]
    public void G2_is_enforced_by_the_cp_sat_model_itself()
    {
        // Games and Library are the only single-period items, so they must share a slot pair (same day) unless G2 holds.
        // No counting check catches this; CP-SAT must prove it.
        var run = new Synth(Synth.SmallBell(days: 2))
            .Section("Class 12", "Lily")
            .Subject("Class 12", "Games", 1, 1).Subject("Class 12", "Library", 1, 1)
            .Subject("Class 12", "Accounts", 2, 4).Subject("Class 12", "Economics", 2, 2)
            .Assign("Grade 12 - Lily", "Games", "T1").Assign("Grade 12 - Lily", "Library", "T2")
            .Assign("Grade 12 - Lily", "Accounts", "T3").Assign("Grade 12 - Lily", "Economics", "T4")
            .Run();

        Assert.Equal("INFEASIBLE", run.Report.Outcome);
        Assert.DoesNotContain(run.Report.SchedulingConflicts, c => c.ProvesInfeasibility && c.Code != "SOLVER_INFEASIBLE");
        Assert.Contains(run.Report.SchedulingConflicts, c => c.Code == "SOLVER_INFEASIBLE");
        Assert.Equal("INFEASIBLE", run.Report.Solver.Status);
    }

    [Fact]
    public void G1_first_pair_is_unusable_for_games_even_when_that_makes_the_week_impossible()
    {
        // Science's teacher is unavailable in periods 1–2, so only Games / Library could fill them — G1 forbids it.
        var run = new Synth(Synth.SmallBell(days: 2))
            .Section("Class 7", "Lily")
            .Activity("Class 7", "Games / Library", 2, 4).Subject("Class 7", "Science", 2, 4)
            .Assign("Grade 7 - Lily", "Science", "T1")
            .Unavailable("T1", (SchoolDay.Monday, 1), (SchoolDay.Monday, 2), (SchoolDay.Tuesday, 1), (SchoolDay.Tuesday, 2))
            .Run();

        Assert.Equal("INFEASIBLE", run.Report.Outcome);
        Assert.Contains(run.Report.SchedulingConflicts, c => c.Code == "SOLVER_INFEASIBLE");
    }

    [Fact]
    public void H2_teacher_off_day_is_respected()
    {
        var run = new Synth(Synth.SmallBell(days: 3))
            .Section("Class 4", "Lily")
            .Subject("Class 4", "Mathematics", 2, 4).Subject("Class 4", "Science", 2, 4).Subject("Class 4", "Hindi", 2, 4)
            .Assign("Grade 4 - Lily", "Mathematics", "T1").Assign("Grade 4 - Lily", "Science", "T2").Assign("Grade 4 - Lily", "Hindi", "T3")
            .OffDay("T1", SchoolDay.Monday)
            .Run();

        Assert.True(run.Report.Success, run.Report.Outcome);
        Assert.DoesNotContain(run.Cells().Of("Grade 4 - Lily", "Mathematics"), c => c.Day == SchoolDay.Monday);
    }

    [Fact]
    public void H6_day_restriction_is_respected()
    {
        var run = new Synth(Synth.SmallBell(days: 3))
            .Section("Class 4", "Lily")
            .Subject("Class 4", "Mathematics", 2, 4).Subject("Class 4", "Science", 2, 4).Subject("Class 4", "Hindi", 2, 4)
            .Assign("Grade 4 - Lily", "Mathematics", "T1").Assign("Grade 4 - Lily", "Science", "T2").Assign("Grade 4 - Lily", "Hindi", "T3")
            .OnlyOn("Class 4", "Science", SchoolDay.Tuesday, SchoolDay.Wednesday)
            .Run();

        Assert.True(run.Report.Success, run.Report.Outcome);
        Assert.All(run.Cells().Of("Grade 4 - Lily", "Science"), c => Assert.NotEqual(SchoolDay.Monday, c.Day));
    }

    [Fact]
    public void H9_activity_fixed_slots_are_respected()
    {
        var run = new Synth(Synth.SmallBell(days: 2))
            .Section("Class 1", "Lily")
            .Activity("Class 1", "Happy Feet", 2, 2).Subject("Class 1", "Mathematics", 2, 4).Subject("Class 1", "Hindi", 2, 2)
            .Assign("Grade 1 - Lily", "Mathematics", "T1").Assign("Grade 1 - Lily", "Hindi", "T2")
            .Fixed("Class 1", "Happy Feet", (SchoolDay.Tuesday, 3), (SchoolDay.Tuesday, 4))
            .Run();

        Assert.True(run.Report.Success, run.Report.Outcome);
        var cells = run.Cells().Of("Grade 1 - Lily", "Happy Feet").Select(c => (c.Day, c.Period)).OrderBy(c => c.Period).ToList();
        Assert.Equal(new[] { (SchoolDay.Tuesday, 3), (SchoolDay.Tuesday, 4) }, cells);
    }

    [Fact]
    public void T6_activity_instructors_are_not_teacher_booked()
    {
        // The same instructor runs Happy Feet for two sections in the SAME fixed slot — allowed (whole-school activity).
        var run = new Synth(Synth.SmallBell(days: 2))
            .Section("Class 1", "Lily").Section("Class 1", "Lavender")
            .Activity("Class 1", "Happy Feet", 2, 2).Subject("Class 1", "Mathematics", 2, 4).Subject("Class 1", "Hindi", 2, 2)
            .Assign("Grade 1 - Lily", "Happy Feet", "K1").Assign("Grade 1 - Lavender", "Happy Feet", "K1")
            .Assign("Grade 1 - Lily", "Mathematics", "T1").Assign("Grade 1 - Lavender", "Mathematics", "T2")
            .Assign("Grade 1 - Lily", "Hindi", "T3").Assign("Grade 1 - Lavender", "Hindi", "T4")
            .Fixed("Class 1", "Happy Feet", (SchoolDay.Monday, 3), (SchoolDay.Monday, 4))
            .Run();

        Assert.True(run.Report.Success, run.Report.Outcome);
        var activities = run.Cells().Where(c => c.Item.Name == "Happy Feet").ToList();
        Assert.Equal(4, activities.Count);
        Assert.All(activities, c => Assert.Contains(c.Item.Instructors, i => i.Code == "K1"));
        Assert.All(activities, c => Assert.False(c.Item.BooksTeacher));
    }

    [Fact]
    public void T2_scheduled_lessons_carry_only_the_formally_assigned_teacher()
    {
        var run = new Synth(Synth.SmallBell(days: 2))
            .Section("Class 4", "Lily")
            .Subject("Class 4", "Mathematics", 2, 4).Subject("Class 4", "Science", 2, 4)
            .Assign("Grade 4 - Lily", "Mathematics", "T1").Assign("Grade 4 - Lily", "Science", "T2")
            .Run();

        var lessons = run.Report.SectionTimetables!["Grade 4 - Lily"].Values.SelectMany(v => v).ToList();
        Assert.All(lessons.Where(l => l.Subject == "Mathematics"), l => Assert.Equal("T1", l.TeacherCode));
        Assert.All(lessons.Where(l => l.Subject == "Science"), l => Assert.Equal("T2", l.TeacherCode));
    }

    [Fact]
    public void PR_MATH_soft_objective_moves_mathematics_to_the_morning_when_possible()
    {
        var run = new Synth(Synth.SmallBell(days: 2))
            .Section("Class 4", "Lily")
            .Subject("Class 4", "Mathematics", 2, 4).Subject("Class 4", "Science", 2, 4)
            .Assign("Grade 4 - Lily", "Mathematics", "T1").Assign("Grade 4 - Lily", "Science", "T2")
            .Run();

        Assert.Equal("OPTIMAL", run.Report.Solver.Status);
        Assert.Equal(0, run.Report.Validation!.SoftConstraintPenalty);
        Assert.All(run.Cells().Of("Grade 4 - Lily", "Mathematics"), c => Assert.InRange(c.Period, 1, 2));
        Assert.True(run.Report.Validation.ObjectiveMatchesIndependentCheck);
    }
}
