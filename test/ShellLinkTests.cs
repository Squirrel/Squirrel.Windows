using Squirrel.Shell;
using Xunit;

namespace Squirrel.Tests
{
    public class ShellLinkTests
    {
        [Theory]
        [InlineData(@"C:\MyApp\MyApp.exe", @"C:\MyApp", true)]
        [InlineData(@"C:\MyApp\MyApp.exe", @"C:\MyAppTwo", false)]
        public void IsTargetInDirectoryTest(
            string target,
            string directory,
            bool isTargetInDirectory)
        {
            var shellLink = new ShellLink
            {
                Target = target
            };
            Assert.Equal(isTargetInDirectory, shellLink.IsTargetInDirectory(directory));
        }
    }
}
