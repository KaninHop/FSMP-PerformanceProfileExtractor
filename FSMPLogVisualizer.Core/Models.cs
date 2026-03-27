using SQLite;
using System;

namespace FSMPLogVisualizer.Core
{
    public class LogRunSession
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        public string Version { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        
        // Use the first timestamp or a hash of the file name + size for deduplication
        public string SessionKey { get; set; } = string.Empty;
        
        public DateTime ImportedAt { get; set; }
    }

    public class LogDataPoint
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        
        [Indexed]
        public int SessionId { get; set; }

        public string Timestamp { get; set; } = string.Empty;

        // From "smp cost" line
        public double? CostInMainLoop { get; set; }
        public double? CostOutsideMainLoop { get; set; }
        public double? PercentageOutside { get; set; }

        // From "activeSkeletons" line
        public double? MsecsPerActiveSkeleton { get; set; }
        public int? ActiveSkeletons { get; set; }
        public int? MaxActiveSkeletons { get; set; }
        public int? TotalSkeletons { get; set; }
        public double? ProcessTimeInMainLoop { get; set; }
        public double? TargetTime { get; set; }
    }
}
