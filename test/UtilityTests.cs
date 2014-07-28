using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text;
using ReactiveUIMicro;
using Squirrel.Core;
using Squirrel.Tests.TestHelpers;
using Xunit;

namespace Squirrel.Tests.Core
{
    public class UtilityTests : IEnableLogger
    {
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

        [Fact]
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

        static void CreateSampleDirectory(string directory)
        {
            while (true) {
                Directory.CreateDirectory(directory);

                for (var j = 0; j < 100; j++) {
                    var file = Path.Combine(directory, newId());
                    if (file.Length > 260) continue;
                    File.WriteAllText(file, Guid.NewGuid().ToString());
                }

                if (new Random().NextDouble() > 0.5) {
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
    }
}
