using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mono.Options;
using Squirrel.CommandLine.Sync;
using Squirrel.SimpleSplat;

namespace Squirrel.CommandLine
{
    internal class SquirrelHost
    {
#pragma warning disable CS0436 // Type conflicts with imported type
        public static string DisplayVersion => ThisAssembly.AssemblyInformationalVersion + (ThisAssembly.IsPublicRelease ? "" : " (prerelease)");
        public static string FileVersion => ThisAssembly.AssemblyFileVersion;
#pragma warning restore CS0436 // Type conflicts with imported type

        public static int Run(string[] args, CommandSet packageCommands)
        {
            var logger = ConsoleLogger.RegisterLogger();

            bool help = false;
            bool verbose = false;
            var globalOptions = new OptionSet() {
                { "h|?|help", "Ignores all other arguments and shows help text", _ => help = true },
                { "verbose", "Print extra diagnostic logging", _ => verbose = true },
            };

            var exeName = Path.GetFileName(SquirrelRuntimeInfo.EntryExePath);
            string sqUsage =
                $"Squirrel {DisplayVersion}, tool for creating and deploying Squirrel releases" + Environment.NewLine +
                $"Usage: {exeName} [verb] [--option:value]";

            var commands = new CommandSet {
                "",
                sqUsage,
                "",
                "[ Global Options ]",
                globalOptions.GetHelpText().TrimEnd(),
                "",
                packageCommands,
                //"[ Package Authoring ]",
                //{ "pack", "Creates a Squirrel release from a folder containing application files", new PackOptions(), Pack },
                //{ "releasify", "Take an existing nuget package and convert it into a Squirrel release", new ReleasifyOptions(), Releasify },
                "",
                "[ Package Deployment / Syncing ]",
                { "s3-down", "Download releases from S3 compatible API", new SyncS3Options(), o => Download(new S3Repository(o)) },
                { "s3-up", "Upload releases to S3 compatible API", new SyncS3Options(), o => Upload(new S3Repository(o)) },
                { "http-down", "Download releases from an HTTP source", new SyncHttpOptions(), o => Download(new SimpleWebRepository(o)) },
                { "github-down", "Download releases from GitHub", new SyncGithubOptions(), o => Download(new GitHubRepository(o)) },
                //"",
                //"[ Examples ]",
                //$"    {exeName} pack ",
                //$"        ",
            };

            try {
                globalOptions.Parse(args);

                if (verbose) {
                    logger.Level = LogLevel.Debug;
                }

                if (help) {
                    commands.WriteHelp();
                    return 0;
                } else {
                    // parse cli and run command
                    commands.Execute(args);
                }

                return 0;
            } catch (Exception ex) when (ex is OptionValidationException || ex is OptionException) {
                // if the arguments fail to validate, print argument help
                Console.WriteLine();
                logger.Write(ex.Message, LogLevel.Error);
                commands.WriteHelp();
                Console.WriteLine();
                logger.Write(ex.Message, LogLevel.Error);
                return -1;
            } catch (Exception ex) {
                // for other errors, just print the error and short usage instructions
                Console.WriteLine();
                logger.Write(ex.ToString(), LogLevel.Error);
                Console.WriteLine();
                Console.WriteLine(sqUsage);
                Console.WriteLine($" > '{exeName} -h' to see program help.");
                return -1;
            }
        }

        static void Upload<T>(T repo) where T : IPackageRepository => repo.UploadMissingPackages().GetAwaiter().GetResult();

        static void Download<T>(T repo) where T : IPackageRepository => repo.DownloadRecentPackages().GetAwaiter().GetResult();
    }
}
