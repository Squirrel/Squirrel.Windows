using Xunit;

namespace Squirrel.Tests
{
    public class StringExtensionsTests
    {
        public class TheGetFinalUrlMethod
        {
            [Fact]
            public void ReturnsValidUrlForUpperCaseUrl()
            {
                var url = "https://downloads.myprovider.com/MyProduct/beta/RELEASES".GetFinalUrl();

                Assert.StrictEqual("https://downloads.myprovider.com/myproduct/beta/releases", url);
            }
        }
    }
}
