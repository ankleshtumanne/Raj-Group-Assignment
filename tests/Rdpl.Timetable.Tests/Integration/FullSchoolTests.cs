using System.Text.Json;
using Rdpl.Timetable.Configuration;
using Rdpl.Timetable.Tests.TestSupport;

namespace Rdpl.Timetable.Tests.Integration;

/// <summary>
/// End-to-end on the real client files + DUMMY assignments (the real xlsx has not been supplied).
/// The full-school solve takes a couple of minutes; it is the evidence behind the README's results table.
/// </summary>
public sealed class FullSchoolTests : IClassFixture<RealDataFixture>
{
    private readonly RealDataFixture _fixture;

    public FullSchoolTests(RealDataFixture fixture) => _fixture = fixture;

    [Fact]
    public void Pragmatic_full_school_solve_is_complete_and_passes_independent_validation()
    {
        var limit = int.TryParse(Environment.GetEnvironmentVariable("TIMETABLE_TEST_TIME_LIMIT"), out var t) ? t : 150;
        var run = TimetableEngine.Run(_fixture.Load(), RuleOptions.ForProfile("pragmatic"), Synth.Fast(limit), solve: true);
        var report = run.Report;

        Assert.True(report.Success, $"{report.Outcome}: {report.Solver.Status}");
        Assert.Equal("TIMETABLE_GENERATED", report.Outcome);
        Assert.Contains(report.Solver.Status, new[] { "OPTIMAL", "FEASIBLE" });
        Assert.Equal(30, report.Summary.SectionsScheduled);
        Assert.Equal(30 * 52, report.Summary.TotalCellsFilled);
        Assert.True(report.Validation!.HardConstraintsSatisfied);
        Assert.Equal(0, report.Validation.HardViolationCount);

        // Second, report-level check straight from the output JSON (independent of solver variables AND of the validator).
        using var json = JsonDocument.Parse(report.ToJson());
        var root = json.RootElement;
        var sections = root.GetProperty("sectionTimetables");
        Assert.Equal(30, sections.EnumerateObject().Count());
        foreach (var section in sections.EnumerateObject())
        {
            var mondayToThursday = new[] { "MONDAY", "TUESDAY", "WEDNESDAY", "THURSDAY" }.Sum(d => section.Value.GetProperty(d).GetArrayLength());
            var fridaySaturday = new[] { "FRIDAY", "SATURDAY" }.Sum(d => section.Value.GetProperty(d).GetArrayLength());
            Assert.Equal(32, mondayToThursday);
            Assert.Equal(20, fridaySaturday);
            foreach (var day in section.Value.EnumerateObject())
            {
                foreach (var lesson in day.Value.EnumerateArray())
                {
                    var subject = lesson.GetProperty("subject").GetString()!;
                    var period = lesson.GetProperty("period").GetInt32();
                    if (subject.Contains("Games", StringComparison.Ordinal) || subject.Contains("Library", StringComparison.Ordinal))
                    {
                        Assert.True(period > 2, $"G1 broken in {section.Name} {day.Name} P{period}");
                    }

                    if (lesson.TryGetProperty("teacherCode", out var code))
                    {
                        Assert.NotEqual("UNASSIGNED-TT", code.GetString());
                    }
                }
            }
        }

        var seen = new HashSet<(string, string, int)>();
        foreach (var teacher in root.GetProperty("teacherTimetables").EnumerateObject())
        {
            foreach (var day in teacher.Value.EnumerateObject())
            {
                foreach (var lesson in day.Value.EnumerateArray())
                {
                    Assert.True(seen.Add((teacher.Name, day.Name, lesson.GetProperty("period").GetInt32())), $"H1 broken: {teacher.Name} {day.Name}");
                }
            }
        }
    }

    [Fact]
    public void Strict_full_school_run_reports_infeasible_with_explanations_and_a_diagnostic()
    {
        var run = TimetableEngine.Run(_fixture.Load(), new RuleOptions(), Synth.Fast(20), solve: true);
        var report = run.Report;

        Assert.False(report.Success);
        Assert.Equal("INFEASIBLE", report.Outcome);
        Assert.Null(report.SectionTimetables);
        Assert.Contains(report.SchedulingConflicts, c => c.Code == "BLOCK_PARITY" && c.ProvesInfeasibility);
        Assert.Contains(report.SchedulingConflicts, c => c.Code == "L1_DAY_CAPACITY" && c.Sections!.Contains("Grade 12 - Lily"));
        Assert.NotNull(report.Diagnostic);
        Assert.Contains("NOT A VALID TIMETABLE", report.Diagnostic!.Purpose);
    }
}
