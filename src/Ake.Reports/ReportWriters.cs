using System.Globalization;
using System.Text;
using Ake.Core;
using ClosedXML.Excel;

namespace Ake.Reports;

/// <summary>
/// CSV makine biçimidir: ayraç virgül (değiştirilebilir), ondalık ayraç nokta, satır sonu LF.
/// İnsan için XLSX kullanılır.
/// </summary>
public static class CsvReport
{
    public static string Write(ReportTable table, char separator = ',')
    {
        ArgumentNullException.ThrowIfNull(table);

        var builder = new StringBuilder();
        AppendRow(builder, table.Columns.Select(column => (object?)column.Header), separator);
        foreach (var row in table.Rows)
        {
            AppendRow(builder, row, separator);
        }

        return builder.ToString();
    }

    /// <summary>UTF-8, BOM'lu yazar; Excel Türkçe karakterleri BOM olmadan bozar.</summary>
    public static Result Save(ReportTable table, string path, char separator = ',') =>
        ReportFiles.Guard(path, () => File.WriteAllText(path, Write(table, separator), new UTF8Encoding(true)));

    private static void AppendRow(StringBuilder builder, IEnumerable<object?> cells, char separator)
    {
        var first = true;
        foreach (var cell in cells)
        {
            if (!first)
            {
                builder.Append(separator);
            }

            first = false;
            builder.Append(Escape(Text(cell), separator));
        }

        builder.Append('\n');
    }

    private static string Text(object? cell) => cell switch
    {
        null => string.Empty,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => cell.ToString() ?? string.Empty,
    };

    private static string Escape(string text, char separator) =>
        text.Contains(separator) || text.Contains('"') || text.Contains('\n') || text.Contains('\r')
            ? $"\"{text.Replace("\"", "\"\"")}\""
            : text;
}

/// <summary>XLSX çıktısı: rapor başına bir sayfa, kalın ve dondurulmuş başlık satırı, filtre.</summary>
public static class XlsxReport
{
    public static Result Save(IReadOnlyList<ReportTable> tables, string path)
    {
        ArgumentNullException.ThrowIfNull(tables);

        return ReportFiles.Guard(path, () =>
        {
            using var workbook = new XLWorkbook();
            foreach (var table in tables)
            {
                AddSheet(workbook, table);
            }

            workbook.SaveAs(path);
        });
    }

    private static void AddSheet(XLWorkbook workbook, ReportTable table)
    {
        var sheet = workbook.Worksheets.Add(table.Name);

        for (var column = 0; column < table.Columns.Count; column++)
        {
            sheet.Cell(1, column + 1).Value = table.Columns[column].Header;
        }

        for (var row = 0; row < table.Rows.Count; row++)
        {
            for (var column = 0; column < table.Columns.Count; column++)
            {
                var cell = sheet.Cell(row + 2, column + 1);
                switch (table.Rows[row][column])
                {
                    case null:
                        break;
                    case int number:
                        cell.Value = number;
                        break;
                    case decimal number:
                        cell.Value = number;
                        break;
                    case var other:
                        // Metin olarak yaz: "1.2" gibi klemens adları sayıya ya da tarihe çevrilmesin.
                        cell.SetValue(other.ToString());
                        break;
                }
            }
        }

        var header = sheet.Range(1, 1, 1, table.Columns.Count);
        header.Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        if (table.Rows.Count > 0)
        {
            sheet.Range(1, 1, table.Rows.Count + 1, table.Columns.Count).SetAutoFilter();
        }

        // Sütun genişliği içerikten tahmin edilir. AdjustToContents yazı tipi ölçümü ister ve
        // yazı tipi kurulu olmayan makinede (CI, konteyner) farklı sonuç verir.
        for (var column = 0; column < table.Columns.Count; column++)
        {
            var longest = table.Rows
                .Select(row => row[column]?.ToString()?.Length ?? 0)
                .Append(table.Columns[column].Header.Length)
                .Max();
            sheet.Column(column + 1).Width = Math.Clamp(longest + 2, 8, 60);
        }
    }
}

internal static class ReportFiles
{
    public static Result Guard(string path, Action write)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            write();
            return Result.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure(new Error("rapor.dosya_yazilamadi", $"'{path}' yazılamadı: {ex.Message}"));
        }
    }
}
