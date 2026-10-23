using System.Globalization;
using Rdpl.Timetable.Models;

namespace Rdpl.Timetable.Data;

/// <summary>
/// Reads section-level teaching assignments from TEACHER_CLASS_ALLOCATION.xlsx (sheet "Section Assignments").
/// The real workbook has not been received, so headers are matched flexibly (see <see cref="HeaderAliases"/>);
/// if the real sheet uses different names, add them here — nothing else needs to change.
/// </summary>
public static class AssignmentWorkbookLoader
{
    public const string DefaultSheetName = "Section Assignments";

    /// <summary>Accepted header spellings per logical column (compared after removing non-alphanumerics, case-insensitive).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> HeaderAliases = new Dictionary<string, string[]>
    {
        ["section"] = new[] { "Section", "Section Name", "Grade Section", "Class Section", "Section Display Name", "Section Id" },
        ["class"] = new[] { "Class", "Grade", "Class Name", "Grade Name" },
        ["type"] = new[] { "Type", "Item Type", "Subject Type", "Subject / Activity Type", "Allocation Type" },
        ["subject"] = new[] { "Subject / Activity", "Subject", "Activity", "Subject Name", "Subject/Activity Name" },
        ["teacherCode"] = new[] { "Teacher Code", "Employee Code", "Emp Code", "Code", "Staff Code" },
        ["teacherName"] = new[] { "Teacher", "Teacher Name", "Employee Name", "Name", "Staff Name" },
        ["workload"] = new[] { "Periods / Week", "Periods per Week", "workload_per_week", "Workload Per Week", "Workload", "Periods" },
        ["status"] = new[] { "Status", "Assignment Status" },
        ["origin"] = new[] { "Data Origin", "Origin" },
    };

    public sealed record Result(
        IReadOnlyList<AssignmentRow> Rows,
        IReadOnlyList<DataConflict> Conflicts,
        bool IsDummy,
        string SheetUsed,
        IReadOnlyDictionary<string, string> ColumnMapping);

    public static Result Load(string fullPath, string sheetName = DefaultSheetName)
    {
        var sheets = XlsxReader.Read(fullPath);
        var fileName = Path.GetFileName(fullPath);
        return Parse(sheets, fileName, sheetName);
    }

    public static Result Parse(IReadOnlyList<XlsxSheet> sheets, string fileName, string sheetName = DefaultSheetName)
    {
        var conflicts = new List<DataConflict>();
        var sheet = sheets.FirstOrDefault(s => string.Equals(s.Name.Trim(), sheetName, StringComparison.OrdinalIgnoreCase));
        (int HeaderIndex, Dictionary<string, int> Map)? located = null;
        if (sheet is not null)
        {
            located = LocateHeader(sheet);
        }
        else
        {
            // Fallback: first sheet whose header row has subject + teacher columns.
            foreach (var candidate in sheets)
            {
                located = LocateHeader(candidate);
                if (located is not null)
                {
                    sheet = candidate;
                    conflicts.Add(new DataConflict("SHEET_NAME_MAPPED", ConflictSeverity.Info,
                        $"Sheet '{sheetName}' not found; using '{candidate.Name}' which has the expected columns.", Source: fileName));
                    break;
                }
            }
        }

        if (sheet is null || located is null)
        {
            throw new DataInputException($"{fileName}: no sheet with Section/Subject/Teacher/Workload columns was found (expected sheet '{sheetName}').");
        }

        var (headerIndex, map) = located.Value;
        foreach (var required in new[] { "subject", "teacherCode", "workload" })
        {
            if (!map.ContainsKey(required))
            {
                throw new DataInputException($"{fileName}/{sheet.Name}: required column '{required}' not found in header row.");
            }
        }

        if (!map.ContainsKey("section") && !map.ContainsKey("class"))
        {
            throw new DataInputException($"{fileName}/{sheet.Name}: a Section (or Class) column is required.");
        }

        var rows = new List<AssignmentRow>();
        foreach (var row in sheet.Rows.Skip(headerIndex + 1))
        {
            if (row.IsEmpty)
            {
                continue;
            }

            string Cell(string key) => map.TryGetValue(key, out var col) ? Clean(row.Get(col)) : string.Empty;

            var workloadText = Cell("workload");
            int? workload = null;
            if (workloadText.Length > 0)
            {
                if (double.TryParse(workloadText, NumberStyles.Float, CultureInfo.InvariantCulture, out var w) && w >= 0 && w <= 500 && Math.Abs(w - Math.Round(w)) < 1e-9)
                {
                    workload = (int)Math.Round(w);
                }
                else
                {
                    conflicts.Add(new DataConflict(DataConflictTypes.InvalidRow, ConflictSeverity.Warning,
                        $"Row {row.RowNumber}: workload '{Short(workloadText)}' is not a whole number between 0 and 500; row ignored.", Source: $"{fileName}/{sheet.Name}"));
                    continue;
                }
            }

            var assignment = new AssignmentRow(
                fileName,
                sheet.Name,
                row.RowNumber,
                Cell("section"),
                Cell("class"),
                Cell("type"),
                Cell("subject"),
                Cell("teacherCode"),
                Cell("teacherName"),
                workload,
                Cell("status"),
                Cell("origin"));

            if (assignment.SubjectName.Length == 0 || (assignment.TeacherCode.Length == 0 && assignment.TeacherName.Length == 0))
            {
                conflicts.Add(new DataConflict(DataConflictTypes.InvalidRow, ConflictSeverity.Warning,
                    $"Row {row.RowNumber}: subject or teacher is empty; row ignored.", assignment.SectionText, assignment.SubjectName, Source: $"{fileName}/{sheet.Name}"));
                continue;
            }

            rows.Add(assignment);
        }

        var isDummy = rows.Any(r => r.IsDummy)
            || sheets.Any(s => s.Name.Contains("DUMMY", StringComparison.OrdinalIgnoreCase)
                || s.Rows.Take(5).Any(r => r.Cells.Values.Any(v => v.Contains("DUMMY", StringComparison.Ordinal))));

        var mapping = map.ToDictionary(kv => kv.Key, kv => Clean(sheet.Rows[headerIndex].Get(kv.Value)));
        return new Result(rows, conflicts, isDummy, sheet.Name, mapping);
    }

    private static (int, Dictionary<string, int>)? LocateHeader(XlsxSheet sheet)
    {
        for (var i = 0; i < Math.Min(sheet.Rows.Count, 15); i++)
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (column, text) in sheet.Rows[i].Cells)
            {
                var key = Key(text);
                if (key.Length == 0)
                {
                    continue;
                }

                foreach (var (logical, aliases) in HeaderAliases)
                {
                    if (!map.ContainsKey(logical) && aliases.Any(a => Key(a) == key))
                    {
                        map[logical] = column;
                        break;
                    }
                }
            }

            if (map.ContainsKey("subject") && (map.ContainsKey("teacherCode") || map.ContainsKey("teacherName")))
            {
                return (i, map);
            }
        }

        return null;
    }

    private static string Key(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string Clean(string s)
    {
        var trimmed = s.Trim();
        return trimmed.Length > 200 ? trimmed[..200] : trimmed;
    }

    private static string Short(string s) => s.Length <= 20 ? s : s[..20] + "…";
}
