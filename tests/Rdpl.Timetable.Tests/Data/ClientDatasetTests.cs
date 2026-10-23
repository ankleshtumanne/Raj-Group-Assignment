using Rdpl.Timetable.Configuration;
using Rdpl.Timetable.Models;
using Rdpl.Timetable.Normalization;
using Rdpl.Timetable.Tests.TestSupport;
using Rdpl.Timetable.Validation;

namespace Rdpl.Timetable.Tests.Data;

/// <summary>Checks the loaders against the REAL client files. Expected numbers were counted by hand from the documents.</summary>
public sealed class ClientDatasetTests : IClassFixture<RealDataFixture>
{
    private readonly RealDataFixture _fixture;

    public ClientDatasetTests(RealDataFixture fixture) => _fixture = fixture;

    [Fact]
    public void Sections_json_has_41_sections_of_which_11_are_pre_primary()
    {
        var data = _fixture.Load();
        Assert.Equal(41, data.Sections.Count);
        Assert.Equal(11, data.Sections.Count(s => s.IsPrePrimary));
        Assert.Equal(30, data.Sections.Count(s => s.ClassNumber is not null));
        Assert.Contains(data.Sections, s => s.Id == "class-10-lily" && s.DisplayName == "Grade 10 - Lily" && s.CurriculumKey == "Class 10");
    }

    [Fact]
    public void Bell_schedule_gives_52_slots_with_the_real_pairs_and_breaks()
    {
        var bell = _fixture.Load().Bell;
        Assert.Equal(52, bell.WeeklyTeachingSlots);
        Assert.Equal(6, bell.Days.Count);

        var monday = bell.GetDay(SchoolDay.Monday)!;
        Assert.Equal(8, monday.Periods.Count);
        Assert.Equal(new[] { 2, 4, 6 }, monday.LastPeriodsBeforeBreak.OrderBy(p => p));
        Assert.Equal(new[] { (1, 2), (3, 4), (5, 6), (7, 8) }, monday.SlotPairs);

        var friday = bell.GetDay(SchoolDay.Friday)!;
        Assert.Equal(10, friday.Periods.Count);
        Assert.Equal(new[] { 2, 4 }, friday.LastPeriodsBeforeBreak.OrderBy(p => p));
        Assert.Equal(5, friday.SlotPairs.Count);
        Assert.Equal(new[] { 1, 2, 3, 4 }, friday.MorningPeriods);
    }

    [Fact]
    public void Curriculum_has_12_classes_each_exactly_52_periods()
    {
        var curricula = _fixture.Load().Curricula;
        Assert.Equal(12, curricula.Count);
        Assert.All(curricula.Values, c =>
        {
            Assert.Equal(52, c.ComputedTotal);
            Assert.Equal(52, c.DeclaredTotalPeriodsPerWeek);
        });

        Assert.Equal(11, curricula["Class 1"].Items.Count);
        var accounts = Assert.Single(curricula["Class 11"].Items, i => i.Name == "Accounts");
        Assert.Equal((2, 11), (accounts.PeriodsPerDay, accounts.PeriodsPerWeek));
        var gamesLibrary = Assert.Single(curricula["Class 7"].Items, i => i.Name == "Games / Library");
        Assert.Equal(CurriculumItemType.ActivityGroup, gamesLibrary.Type);
    }

    [Fact]
    public void Class_level_assignments_parse_roster_placeholders_and_unassigned_staff()
    {
        var classLevel = _fixture.Load().ClassLevel!;
        Assert.Equal(44, classLevel.Roster.Count);
        Assert.Equal(32, classLevel.Assignments.Count(a => a.TeacherCode == "UNASSIGNED-TT"));
        Assert.Equal(12, classLevel.StaffWithoutAssignment.Count);
        Assert.Contains(classLevel.Assignments, a => a.TeacherCode == "RVS115" && a.ClassName == "Class 10" && a.SubjectName == "Mathematics" && a.PeriodsPerWeek == 0);
    }

