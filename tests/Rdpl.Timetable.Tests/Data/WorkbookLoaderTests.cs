using Rdpl.Timetable.Data;

namespace Rdpl.Timetable.Tests.Data;

public sealed class WorkbookLoaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rdpl-xlsx-" + Guid.NewGuid().ToString("N"));

    public WorkbookLoaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Workbook(string sheetName, params object?[][] rows)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".xlsx");
        XlsxWriter.Write(path, new List<(string, IReadOnlyList<IReadOnlyList<object?>>)> { (sheetName, rows.Select(r => (IReadOnlyList<object?>)r).ToList()) });
        return path;
    }

    [Fact]
    public void Writer_and_reader_round_trip_text_numbers_and_special_characters()
    {
        var path = Workbook("Sheet <1>", new object?[] { "A & B", "x<y>\"z\"", 42, null, "Grade 10 - Lily" });
        var sheet = Assert.Single(XlsxReader.Read(path));
        Assert.Equal("Sheet <1>", sheet.Name);
        var row = Assert.Single(sheet.Rows);
        Assert.Equal("A & B", row.Get(0));
        Assert.Equal("x<y>\"z\"", row.Get(1));
        Assert.Equal("42", row.Get(2));
        Assert.Equal(string.Empty, row.Get(3));
        Assert.Equal("Grade 10 - Lily", row.Get(4));
    }

    [Fact]
    public void Loader_accepts_alternative_header_names()
    {
        // Header spellings differ from the DUMMY workbook — the real workbook may use any of the HeaderAliases.
        var path = Workbook("Section Assignments",
            new object?[] { "Grade Section", "Subject Name", "Employee Code", "Employee Name", "workload_per_week" },
            new object?[] { "Grade 10 - Lily", "Mathematics", "RVS115", "Ashish Thakur", 9 });

        var result = AssignmentWorkbookLoader.Load(path);
        var row = Assert.Single(result.Rows);
        Assert.Equal(("Grade 10 - Lily", "Mathematics", "RVS115", 9), (row.SectionText, row.SubjectName, row.TeacherCode, row.WorkloadPerWeek!.Value));
        Assert.False(result.IsDummy);
    }

    [Fact]
    public void Loader_falls_back_to_a_sheet_with_the_expected_columns_and_says_so()
    {
        var path = Workbook("Allocations",
            new object?[] { "Section", "Subject", "Teacher Code", "Periods / Week" },
            new object?[] { "Grade 9 - Lily", "Physics", "RVS136", 4 });

        var result = AssignmentWorkbookLoader.Load(path);
        Assert.Single(result.Rows);
        Assert.Equal("Allocations", result.SheetUsed);
        Assert.Contains(result.Conflicts, c => c.Type == "SHEET_NAME_MAPPED");
    }

    [Fact]
    public void Invalid_workload_and_empty_rows_are_reported_not_silently_dropped()
    {
        var path = Workbook("Section Assignments",
            new object?[] { "Section", "Subject", "Teacher Code", "Teacher", "Periods / Week" },
            new object?[] { "Grade 9 - Lily", "Physics", "RVS136", "Shashi Shukla", "ten" },
            new object?[] { "Grade 9 - Lily", "", "RVS136", "Shashi Shukla", 4 },
            new object?[] { "Grade 9 - Lily", "Physics", "RVS136", "Shashi Shukla", 4 });

        var result = AssignmentWorkbookLoader.Load(path);
        Assert.Single(result.Rows);
        Assert.Equal(2, result.Conflicts.Count(c => c.Type == "INVALID_ROW"));
    }

    [Fact]
    public void Missing_required_columns_fail_with_a_clear_message()
    {
        var path = Workbook("Section Assignments",
            new object?[] { "Section", "Subject", "Teacher Code" },
            new object?[] { "Grade 9 - Lily", "Physics", "RVS136" });

        var ex = Assert.Throws<DataInputException>(() => AssignmentWorkbookLoader.Load(path));
        Assert.Contains("workload", ex.Message);
    }

    [Fact]
    public void Dummy_marker_is_detected()
    {
        var path = Workbook("Section Assignments",
            new object?[] { "Section", "Subject", "Teacher Code", "Periods / Week", "Data Origin" },
            new object?[] { "Grade 9 - Lily", "Physics", "RVS136", 4, DummyAssignmentGenerator.OriginLabel });

        Assert.True(AssignmentWorkbookLoader.Load(path).IsDummy);
    }
}
