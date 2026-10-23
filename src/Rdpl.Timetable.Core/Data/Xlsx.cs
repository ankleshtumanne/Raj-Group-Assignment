using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml;

namespace Rdpl.Timetable.Data;

/// <summary>A worksheet as a list of rows; each row maps 0-based column index → cell text.</summary>
public sealed record XlsxSheet(string Name, IReadOnlyList<XlsxRow> Rows);

public sealed record XlsxRow(int RowNumber, IReadOnlyDictionary<int, string> Cells)
{
    public string Get(int column) => Cells.TryGetValue(column, out var v) ? v : string.Empty;

    public bool IsEmpty => Cells.Values.All(string.IsNullOrWhiteSpace);
}

/// <summary>
/// Minimal, dependency-free .xlsx reader (Office Open XML = zip + XML) using only the .NET BCL.
/// Hardened for untrusted files: entry-count/size/ratio limits (zip bombs), DTD processing prohibited
/// and no external resolver (XXE), bounded strings.
/// </summary>
public static class XlsxReader
{
    private const int MaxEntries = 500;
    private const long MaxEntryBytes = 64L * 1024 * 1024;
    private const long MaxTotalBytes = 160L * 1024 * 1024;
    private const int MaxCompressionRatio = 150;
    private const int MaxRowsPerSheet = 200_000;
    private const int MaxColumns = 200;
    private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PkgRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static IReadOnlyList<XlsxSheet> Read(string fullPath)
    {
        using var stream = SafeFileAccess.OpenRead(fullPath, SafeFileAccess.MaxWorkbookBytes);
        return Read(stream, Path.GetFileName(fullPath));
    }

    public static IReadOnlyList<XlsxSheet> Read(Stream stream, string displayName)
    {
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException ex)
        {
            throw new DataInputException($"{displayName} is not a valid .xlsx workbook.", ex);
        }

