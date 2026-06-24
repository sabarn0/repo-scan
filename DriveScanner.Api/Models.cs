using System.Collections.Generic;

namespace DriveScanner.Api.Models
{
    public class SearchRule
    {
        public string Id { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string SearchType { get; set; } = "Regex"; // "Literal", "Wildcard", "Regex"
        public string PatternValue { get; set; } = string.Empty;
        public bool IsSensitive { get; set; } = false;
    }

    public class ScanRequest
    {
        public string TargetPath { get; set; } = string.Empty;
        public List<string> Blacklist { get; set; } = new();
        public List<string> Extensions { get; set; } = new();
        public List<SearchRule> SearchRules { get; set; } = new();
    }

    public class ScanProgressUpdate
    {
        public string CurrentFile { get; set; } = string.Empty;
        public int FilesScanned { get; set; }
        public int TotalFilesFound { get; set; }
        public Dictionary<string, int> CategoryMatchCounts { get; set; } = new();
        public Dictionary<string, int> CategoryUniqueCounts { get; set; } = new();
        public bool IsCompleted { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class ScanMatchEntry
    {
        public string RuleId { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string RawLine { get; set; } = string.Empty;
        public string SanitizedValue { get; set; } = string.Empty;
        public int LineNumber { get; set; }
    }

    public class ScanSummaryRow
    {
        public string CategoryName { get; set; } = string.Empty;
        public int TotalMatches { get; set; }
        public int UniqueMatches { get; set; }
    }

    public class UniqueReportRow
    {
        public string CategoryName { get; set; } = string.Empty;
        public string SanitizedValue { get; set; } = string.Empty;
        public int Count { get; set; }
        public List<string> Files { get; set; } = new();
    }
}
