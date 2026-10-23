using Rdpl.Timetable.Data;

namespace Rdpl.Timetable.Tests.TestSupport;

/// <summary>
/// Copies the client package (data/client) and config into a temporary root and generates the DUMMY assignments
/// workbook there, exactly as the CLI's generate-dummy-assignments command does. Tests never touch the repo folders.
/// </summary>
public sealed class RealDataFixture : IDisposable
{
    public RealDataFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "rdpl-timetable-tests-" + Guid.NewGuid().ToString("N"));
        var source = AppContext.BaseDirectory;
        CopyDirectory(Path.Combine(source, "data", "client"), Path.Combine(Root, "data", "client"));
        CopyDirectory(Path.Combine(source, "config"), Path.Combine(Root, "config"));

        var sections = JsonInputLoader.LoadSections(Path.Combine(Root, "data", "client", "datasets", "sections.json"));
        var classLevel = ClassAssignmentMarkdownParser.Parse(File.ReadAllText(Path.Combine(Root, "data", "client", "TEACHER_CLASS_ASSIGNMENTS.md")));
        WorkbookPath = Path.Combine(Root, "data", "dummy", "TEACHER_CLASS_ALLOCATION.DUMMY.xlsx");
        DummySummary = DummyAssignmentGenerator.Generate(sections, classLevel, WorkbookPath, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "UNASSIGNED-TT" });
        Paths = new DatasetPaths
        {
            AllowedRoot = Root,
            DataRoot = "data/client",
            ConfigRoot = "config",
            AssignmentsWorkbook = "data/dummy/TEACHER_CLASS_ALLOCATION.DUMMY.xlsx",
        };
    }

    public string Root { get; }

    public string WorkbookPath { get; }

    public DummyAssignmentGenerator.Summary DummySummary { get; }

    public DatasetPaths Paths { get; }

    public LoadedDataset Load() => DatasetLoader.Load(Paths);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup of the temp folder.
        }
    }

    private static void CopyDirectory(string from, string to)
    {
        if (!Directory.Exists(from))
        {
            throw new DirectoryNotFoundException($"Test data folder missing: {from}");
        }

        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dir.Replace(from, to, StringComparison.Ordinal));
        }

        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(from, to, StringComparison.Ordinal), overwrite: true);
        }
    }
}
