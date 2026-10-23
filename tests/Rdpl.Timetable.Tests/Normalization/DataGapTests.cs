using Rdpl.Timetable.Configuration;
using Rdpl.Timetable.Models;
using Rdpl.Timetable.Tests.TestSupport;

namespace Rdpl.Timetable.Tests.Normalization;

public sealed class DataGapTests
{
    private const string Section = "Grade 4 - Lily";

    private static Synth Base() => new Synth(Synth.SmallBell(days: 2))
        .Section("Class 4", "Lily")
        .Subject("Class 4", "Mathematics", 2, 4).Subject("Class 4", "English", 2, 2).Subject("Class 4", "Hindi", 2, 2);

    [Fact]
    public void DATA1_unassigned_placeholder_is_reported_and_never_produces_a_lesson()
    {
        var run = Base()
            .Assign(Section, "Mathematics", "T1").Assign(Section, "Hindi", "T2")
            .Assign(Section, "English", "UNASSIGNED-TT", workload: null, teacherName: "Unassigned Teacher")
            .Run();

        var conflict = Assert.Single(run.Report.DataConflicts, c => c.Type == "UNASSIGNED_TEACHER");
        Assert.Equal(Section, conflict.Section);
        Assert.Equal("English", conflict.Subject);
        Assert.Contains("UNASSIGNED-TT", conflict.Message);
        Assert.Contains(run.Report.DataConflicts, c => c.Type == "MISSING_ASSIGNMENT" && c.Subject == "English");

        Assert.True(run.Report.Success, run.Report.Outcome);
        var english = run.Report.SectionTimetables![Section].Values.SelectMany(v => v).Where(l => l.Subject == "English").ToList();
        Assert.Equal(2, english.Count);
        Assert.All(english, l =>
        {
            Assert.Equal("RESERVED_UNSTAFFED", l.Type);
            Assert.Null(l.TeacherCode);
        });
        Assert.Equal(6, run.Report.Summary.TotalSlotsScheduled);
        Assert.Equal(2, run.Report.Summary.ReservedUnstaffedSlots);
        Assert.Equal(1, run.Report.Summary.UnassignedPlaceholderRows);
    }

    [Fact]
    public void DATA1_unstaffed_items_can_be_dropped_instead_of_reserved()
    {
        var run = Base()
            .Assign(Section, "Mathematics", "T1").Assign(Section, "Hindi", "T2")
            .Assign(Section, "English", "UNASSIGNED-TT", workload: null)
            .Run(new RuleOptions { UnstaffedPolicy = UnstaffedPolicy.Exclude });

        Assert.True(run.Report.Success, run.Report.Outcome);
        Assert.Equal(6, run.Report.Summary.TotalCellsFilled);
        Assert.DoesNotContain(run.Report.SectionTimetables![Section].Values.SelectMany(v => v), l => l.Subject == "English");
    }

    [Fact]
    public void H10_zero_workload_rows_are_excluded_and_reported()
    {
        var normalization = Base()
            .Assign(Section, "Mathematics", "T1").Assign(Section, "English", "T3")
            .Assign(Section, "Hindi", "T2", workload: 0)
            .Normalize();

        var zero = Assert.Single(normalization.Conflicts, c => c.Type == "ZERO_WORKLOAD");
        Assert.Equal("T2", zero.TeacherCode);
        var hindi = Assert.Single(normalization.Problem.Items, i => i.Name == "Hindi");
        Assert.Equal(LessonKind.ReservedUnstaffed, hindi.Kind);
        Assert.Null(hindi.TeacherCode);
        Assert.Contains("zero-workload", hindi.UnstaffedReason);
    }

    [Fact]
    public void H10_zero_workload_can_be_treated_as_assigned_in_what_if_mode()
    {
        var normalization = Base()
            .Assign(Section, "Mathematics", "T1").Assign(Section, "English", "T3")
            .Assign(Section, "Hindi", "T2", workload: 0)
            .Normalize(new RuleOptions { ZeroWorkloadPolicy = ZeroWorkloadPolicy.TreatAsAssigned });

        var hindi = Assert.Single(normalization.Problem.Items, i => i.Name == "Hindi");
        Assert.Equal(LessonKind.Subject, hindi.Kind);
        Assert.Equal("T2", hindi.TeacherCode);
        Assert.Contains(normalization.Conflicts, c => c.Type == "ZERO_WORKLOAD" && c.Severity == ConflictSeverity.Info);
    }

    [Fact]
    public void Orphan_rows_not_in_the_curriculum_are_reported()
    {
        var normalization = Base()
            .Assign(Section, "Mathematics", "T1").Assign(Section, "English", "T3").Assign(Section, "Hindi", "T2")
            .Assign(Section, "Dance", "T9")
            .Normalize();

        var orphan = Assert.Single(normalization.Conflicts, c => c.Type == "ORPHAN_ASSIGNMENT");
        Assert.Equal("Dance", orphan.Subject);
        Assert.Equal(1, normalization.Stats.OrphanRows);
    }

    [Fact]
    public void Unknown_section_rows_are_reported()
    {
        var normalization = Base()
            .Assign(Section, "Mathematics", "T1").Assign(Section, "English", "T3").Assign(Section, "Hindi", "T2")
            .Assign("Grade 4 - Rose", "Mathematics", "T1")
            .Normalize();

        Assert.Contains(normalization.Conflicts, c => c.Type == "SECTION_NOT_FOUND" && c.Section == "Grade 4 - Rose");
    }

    [Fact]
    public void P4_pre_primary_sections_are_excluded_and_reported()
    {
        var normalization = Base()
            .PrePrimarySection("Nursery", "Lily")
            .Assign(Section, "Mathematics", "T1").Assign(Section, "English", "T3").Assign(Section, "Hindi", "T2")
            .Normalize();

        Assert.Single(normalization.ExcludedSections);
        Assert.Contains(normalization.Conflicts, c => c.Type == "SECTION_EXCLUDED_PRE_PRIMARY" && c.Section == "Nursery - Lily");
        Assert.DoesNotContain(normalization.Problem.Sections, s => s.IsPrePrimary);
    }

    [Fact]
    public void Curriculum_that_does_not_fill_the_week_excludes_the_section()
    {
        var normalization = new Synth(Synth.SmallBell(days: 2))
            .Section("Class 4", "Lily")
            .Subject("Class 4", "Mathematics", 2, 4)
            .Assign(Section, "Mathematics", "T1")
            .Normalize();

        Assert.Empty(normalization.Problem.Sections);
        Assert.Contains(normalization.Conflicts, c => c.Type == "CURRICULUM_TOTAL_MISMATCH" && c.Severity == ConflictSeverity.Error);
    }

    [Fact]
    public void Two_formal_teachers_for_one_section_subject_are_flagged_as_ambiguous()
    {
        var normalization = Base()
            .Assign(Section, "Mathematics", "T1", workload: 4).Assign(Section, "Mathematics", "T5", workload: 4)
            .Assign(Section, "English", "T3").Assign(Section, "Hindi", "T2")
            .Normalize();

        Assert.Contains(normalization.Conflicts, c => c.Type == "AMBIGUOUS_TEACHER" && c.Subject == "Mathematics");
        Assert.Equal("T1", Assert.Single(normalization.Problem.Items, i => i.Name == "Mathematics").TeacherCode);
    }
}
