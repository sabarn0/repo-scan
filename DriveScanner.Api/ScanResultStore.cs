using System.Collections.Concurrent;
using System.Collections.Generic;
using DriveScanner.Api.Models;

namespace DriveScanner.Api.Services
{
    public class ScanResultStore
    {
        public ConcurrentBag<ScanMatchEntry> Matches { get; set; } = new();
        public ScanProgressUpdate Progress { get; set; } = new();
        public List<SearchRule> ActiveRules { get; set; } = new();

        public void Clear()
        {
            Matches = new ConcurrentBag<ScanMatchEntry>();
            Progress = new ScanProgressUpdate();
            ActiveRules = new List<SearchRule>();
        }
    }
}
