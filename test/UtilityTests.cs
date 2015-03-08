using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Splat;
using Squirrel;
using Squirrel.Tests.TestHelpers;
using Xunit;

namespace Squirrel.Tests.Core
{
    public class UtilityTests : IEnableLogger
    {
        [Fact]
        public void SetAppIdOnShortcutTest()
        {
            var sl = new ShellLink() {
                Target = @"C:\Windows\Notepad.exe",
                Description = "It's Notepad",
            };

            sl.SetAppUserModelId("org.paulbetts.test");
            var path = Path.GetFullPath(@".\test.lnk");
            sl.Save(path);

            Console.WriteLine("Saved to " + path);
        }

        [Theory]
        [InlineData(10, 1)]
        [InlineData(100, 1)]
        [InlineData(0, 1)]
        [InlineData(30000, 2)]
        [InlineData(50000, 2)]
        [InlineData(10000000, 3)]
        public void TestTempNameGeneration(int index, int expectedLength)
        {
            string result = Utility.tempNameForIndex(index, "");
            Assert.Equal(result.Length, expectedLength);
        }

        [Fact]
        public void RemoveByteOrderMarkerIfPresent()
        {
            var utf32Be = new byte[] { 0x00, 0x00, 0xFE, 0xFF };
            var utf32Le = new byte[] { 0xFF, 0xFE, 0x00, 0x00 };
            var utf16Be = new byte[] { 0xFE, 0xFF };
            var utf16Le = new byte[] { 0xFF, 0xFE };
            var utf8 = new byte[] { 0xEF, 0xBB, 0xBF };

            var utf32BeHelloWorld = combine(utf32Be, Encoding.UTF8.GetBytes("hello world"));
            var utf32LeHelloWorld = combine(utf32Le, Encoding.UTF8.GetBytes("hello world"));
            var utf16BeHelloWorld = combine(utf16Be, Encoding.UTF8.GetBytes("hello world"));
            var utf16LeHelloWorld = combine(utf16Le, Encoding.UTF8.GetBytes("hello world"));
            var utf8HelloWorld = combine(utf8, Encoding.UTF8.GetBytes("hello world"));

            var asciiMultipleChars = Encoding.ASCII.GetBytes("hello world");
            var asciiSingleChar = Encoding.ASCII.GetBytes("A");

            var emptyString = string.Empty;
            string nullString = null;
            byte[] nullByteArray = {};
            Assert.Equal(string.Empty, Utility.RemoveByteOrderMarkerIfPresent(emptyString));
            Assert.Equal(string.Empty, Utility.RemoveByteOrderMarkerIfPresent(nullString));
            Assert.Equal(string.Empty, Utility.RemoveByteOrderMarkerIfPresent(nullByteArray));

            Assert.Equal(string.Empty, Utility.RemoveByteOrderMarkerIfPresent(utf32Be));
            Assert.Equal(string.Empty, Utility.RemoveByteOrderMarkerIfPresent(utf32Le));
            Assert.Equal(string.Empty, Utility.RemoveByteOrderMarkerIfPresent(utf16Be));
            Assert.Equal(string.Empty, Utility.RemoveByteOrderMarkerIfPresent(utf16Le));
            Assert.Equal(string.Empty, Utility.RemoveByteOrderMarkerIfPresent(utf8));

            Assert.Equal("hello world", Utility.RemoveByteOrderMarkerIfPresent(utf32BeHelloWorld));
            Assert.Equal("hello world", Utility.RemoveByteOrderMarkerIfPresent(utf32LeHelloWorld));
            Assert.Equal("hello world", Utility.RemoveByteOrderMarkerIfPresent(utf16BeHelloWorld));
            Assert.Equal("hello world", Utility.RemoveByteOrderMarkerIfPresent(utf16LeHelloWorld));
            Assert.Equal("hello world", Utility.RemoveByteOrderMarkerIfPresent(utf8HelloWorld));

            Assert.Equal("hello world", Utility.RemoveByteOrderMarkerIfPresent(asciiMultipleChars));
            Assert.Equal("A", Utility.RemoveByteOrderMarkerIfPresent(asciiSingleChar));
        }

        [Fact]
        public void ShaCheckShouldBeCaseInsensitive()
        {
            var sha1FromExternalTool = "75255cfd229a1ed1447abe1104f5635e69975d30";
            var inputPackage = IntegrationTestHelper.GetPath("fixtures", "Squirrel.Core.1.0.0.0.nupkg");
            var stream = File.OpenRead(inputPackage);
            var sha1 = Utility.CalculateStreamSHA1(stream);

            Assert.NotEqual(sha1FromExternalTool, sha1);
            Assert.Equal(sha1FromExternalTool, sha1, StringComparer.OrdinalIgnoreCase);
        }

        [Fact(Skip="This test takes forever")]
        public void CanDeleteDeepRecursiveDirectoryStructure()
        {
            string tempDir;
            using (Utility.WithTempDirectory(out tempDir)) {
                for (var i = 0; i < 50; i++) {
                    var directory = Path.Combine(tempDir, newId());
                    CreateSampleDirectory(directory);
                }

                var files = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);

                var count = files.Count();

                this.Log().Info("Created {0} files under directory {1}", count, tempDir);

                var sw = new Stopwatch();
                sw.Start();
                Utility.DeleteDirectory(tempDir).Wait();
                sw.Stop();
                this.Log().Info("Delete took {0}ms", sw.ElapsedMilliseconds);

                Assert.False(Directory.Exists(tempDir));
            }
        }

        [Fact]
        public void CreateFakePackageSmokeTest()
        {
            string path;
            using (Utility.WithTempDirectory(out path)) {
                var output = IntegrationTestHelper.CreateFakeInstalledApp("0.3.0", path);
                Assert.True(File.Exists(output));
            }
        }

        static void CreateSampleDirectory(string directory)
        {
            Random prng = new Random();
            while (true) {
                Directory.CreateDirectory(directory);

                for (var j = 0; j < 100; j++) {
                    var file = Path.Combine(directory, newId());
                    if (file.Length > 260) continue;
                    File.WriteAllText(file, Guid.NewGuid().ToString());
                }

                if (prng.NextDouble() > 0.5) {
                    var childDirectory = Path.Combine(directory, newId());
                    if (childDirectory.Length > 248) return;
                    directory = childDirectory;
                    continue;
                }

                break;
            }
        }

        static string newId()
        {
            var text = Guid.NewGuid().ToString();
            var bytes = Encoding.Unicode.GetBytes(text);
            var provider = new SHA1Managed();
            var hashString = string.Empty;

            foreach (var x in provider.ComputeHash(bytes)) {
                hashString += String.Format("{0:x2}", x);
            }

            if (hashString.Length > 7) {
                return hashString.Substring(0, 7);
            }

            return hashString;
        }

        static byte[] combine(params byte[][] arrays)
        {
            var rv = new byte[arrays.Sum(a => a.Length)];
            var offset = 0;
            foreach (var array in arrays)
            {
                Buffer.BlockCopy(array, 0, rv, offset, array.Length);
                offset += array.Length;
            }
            return rv;
        }

    }
}
