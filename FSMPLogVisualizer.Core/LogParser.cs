using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FSMPLogVisualizer.Core
{
    public class LogParser
    {
        // "smp cost in main loop (msecs): {:.2f}, cost outside main loop: {:.2f}, percentage outside vs total: {:.2f}"
        private static readonly Regex smpCostRegex = new Regex(
            @"smp cost in main loop \(msecs\):\s*([\d\.]+),\s*cost outside main loop:\s*([\d\.]+),\s*percentage outside vs total:\s*([\d\.]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // "msecs/activeSkeleton {:.2f} activeSkeletons/maxActive/total {}/{}/{} processTimeInMainLoop/targetTime {:.2f}/{:.2f}"
        private static readonly Regex activeSkeletonsRegex = new Regex(
            @"msecs/activeSkeleton\s*([\d\.]+)\s*activeSkeletons/maxActive/total\s*(\d+)/(\d+)/(\d+)\s*processTimeInMainLoop/targetTime\s*([\d\.]+)/([\d\.]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Extracts timestamp "[16:45:25.906]"
        private static readonly Regex timestampRegex = new Regex(@"^\[([\d:\.]+)\]", RegexOptions.Compiled);

        public async Task<(LogRunSession session, List<LogDataPoint> dataPoints)> ParseLogFileAsync(string filePath, string existingLastTimestamp = null)
        {
            var dataPoints = new List<LogDataPoint>();
            
            var session = new LogRunSession
            {
                FileName = Path.GetFileName(filePath),
                ImportedAt = DateTime.Now
            };

            bool skipUntilNew = !string.IsNullOrEmpty(existingLastTimestamp);

            using var reader = new StreamReader(filePath);
            string? line;
            
            // Read version from first line if possible
            if ((line = await reader.ReadLineAsync()) != null)
            {
                session.Version = ExtractVersion(line);
                session.SessionKey = $"{session.FileName}_{session.Version}"; // Basic key
            }

            // Stateful tracking
            int? currentActiveSkeletons = null;
            int? currentMaxActive = null;
            int? currentTotal = null;
            double? currentMsecsPerAct = null;
            double? currentProcTime = null;
            double? currentTargetTime = null;

            while ((line = await reader.ReadLineAsync()) != null)
            {
                var tsMatch = timestampRegex.Match(line);
                string timestamp = tsMatch.Success ? tsMatch.Groups[1].Value : string.Empty;

                if (skipUntilNew && tsMatch.Success)
                {
                    if (timestamp == existingLastTimestamp)
                    {
                        skipUntilNew = false; // We found the last known timestamp, resume parsing after this
                    }
                    continue;
                }

                if (skipUntilNew) continue;

                var skelMatch = activeSkeletonsRegex.Match(line);
                if (skelMatch.Success)
                {
                    currentMsecsPerAct = double.Parse(skelMatch.Groups[1].Value);
                    currentActiveSkeletons = int.Parse(skelMatch.Groups[2].Value);
                    currentMaxActive = int.Parse(skelMatch.Groups[3].Value);
                    currentTotal = int.Parse(skelMatch.Groups[4].Value);
                    currentProcTime = double.Parse(skelMatch.Groups[5].Value);
                    currentTargetTime = double.Parse(skelMatch.Groups[6].Value);

                    dataPoints.Add(new LogDataPoint
                    {
                        Timestamp = timestamp,
                        MsecsPerActiveSkeleton = currentMsecsPerAct,
                        ActiveSkeletons = currentActiveSkeletons,
                        MaxActiveSkeletons = currentMaxActive,
                        TotalSkeletons = currentTotal,
                        ProcessTimeInMainLoop = currentProcTime,
                        TargetTime = currentTargetTime
                    });
                }

                var costMatch = smpCostRegex.Match(line);
                if (costMatch.Success)
                {
                    // Cost match means we emit a datapoint with the cost, and optionally the latest skeleton data if we have it
                    dataPoints.Add(new LogDataPoint
                    {
                        Timestamp = timestamp,
                        CostInMainLoop = double.Parse(costMatch.Groups[1].Value),
                        CostOutsideMainLoop = double.Parse(costMatch.Groups[2].Value),
                        PercentageOutside = double.Parse(costMatch.Groups[3].Value),
                        
                        // Associate with the most recent skeleton count to allow grouping by skeletons
                        ActiveSkeletons = currentActiveSkeletons,
                        MaxActiveSkeletons = currentMaxActive,
                        TotalSkeletons = currentTotal,
                    });
                }
            }

            return (session, dataPoints);
        }

        private string ExtractVersion(string firstLine)
        {
            // e.g., "hdtSMP64 200500" or "[16:44:22.378] [2840 ] [I] hdtsmp64 v3-1-9-0"
            if (firstLine.Contains("v3", StringComparison.OrdinalIgnoreCase) || firstLine.Contains("hdtsmp", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(firstLine, @"(v\d+-\d+-\d+-\d+|\d{6})", RegexOptions.IgnoreCase);
                if (match.Success) return match.Value;
                
                var vMatch = Regex.Match(firstLine, @"hdtSMP64\s+(.*)", RegexOptions.IgnoreCase);
                if (vMatch.Success) return vMatch.Groups[1].Value.Trim();
            }
            return "Unknown";
        }
    }
}
