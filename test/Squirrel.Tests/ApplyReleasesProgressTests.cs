using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Squirrel.Tests
{
    public class ApplyReleasesProgressTests
    {

        [Fact(Skip = "Test does not pass consistently due to dependency on Task.Delay()")]
        public async Task CalculatesPercentageCorrectly()
        {
            // Just 1 complex situation should be enough to cover this

            var percentage = 0;
            var progress = new ApplyReleasesProgress(5, x => percentage = x);

            // 2 releases already finished
            progress.FinishRelease();
            progress.FinishRelease();

            // Report 40 % in current release
            progress.ReportReleaseProgress(50);

            // Required for callback to be invoked
            await Task.Delay(50);

            // 20 per release
            // 10 because we are half-way the 3rd release
            var expectedProgress = 20 + 20 + 10;

            Assert.Equal(expectedProgress, percentage);
        }
    }
}