    [Fact]
    public void Dummy_workbook_is_labelled_and_round_trips_through_the_excel_loader()
    {
        var data = _fixture.Load();
        Assert.True(data.AssignmentsAreDummy);
        Assert.Equal(_fixture.DummySummary.Rows, data.AssignmentRows.Count);
        Assert.All(data.AssignmentRows, r => Assert.True(r.IsDummy));
        Assert.Contains(data.Conflicts, c => c.Type == "DUMMY_DATA_IN_USE");
        Assert.Contains(data.Conflicts, c => c.Type == "MISSING_INPUT_FILE" && c.Source == "Timetable-Engine-Rules-Coverage.md");
    }

    [Fact]
    public void Normalization_scopes_30_sections_and_reports_every_placeholder_and_zero_row()
    {
        var data = _fixture.Load();
        var result = ProblemBuilder.Build(data, new RuleOptions());
        Assert.Equal(30, result.Problem.Sections.Count);
        Assert.Equal(11, result.ExcludedSections.Count);
        Assert.Equal(30 * 52, result.Problem.Items.Sum(i => i.PeriodsPerWeek));

        Assert.Equal(_fixture.DummySummary.PlaceholderRows, result.Stats.PlaceholderRows);
        Assert.Equal(result.Stats.PlaceholderRows, result.Conflicts.Count(c => c.Type == "UNASSIGNED_TEACHER"));
        Assert.Equal(_fixture.DummySummary.ZeroWorkloadRows, result.Stats.ZeroWorkloadRows);
        Assert.Equal(result.Stats.ZeroWorkloadRows, result.Conflicts.Count(c => c.Type == "ZERO_WORKLOAD"));

        // No placeholder teacher ever reaches the model (DATA-1).
        Assert.DoesNotContain(result.Problem.Items, i => i.TeacherCode == "UNASSIGNED-TT");
        Assert.All(result.Problem.Items.Where(i => i.Kind == LessonKind.ReservedUnstaffed), i => Assert.Null(i.TeacherCode));
    }

    [Fact]
    public void Strict_profile_is_proven_infeasible_before_solving_with_specific_reasons()
    {
        var problem = ProblemBuilder.Build(_fixture.Load(), new RuleOptions()).Problem;
        var findings = PreSolveAnalyzer.Analyze(problem).Where(f => f.ProvesInfeasibility).ToList();

        // Counted from CLASS_WISE_SUBJECTS.md: every ppd-2 item with an odd weekly count, per section.
        var oddBlocks = problem.Items.Count(i => i.IsBlock && i.PeriodsPerWeek % 2 == 1);
        Assert.Equal(oddBlocks, findings.Count(f => f.Code == "BLOCK_PARITY"));
        Assert.True(oddBlocks > 0);

        // Class 11/12: English Language 5/wk + English Literature 7/wk need ≥ 3 + 4 = 7 days.
        Assert.Equal(4, findings.Count(f => f.Code == "L1_DAY_CAPACITY"));
        Assert.All(findings.Where(f => f.Code == "L1_DAY_CAPACITY"), f => Assert.Contains("need 7 separate days", f.Message));

        // Class 6: G.K./Moral is the only single-period subject.
        Assert.Equal(2, findings.Count(f => f.Code == "SINGLES_UNPAIRABLE"));
    }

    [Fact]
    public void Pragmatic_profile_has_no_counting_proof_of_infeasibility()
    {
        var problem = ProblemBuilder.Build(_fixture.Load(), RuleOptions.ForProfile("pragmatic")).Problem;
        Assert.DoesNotContain(PreSolveAnalyzer.Analyze(problem), f => f.ProvesInfeasibility);
    }

    [Fact]
    public void Data_validation_mode_never_runs_the_solver()
    {
        var run = TimetableEngine.Run(_fixture.Load(), new RuleOptions(), new SolverOptions(), solve: false);
        Assert.Equal("DATA_VALIDATION_ONLY", run.Report.Outcome);
        Assert.Equal("NOT_RUN", run.Report.Solver.Status);
        Assert.Null(run.Strict);
        Assert.Equal("DUMMY_ASSIGNMENTS", run.Report.DataSource.Kind);
    }
}
