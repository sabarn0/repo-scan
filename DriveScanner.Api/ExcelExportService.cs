using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using DriveScanner.Api.Models;

namespace DriveScanner.Api.Services
{
    public class ExcelExportService
    {
        private readonly ScanResultStore _resultStore;

        public ExcelExportService(ScanResultStore resultStore)
        {
            _resultStore = resultStore;
        }

        public byte[] GenerateExcelReport()
        {
            using var workbook = new XLWorkbook();

            // 1. Summary Sheet
            var summarySheet = workbook.Worksheets.Add("Summary");
            summarySheet.Cell("A1").Value = "Category Name";
            summarySheet.Cell("B1").Value = "Total Matches";
            summarySheet.Cell("C1").Value = "Unique Matches";

            // Headers formatting
            var summaryHeaderRange = summarySheet.Range("A1:C1");
            summaryHeaderRange.Style.Font.Bold = true;
            summaryHeaderRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E78");
            summaryHeaderRange.Style.Font.FontColor = XLColor.White;

            var matches = _resultStore.Matches.ToList();
            var categories = _resultStore.ActiveRules
                .Select(r => r.CategoryName)
                .Distinct()
                .ToList();

            // If some categories had no matches but were active, they should still show in summary
            int summaryRowIdx = 2;
            foreach (var cat in categories)
            {
                var catMatches = matches.Where(m => m.CategoryName.Equals(cat, StringComparison.OrdinalIgnoreCase)).ToList();
                int totalMatches = catMatches.Count;
                int uniqueMatches = catMatches.Select(m => m.SanitizedValue).Distinct().Count();

                summarySheet.Cell(summaryRowIdx, 1).Value = cat;
                summarySheet.Cell(summaryRowIdx, 2).Value = totalMatches;
                summarySheet.Cell(summaryRowIdx, 3).Value = uniqueMatches;
                summaryRowIdx++;
            }
            summarySheet.Columns().AdjustToContents();

            // 2. Report Sheet (Aggregated unique data across entire workspace)
            var reportSheet = workbook.Worksheets.Add("Report");
            reportSheet.Cell("A1").Value = "Type";
            reportSheet.Cell("B1").Value = "Sanitized Value";
            reportSheet.Cell("C1").Value = "Count";
            reportSheet.Cell("D1").Value = "Files (Comma-Separated List)";

            var reportHeaderRange = reportSheet.Range("A1:D1");
            reportHeaderRange.Style.Font.Bold = true;
            reportHeaderRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E78");
            reportHeaderRange.Style.Font.FontColor = XLColor.White;

            var aggregatedUnique = matches
                .GroupBy(m => new { m.CategoryName, m.SanitizedValue })
                .Select(g => new UniqueReportRow
                {
                    CategoryName = g.Key.CategoryName,
                    SanitizedValue = g.Key.SanitizedValue,
                    Count = g.Count(),
                    Files = g.Select(m => m.FilePath).Distinct().ToList()
                })
                .OrderBy(r => r.CategoryName)
                .ThenByDescending(r => r.Count)
                .ToList();

            int reportRowIdx = 2;
            foreach (var row in aggregatedUnique)
            {
                reportSheet.Cell(reportRowIdx, 1).Value = row.CategoryName;
                reportSheet.Cell(reportRowIdx, 2).Value = SafeValue(row.SanitizedValue);
                reportSheet.Cell(reportRowIdx, 3).Value = row.Count;
                reportSheet.Cell(reportRowIdx, 4).Value = SafeValue(string.Join(", ", row.Files));
                reportRowIdx++;
            }
            reportSheet.Columns().AdjustToContents();

            // 3. Category Sheets (Dynamically generated tabs for each category with hits)
            foreach (var cat in categories)
            {
                var catMatches = matches.Where(m => m.CategoryName.Equals(cat, StringComparison.OrdinalIgnoreCase)).ToList();
                if (catMatches.Count == 0) continue; // Only create sheet if there are matches

                // Clean name for excel sheet tab limit (max 31 chars, no special chars)
                string safeSheetName = cat;
                char[] invalidChars = new[] { ':', '\\', '/', '?', '*', '[', ']' };
                foreach (char c in invalidChars)
                {
                    safeSheetName = safeSheetName.Replace(c, '_');
                }
                if (safeSheetName.Length > 31)
                {
                    safeSheetName = safeSheetName.Substring(0, 31);
                }

                var catSheet = workbook.Worksheets.Add(safeSheetName);
                catSheet.Cell("A1").Value = "Type";
                catSheet.Cell("B1").Value = "Path";
                catSheet.Cell("C1").Value = "File";
                catSheet.Cell("D1").Value = "Raw Match Line";
                catSheet.Cell("E1").Value = "Sanitized Value";

                var catHeaderRange = catSheet.Range("A1:E1");
                catHeaderRange.Style.Font.Bold = true;
                catHeaderRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E78");
                catHeaderRange.Style.Font.FontColor = XLColor.White;

                int catRowIdx = 2;
                foreach (var entry in catMatches)
                {
                    catSheet.Cell(catRowIdx, 1).Value = entry.CategoryName;
                    catSheet.Cell(catRowIdx, 2).Value = entry.FilePath;
                    catSheet.Cell(catRowIdx, 3).Value = entry.FileName;
                    catSheet.Cell(catRowIdx, 4).Value = SafeValue(entry.RawLine);
                    catSheet.Cell(catRowIdx, 5).Value = SafeValue(entry.SanitizedValue);
                    catRowIdx++;
                }
                catSheet.Columns().AdjustToContents();
            }

            using var memoryStream = new MemoryStream();
            workbook.SaveAs(memoryStream);
            return memoryStream.ToArray();
        }

        private string SafeValue(string? value)
        {
            if (value == null) return string.Empty;
            if (value.Length > 32000)
            {
                return value.Substring(0, 31997) + "...";
            }
            return value;
        }
    }
}
