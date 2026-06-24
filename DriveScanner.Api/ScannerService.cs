using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using DriveScanner.Api.Hubs;
using DriveScanner.Api.Models;

namespace DriveScanner.Api.Services
{
    public class ScannerService
    {
        private readonly IHubContext<ScanHub> _hubContext;
        private readonly ScanResultStore _resultStore;
        private readonly EvaluatorService _evaluatorService;
        private CancellationTokenSource? _cts;

        public ScannerService(
            IHubContext<ScanHub> hubContext,
            ScanResultStore resultStore,
            EvaluatorService evaluatorService)
        {
            _hubContext = hubContext;
            _resultStore = resultStore;
            _evaluatorService = evaluatorService;
        }

        public void CancelScan()
        {
            _cts?.Cancel();
        }

        public Task StartScanAsync(ScanRequest request, string sessionId)
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            // Run in background task
            _ = Task.Run(() => PerformScan(request, sessionId, token), token);
            return Task.CompletedTask;
        }

        private async Task PerformScan(ScanRequest request, string sessionId, CancellationToken token)
        {
            _resultStore.Clear();
            _resultStore.ActiveRules = request.SearchRules;
            _evaluatorService.ClearCache();

            var progress = _resultStore.Progress;
            progress.IsCompleted = false;

            try
            {
                if (!Directory.Exists(request.TargetPath))
                {
                    progress.ErrorMessage = $"Directory '{request.TargetPath}' does not exist.";
                    progress.IsCompleted = true;
                    await SendProgressUpdate(sessionId, progress);
                    return;
                }

                // We will hold unique values in concurrent set to update unique counters
                var uniqueCategoryValues = new ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>();
                foreach (var rule in request.SearchRules)
                {
                    uniqueCategoryValues.TryAdd(rule.CategoryName, new ConcurrentDictionary<string, byte>());
                    progress.CategoryMatchCounts[rule.CategoryName] = 0;
                    progress.CategoryUniqueCounts[rule.CategoryName] = 0;
                }

                progress.TotalFilesFound = 0;
                progress.FilesScanned = 0;

                int scannedCount = 0;
                DateTime lastProgressTime = DateTime.MinValue;

                // Traverse and scan files on-the-fly
                await ScanRecursive(
                    request.TargetPath,
                    request.Blacklist,
                    request.Extensions,
                    request.SearchRules,
                    uniqueCategoryValues,
                    progress,
                    token,
                    async () =>
                    {
                        scannedCount++;
                        progress.FilesScanned = scannedCount;

                        // Throttle SignalR updates to every 150ms to keep UI responsive
                        if ((DateTime.UtcNow - lastProgressTime).TotalMilliseconds > 150)
                        {
                            await SendProgressUpdate(sessionId, progress);
                            lastProgressTime = DateTime.UtcNow;
                        }
                    }
                );

                progress.IsCompleted = true;
                progress.CurrentFile = "Done";
                await SendProgressUpdate(sessionId, progress);
            }
            catch (Exception ex)
            {
                progress.ErrorMessage = ex.Message;
                progress.IsCompleted = true;
                await SendProgressUpdate(sessionId, progress);
            }
        }

        private async Task ScanRecursive(
            string currentDir,
            List<string> blacklist,
            List<string> extensions,
            List<SearchRule> rules,
            ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> uniqueCategoryValues,
            ScanProgressUpdate progress,
            CancellationToken token,
            Func<Task> onFileScanned)
        {
            if (token.IsCancellationRequested) return;

            try
            {
                // Check if directory is in blacklist
                string dirName = Path.GetFileName(currentDir);
                if (blacklist.Any(b => !string.IsNullOrEmpty(b) &&
                    (dirName.Equals(b, StringComparison.OrdinalIgnoreCase) ||
                     currentDir.Contains(Path.DirectorySeparatorChar + b + Path.DirectorySeparatorChar) ||
                     currentDir.EndsWith(Path.DirectorySeparatorChar + b))))
                {
                    return;
                }

                // Process files in the current folder
                string[] files;
                try
                {
                    files = Directory.GetFiles(currentDir);
                }
                catch
                {
                    // Skip directory if files listing fails (e.g. system permissions/locks)
                    return;
                }

                foreach (var file in files)
                {
                    if (token.IsCancellationRequested) return;

                    string ext = Path.GetExtension(file);
                    if (extensions.Count == 0 || extensions.Any(e => ext.Equals(e, StringComparison.OrdinalIgnoreCase)))
                    {
                        progress.TotalFilesFound++;
                        progress.CurrentFile = file;

                        await ScanFile(file, rules, uniqueCategoryValues, progress, token);
                        await onFileScanned();
                    }
                }

                // Recurse sub-directories
                string[] subDirs;
                try
                {
                    subDirs = Directory.GetDirectories(currentDir);
                }
                catch
                {
                    // Skip directory if sub-directories listing fails
                    return;
                }

                foreach (var dir in subDirs)
                {
                    await ScanRecursive(dir, blacklist, extensions, rules, uniqueCategoryValues, progress, token, onFileScanned);
                }
            }
            catch
            {
                // General error protection
            }
        }

        private async Task ScanFile(
            string filePath,
            List<SearchRule> rules,
            ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> uniqueCategoryValues,
            ScanProgressUpdate progress,
            CancellationToken token)
        {
            try
            {
                string fileName = Path.GetFileName(filePath);
                int lineNum = 0;

                using (var reader = new StreamReader(filePath))
                {
                    string? line;
                    while ((line = await reader.ReadLineAsync(token)) != null)
                    {
                        lineNum++;
                        if (token.IsCancellationRequested) break;

                        var matches = _evaluatorService.EvaluateLine(line, lineNum, filePath, fileName, rules);
                        foreach (var match in matches)
                        {
                            _resultStore.Matches.Add(match);

                            // Update stats
                            if (progress.CategoryMatchCounts.ContainsKey(match.CategoryName))
                            {
                                progress.CategoryMatchCounts[match.CategoryName]++;
                            }

                            if (uniqueCategoryValues.TryGetValue(match.CategoryName, out var uniqueSet))
                            {
                                if (uniqueSet.TryAdd(match.SanitizedValue, 0))
                                {
                                    if (progress.CategoryUniqueCounts.ContainsKey(match.CategoryName))
                                    {
                                        progress.CategoryUniqueCounts[match.CategoryName]++;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Skip files that cannot be opened/read (e.g. locks or access denied)
            }
        }

        private async Task SendProgressUpdate(string sessionId, ScanProgressUpdate progress)
        {
            await _hubContext.Clients.Group(sessionId).SendAsync("ReceiveProgress", progress);
        }
    }
}
