using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DriveScanner.Api.Models;

namespace DriveScanner.Api.Services
{
    public class EvaluatorService
    {
        private readonly ConcurrentDictionary<string, Regex> _regexCache = new();

        public List<ScanMatchEntry> EvaluateLine(
            string line,
            int lineNumber,
            string filePath,
            string fileName,
            List<SearchRule> rules)
        {
            var matches = new List<ScanMatchEntry>();

            foreach (var rule in rules)
            {
                bool isMatch = false;
                string matchedText = string.Empty;

                try
                {
                    if (rule.SearchType == "Regex")
                    {
                        var regex = GetOrCreateRegex(rule.Id, rule.PatternValue);
                        var match = regex.Match(line);
                        if (match.Success)
                        {
                            isMatch = true;
                            matchedText = match.Value;
                        }
                    }
                    else if (rule.SearchType == "Literal")
                    {
                        int index = line.IndexOf(rule.PatternValue, StringComparison.OrdinalIgnoreCase);
                        if (index >= 0)
                        {
                            isMatch = true;
                            matchedText = line.Substring(index, rule.PatternValue.Length);
                        }
                    }
                    else if (rule.SearchType == "Wildcard")
                    {
                        string pattern = "^" + Regex.Escape(rule.PatternValue).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                        var regex = GetOrCreateRegex(rule.Id + "_wc", pattern);
                        var match = regex.Match(line);
                        if (match.Success)
                        {
                            isMatch = true;
                            matchedText = match.Value;
                        }
                    }
                }
                catch
                {
                    // Ignore malformed patterns per runtime matching
                }

                if (isMatch)
                {
                    string sanitizedValue = matchedText;
                    if (rule.IsSensitive)
                    {
                        // Replace the matched text in the line, or just return masked matched text
                        // The requirement says: "mask out the specific matched text values (e.g. replacing secret text with ******)"
                        sanitizedValue = "******";
                    }

                    matches.Add(new ScanMatchEntry
                    {
                        RuleId = rule.Id,
                        CategoryName = rule.CategoryName,
                        FilePath = filePath,
                        FileName = fileName,
                        RawLine = line,
                        SanitizedValue = sanitizedValue,
                        LineNumber = lineNumber
                    });
                }
            }

            return matches;
        }

        private Regex GetOrCreateRegex(string ruleId, string pattern)
        {
            return _regexCache.GetOrAdd(ruleId, _ => new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        }

        public void ClearCache()
        {
            _regexCache.Clear();
        }
    }
}
