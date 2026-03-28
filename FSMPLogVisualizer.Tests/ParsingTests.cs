using FSMPLogVisualizer.Core;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;

namespace FSMPLogVisualizer.Tests
{
    public class ParsingTests
    {
        [Fact]
        public async Task Test_ParseVersion3_Log()
        {
            var testLogPath = "test_v3.log";
            File.WriteAllText(testLogPath, @"[16:44:22.378] [2840 ] [I] hdtsmp64 v3-1-9-0
[16:45:25.906] [2840 ] [T] msecs/activeSkeleton 0.02 activeSkeletons/maxActive/total 2/10/54 processTimeInMainLoop/targetTime 0.03/4.17
[16:48:32.572] [2840 ] [I] smp cost in main loop (msecs): 1.59, cost outside main loop: 4.85, percentage outside vs total: 75.348");

            var parser = new LogParser();
            var (session, dataPoints) = await parser.ParseLogFileAsync(testLogPath);

            session.Version.Should().Be("v3-1-9-0");
            
            // We should have 2 datapoints
            dataPoints.Should().HaveCount(2);
            
            var skelPoint = dataPoints[0];
            skelPoint.ActiveSkeletons.Should().Be(2);
            skelPoint.MsecsPerActiveSkeleton.Should().Be(0.02);
            skelPoint.TotalSkeletons.Should().Be(54);

            var costPoint = dataPoints[1];
            costPoint.CostInMainLoop.Should().Be(1.59);
            costPoint.CostOutsideMainLoop.Should().Be(4.85);
            costPoint.PercentageOutside.Should().Be(75.348);
            costPoint.ActiveSkeletons.Should().Be(2); // Inherited from previous line
        }

        [Fact]
        public async Task Test_ParseVersion2_Log()
        {
            var testLogPath = "test_v2.log";
            File.WriteAllText(testLogPath, @"hdtSMP64 200500
smp cost in main loop (msecs): 1.59, cost outside main loop: 4.85, percentage outside vs total: 75.348");

            var parser = new LogParser();
            var (session, dataPoints) = await parser.ParseLogFileAsync(testLogPath);

            session.Version.Should().Be("200500");
            
            dataPoints.Should().HaveCount(1);
            var costPoint = dataPoints.First();
            costPoint.CostInMainLoop.Should().Be(1.59);
            costPoint.CostOutsideMainLoop.Should().Be(4.85);
            costPoint.PercentageOutside.Should().Be(75.348);
            costPoint.ActiveSkeletons.Should().BeNull();
        }

        [Fact]
        public async Task Test_ParseVersion4_NewMetrics_Log()
        {
            var testLogPath = "test_v4.log";
            File.WriteAllText(testLogPath, @"[16:44:22.378] [2840 ] [I] hdtsmp64 v3-2-0-0
[10:28:18.831] [9652 ] [I] [SMP Metrics] Avg Frame-time Impact: 8.92ms (Setup: 0.11, Wait: 8.76, Apply: 0.05) | Avg Hidden Time: 3.40ms | Avg Total CPU Work: 12.32ms");

            var parser = new LogParser();
            var (session, dataPoints) = await parser.ParseLogFileAsync(testLogPath);

            session.Version.Should().Be("v3-2-0-0");
            
            dataPoints.Should().HaveCount(1);
            var costPoint = dataPoints.First();
            costPoint.CostInMainLoop.Should().Be(8.92);
            costPoint.CostOutsideMainLoop.Should().Be(3.40);
            
            // 3.40 / 12.32 * 100 = 27.597...
            costPoint.PercentageOutside.Should().BeApproximately(27.597, 0.01);
            costPoint.ActiveSkeletons.Should().BeNull();
        }
    }
}
