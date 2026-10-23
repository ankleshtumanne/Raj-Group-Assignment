using System.Text.Json;
using Rdpl.Timetable.Tests.TestSupport;

namespace Rdpl.Timetable.Tests.Reporting;

/// <summary>The output must keep the field names of Candidate_Timetable_Assessment.md "Expected Output".</summary>
public sealed class ReportShapeTests
{
    private static JsonElement Generate(bool feasible)
    {
        var synth = new Synth(Synth.SmallBell(days: 2))
            .Section("Class 4", "Lily")
            .Subject("Class 4", "Mathematics", 2, feasible ? 4 : 3).Subject("Class 4", "Science", 2, feasible ? 4 : 5)
            .Assign("Grade 4 - Lily", "Mathematics", "T1").Assign("Grade 4 - Lily", "Science", "T2");
        return JsonDocument.Parse(synth.Run().Report.ToJson()).RootElement.Clone();
    }

    [Fact]
    public void Feasible_report_has_the_expected_top_level_fields()
    {
        var root = Generate(feasible: true);
        Assert.True(root.GetProperty("success").GetBoolean());
        var solver = root.GetProperty("solver");
        Assert.Equal("Google OR-Tools CP-SAT", solver.GetProperty("engine").GetString());
        Assert.Contains(solver.GetProperty("status").GetString(), new[] { "OPTIMAL", "FEASIBLE" });
        Assert.True(solver.GetProperty("wallTimeMs").GetInt64() >= 0);
        Assert.True(solver.GetProperty("timeLimitSeconds").GetInt32() > 0);

        var summary = root.GetProperty("summary");
        foreach (var field in new[] { "sectionsTotal", "sectionsScheduled", "totalSlotsScheduled", "teachersInvolved", "unassignedPlaceholderRows", "zeroWorkloadRows" })
        {
            Assert.True(summary.TryGetProperty(field, out _), $"summary.{field} missing");
        }

        Assert.Equal(JsonValueKind.Array, root.GetProperty("dataConflicts").ValueKind);
        var lesson = root.GetProperty("sectionTimetables").GetProperty("Grade 4 - Lily").GetProperty("MONDAY")[0];
        Assert.True(lesson.TryGetProperty("period", out _));
        Assert.True(lesson.TryGetProperty("subject", out _));
        Assert.True(lesson.TryGetProperty("teacherCode", out _));
        Assert.True(lesson.TryGetProperty("teacherName", out _));

        var teacherLesson = root.GetProperty("teacherTimetables").EnumerateObject().First().Value.EnumerateObject().First().Value[0];
        Assert.True(teacherLesson.TryGetProperty("section", out _));
        Assert.True(root.GetProperty("validation").GetProperty("hardConstraintsSatisfied").GetBoolean());
        Assert.True(root.GetProperty("validation").TryGetProperty("softConstraintPenalty", out _));
        Assert.True(root.GetProperty("validation").TryGetProperty("violatedSoftConstraints", out _));
    }

    [Fact]
    public void Infeasible_report_has_status_and_scheduling_conflicts()
    {
        var root = Generate(feasible: false);
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("INFEASIBLE", root.GetProperty("outcome").GetString());
        var conflicts = root.GetProperty("schedulingConflicts");
        Assert.True(conflicts.GetArrayLength() > 0);
        Assert.False(root.TryGetProperty("sectionTimetables", out _));
    }
}
