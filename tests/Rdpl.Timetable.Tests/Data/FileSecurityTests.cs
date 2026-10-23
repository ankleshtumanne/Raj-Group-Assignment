using System.IO.Compression;
using Rdpl.Timetable.Data;

namespace Rdpl.Timetable.Tests.Data;

public sealed class FileSecurityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rdpl-sec-" + Guid.NewGuid().ToString("N"));

    public FileSecurityTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("../outside.json")]
    [InlineData("data/../../outside.json")]
    [InlineData("/etc/passwd")]
    public void Paths_outside_the_allowed_root_are_rejected(string path)
    {
        var ex = Assert.Throws<DataInputException>(() => SafeFileAccess.ResolveInsideRoot(_root, path));
        Assert.Contains("outside the allowed folder", ex.Message);
    }

    [Fact]
    public void Paths_inside_the_root_resolve()
    {
        var resolved = SafeFileAccess.ResolveInsideRoot(_root, "data/client/sections.json");
        Assert.StartsWith(_root, resolved);
    }

    [Fact]
    public void Null_bytes_in_paths_are_rejected()
    {
        Assert.Throws<DataInputException>(() => SafeFileAccess.ResolveInsideRoot(_root, "a\0b.json"));
    }

    [Fact]
    public void Oversized_files_are_refused_before_reading()
    {
        var file = Path.Combine(_root, "big.json");
        File.WriteAllText(file, new string('x', 2048));
        var ex = Assert.Throws<DataInputException>(() => SafeFileAccess.ReadAllText(file, maxBytes: 1024));
        Assert.Contains("limit", ex.Message);
    }

    [Fact]
    public void Symbolic_links_are_not_followed()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Creating symlinks needs elevation on Windows.
        }

        var target = Path.Combine(_root, "real.json");
        File.WriteAllText(target, "{}");
        var link = Path.Combine(_root, "link.json");
        File.CreateSymbolicLink(link, target);
        var ex = Assert.Throws<DataInputException>(() => SafeFileAccess.ReadAllText(link, 1024));
        Assert.Contains("symbolic link", ex.Message);
    }

    [Fact]
    public void Invalid_utf8_is_reported_as_input_error()
    {
        var file = Path.Combine(_root, "bad.md");
        File.WriteAllBytes(file, new byte[] { 0x23, 0x20, 0xC3, 0x28 });
        Assert.Throws<DataInputException>(() => SafeFileAccess.ReadAllText(file, 1024));
    }

    [Fact]
    public void Malformed_json_error_does_not_echo_file_content()
    {
        var file = Path.Combine(_root, "sections.json");
        File.WriteAllText(file, "{ \"sections\": [ SECRET-LOOKING-CONTENT ");
        var ex = Assert.Throws<DataInputException>(() => JsonInputLoader.LoadSections(file));
        Assert.DoesNotContain("SECRET", ex.Message);
        Assert.Contains("not valid JSON", ex.Message);
    }

    [Fact]
    public void Corrupt_xlsx_is_reported_as_input_error()
    {
        var file = Path.Combine(_root, "broken.xlsx");
        File.WriteAllText(file, "this is not a zip file");
        Assert.Throws<DataInputException>(() => XlsxReader.Read(file));
    }

    [Fact]
    public void Xlsx_with_a_dtd_is_rejected()
    {
        // Classic XXE payload in the shared-strings part must never be resolved.
        var file = Path.Combine(_root, "xxe.xlsx");
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            Write(zip, "[Content_Types].xml", "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>");
            Write(zip, "xl/workbook.xml", "<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY e SYSTEM \"file:///etc/passwd\">]><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheets>&e;</sheets></workbook>");
        }

        Assert.Throws<DataInputException>(() => XlsxReader.Read(file));
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(content);
    }
}