        using (archive)
        {
            GuardArchive(archive, displayName);
            var sharedStrings = ReadSharedStrings(archive, displayName);
            var sheetTargets = ReadSheetTargets(archive, displayName);
            var sheets = new List<XlsxSheet>();
            foreach (var (name, target) in sheetTargets)
            {
                var entry = archive.GetEntry(target) ?? throw new DataInputException($"{displayName}: sheet '{name}' is missing its XML part.");
                sheets.Add(new XlsxSheet(name, ReadSheet(entry, sharedStrings, displayName)));
            }

            return sheets;
        }
    }

    private static void GuardArchive(ZipArchive archive, string displayName)
    {
        if (archive.Entries.Count > MaxEntries)
        {
            throw new DataInputException($"{displayName}: too many parts in workbook.");
        }

        long total = 0;
        foreach (var e in archive.Entries)
        {
            if (e.FullName.Contains("..", StringComparison.Ordinal) || e.FullName.StartsWith('/'))
            {
                throw new DataInputException($"{displayName}: workbook contains an invalid part path.");
            }

            if (e.Length > MaxEntryBytes)
            {
                throw new DataInputException($"{displayName}: workbook part '{e.Name}' is too large.");
            }

            if (e.CompressedLength > 0 && e.Length / e.CompressedLength > MaxCompressionRatio && e.Length > 1024 * 1024)
            {
                throw new DataInputException($"{displayName}: workbook part '{e.Name}' has a suspicious compression ratio.");
            }

            total += e.Length;
        }

        if (total > MaxTotalBytes)
        {
            throw new DataInputException($"{displayName}: workbook expands to more than {MaxTotalBytes:N0} bytes.");
        }
    }

    private static XmlReader CreateXmlReader(ZipArchiveEntry entry)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            MaxCharactersInDocument = MaxEntryBytes,
            CloseInput = true,
        };

        // Bound the actual decompressed byte count, not just the size the zip header claims.
        return XmlReader.Create(new LengthLimitedStream(entry.Open(), MaxEntryBytes), settings);
    }

    private static List<string> ReadSharedStrings(ZipArchive archive, string displayName)
    {
        var list = new List<string>();
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return list;
        }

        try
        {
            using var reader = CreateXmlReader(entry);
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "si" && reader.NamespaceURI == MainNs)
                {
                    list.Add(ReadTextRuns(reader));
                }
            }
        }
        catch (XmlException ex)
        {
            throw new DataInputException($"{displayName}: shared strings part is malformed.", ex);
        }

        return list;
    }

    /// <summary>Concatenates all &lt;t&gt; descendants of the current element (handles rich-text runs).</summary>
    private static string ReadTextRuns(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            return string.Empty;
        }

        var depth = reader.Depth;
        var sb = new StringBuilder();
        while (reader.Read() && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t" && !reader.IsEmptyElement)
            {
                sb.Append(reader.ReadElementContentAsString());
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                {
                    break;
                }
            }
        }

        return sb.ToString();
    }

    private static List<(string Name, string Target)> ReadSheetTargets(ZipArchive archive, string displayName)
    {
        var rels = new Dictionary<string, string>(StringComparer.Ordinal);
        var relEntry = archive.GetEntry("xl/_rels/workbook.xml.rels") ?? throw new DataInputException($"{displayName}: workbook relationships part missing.");
        var wbEntry = archive.GetEntry("xl/workbook.xml") ?? throw new DataInputException($"{displayName}: workbook part missing.");
        try
        {
            using (var reader = CreateXmlReader(relEntry))
            {
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Relationship" && reader.NamespaceURI == PkgRelNs)
                    {
                        var id = reader.GetAttribute("Id");
                        var target = reader.GetAttribute("Target");
                        if (id is not null && target is not null)
                        {
                            var path = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
                            rels[id] = path.Replace('\\', '/');
                        }
                    }
                }
            }

            var sheets = new List<(string, string)>();
            using (var reader = CreateXmlReader(wbEntry))
            {
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "sheet" && reader.NamespaceURI == MainNs)
                    {
                        var name = reader.GetAttribute("name") ?? string.Empty;
                        var rid = reader.GetAttribute("id", RelNs);
                        if (rid is not null && rels.TryGetValue(rid, out var target))
                        {
                            sheets.Add((name, target));
                        }
                    }
                }
            }

            return sheets;
        }
        catch (XmlException ex)
        {
            throw new DataInputException($"{displayName}: workbook structure is malformed.", ex);
        }
    }

    private static List<XlsxRow> ReadSheet(ZipArchiveEntry entry, IReadOnlyList<string> sharedStrings, string displayName)
    {
        var rows = new List<XlsxRow>();
        try
        {
            using var reader = CreateXmlReader(entry);
            Dictionary<int, string>? cells = null;
            var rowNumber = 0;
            var implicitRow = 0;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "row" && reader.NamespaceURI == MainNs)
                {
                    implicitRow++;
                    rowNumber = int.TryParse(reader.GetAttribute("r"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : implicitRow;
                    cells = new Dictionary<int, string>();
                    if (rows.Count >= MaxRowsPerSheet)
                    {
                        throw new DataInputException($"{displayName}: sheet has more than {MaxRowsPerSheet:N0} rows.");
                    }

                    if (reader.IsEmptyElement)
                    {
                        rows.Add(new XlsxRow(rowNumber, cells));
                        cells = null;
                    }
                }
                else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "row" && cells is not null)
                {
                    rows.Add(new XlsxRow(rowNumber, cells));
                    cells = null;
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "c" && cells is not null)
                {
                    var reference = reader.GetAttribute("r");
                    var type = reader.GetAttribute("t");
                    var column = reference is null ? cells.Count : ColumnIndex(reference);
                    if (column >= MaxColumns)
                    {
                        continue;
                    }

                    cells[column] = ReadCellValue(reader, type, sharedStrings);
                }
            }
        }
        catch (XmlException ex)
        {
            throw new DataInputException($"{displayName}: sheet XML is malformed.", ex);
        }

        return rows;
    }

    private static string ReadCellValue(XmlReader reader, string? type, IReadOnlyList<string> sharedStrings)
    {
        if (reader.IsEmptyElement)
        {
            return string.Empty;
        }

        var depth = reader.Depth;
        string? raw = null;
        string? inline = null;
        while (reader.Read() && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (reader.LocalName == "v")
            {
                raw = reader.ReadElementContentAsString();
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                {
                    break;
                }
            }
            else if (reader.LocalName == "is")
            {
                inline = ReadTextRuns(reader);
            }
        }

        return type switch
        {
            "s" => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx) && idx >= 0 && idx < sharedStrings.Count ? sharedStrings[idx] : string.Empty,
            "inlineStr" => inline ?? string.Empty,
            "b" => raw == "1" ? "TRUE" : "FALSE",
            _ => raw ?? inline ?? string.Empty,
        };
    }

    /// <summary>"C12" → 2 (0-based).</summary>
    public static int ColumnIndex(string cellReference)
    {
        var index = 0;
        var letters = 0;
        foreach (var ch in cellReference)
        {
            if (ch is >= 'A' and <= 'Z')
            {
                index = (index * 26) + (ch - 'A' + 1);
                letters++;
            }
            else if (ch is >= 'a' and <= 'z')
            {
                index = (index * 26) + (ch - 'a' + 1);
                letters++;
            }
            else
            {
                break;
            }

            if (letters > 3)
            {
                return int.MaxValue;
            }
        }

        return index - 1;
    }

    /// <summary>Read-only wrapper that fails once more than <c>limit</c> bytes are read.</summary>
    private sealed class LengthLimitedStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _limit;
        private long _read;

        public LengthLimitedStream(Stream inner, long limit)
        {
            _inner = inner;
            _limit = limit;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => _read; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = _inner.Read(buffer, offset, count);
            _read += n;
            if (_read > _limit)
            {
                throw new DataInputException("Workbook part exceeds the allowed decompressed size.");
            }

            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

/// <summary>Minimal .xlsx writer (inline strings + numbers) used to emit the clearly-labelled DUMMY workbook.</summary>
public static class XlsxWriter
{
    public static void Write(string fullPath, IReadOnlyList<(string Name, IReadOnlyList<IReadOnlyList<object?>> Rows)> sheets)
    {
        ArgumentNullException.ThrowIfNull(sheets);
        if (sheets.Count == 0)
        {
            throw new ArgumentException("At least one sheet is required.", nameof(sheets));
        }

        var directory = Path.GetDirectoryName(fullPath) ?? ".";
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "[Content_Types].xml", ContentTypes(sheets.Count));
            WriteEntry(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            var wb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
            var rels = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            for (var i = 0; i < sheets.Count; i++)
            {
                wb.Append(CultureInfo.InvariantCulture, $"<sheet name=\"{Escape(sheets[i].Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
                rels.Append(CultureInfo.InvariantCulture, $"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>");
                WriteEntry(zip, $"xl/worksheets/sheet{i + 1}.xml", SheetXml(sheets[i].Rows));
            }

            rels.Append(CultureInfo.InvariantCulture, $"<Relationship Id=\"rId{sheets.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
            wb.Append("</sheets></workbook>");
            rels.Append("</Relationships>");
            WriteEntry(zip, "xl/workbook.xml", wb.ToString());
            WriteEntry(zip, "xl/_rels/workbook.xml.rels", rels.ToString());
            WriteEntry(zip, "xl/styles.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills><borders count=\"1\"><border/></borders><cellStyleXfs count=\"1\"><xf/></cellStyleXfs><cellXfs count=\"1\"><xf xfId=\"0\"/></cellXfs></styleSheet>");
        }

        File.Move(temp, fullPath, overwrite: true);
    }

    private static string ContentTypes(int sheetCount)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
        for (var i = 1; i <= sheetCount; i++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        }

        return sb.Append("</Types>").ToString();
    }

    private static string SheetXml(IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        for (var r = 0; r < rows.Count; r++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<row r=\"{r + 1}\">");
            for (var c = 0; c < rows[r].Count; c++)
            {
                var reference = ColumnName(c) + (r + 1).ToString(CultureInfo.InvariantCulture);
                switch (rows[r][c])
                {
                    case null:
                        break;
                    case int or long or double or decimal:
                        sb.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\"><v>{Convert.ToString(rows[r][c], CultureInfo.InvariantCulture)}</v></c>");
                        break;
                    default:
                        sb.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Escape(Convert.ToString(rows[r][c], CultureInfo.InvariantCulture) ?? string.Empty)}</t></is></c>");
                        break;
                }
            }

            sb.Append("</row>");
        }

        return sb.Append("</sheetData></worksheet>").ToString();
    }

    private static string ColumnName(int index)
    {
        var name = string.Empty;
        index++;
        while (index > 0)
        {
            var rem = (index - 1) % 26;
            name = (char)('A' + rem) + name;
            index = (index - 1) / 26;
        }

        return name;
    }

    private static string Escape(string s) => SecurityElement.Escape(s) ?? string.Empty;

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
