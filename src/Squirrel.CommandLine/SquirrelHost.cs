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
    public class SquirrelHost
    {
        public static int Main(string[] args)
        {
            var logger = ConsoleLogger.RegisterLogger();

            bool help = false;
            bool verbose = false;
            string xplat = null;
            var globalOptions = new OptionSet() {
                { "h|?|help", "Ignores all other arguments and shows help text", _ => help = true },
                { "x|xplat=", "Select {PLATFORM} to cross-compile for (eg. win, osx)", v => xplat = v },
                { "verbose", "Print all diagnostic messages", _ => verbose = true },
            };

            var exeName = Path.GetFileName(SquirrelRuntimeInfo.EntryExePath);
            string sqUsage =
                $"Squirrel {SquirrelRuntimeInfo.SquirrelDisplayVersion}, tool for creating Squirrel releases" + Environment.NewLine +
                $"Usage: {exeName} [verb] [--option=value]";

            try {
                globalOptions.Parse(args);

                if (xplat == null)
                    xplat = SquirrelRuntimeInfo.SystemOsName;

                CommandSet packageCommands;

                switch (xplat.ToLower()) {
                case "win":
                case "windows":
                    if (!SquirrelRuntimeInfo.IsWindows)
                        logger.Write("Cross-compiling will cause some features of Squirrel to be disabled.", LogLevel.Warn);
                    packageCommands = Windows.CommandsWindows.GetCommands();
                    break;

                case "mac":
                case "osx":
                case "macos":
                    if (!SquirrelRuntimeInfo.IsOSX)
                        logger.Write("Cross-compiling will cause some features of Squirrel to be disabled.", LogLevel.Warn);
                    packageCommands = OSX.CommandsOSX.GetCommands();
                    break;

                default:
                    throw new NotSupportedException("Unsupported OS platform: " + xplat);
                }

                var commands = new CommandSet {
                    "",
                    sqUsage,
                    "",
                    "[ Global Options ]",
                    globalOptions.GetHelpText().TrimEnd(),
                    "",
                    packageCommands,
                    "",
                    "[ Package Deployment / Syncing ]",
                    { "s3-down", "Download releases from S3 compatible API", new SyncS3Options(), o => Download(new S3Repository(o)) },
                    { "s3-up", "Upload releases to S3 compatible API", new SyncS3Options(), o => Upload(new S3Repository(o)) },
                    { "http-down", "Download releases from an HTTP source", new SyncHttpOptions(), o => Download(new SimpleWebRepository(o)) },
                    { "github-down", "Download releases from GitHub", new SyncGithubOptions(), o => Download(new GitHubRepository(o)) },
                };

                if (verbose) {
                    logger.Level = LogLevel.Debug;
                }

                if (help) {
                    commands.WriteHelp();
                    return 0;
                }

                try {
                    // parse cli and run command
                    commands.Execute(args);
                    return 0;
                } catch (Exception ex) when (ex is OptionValidationException || ex is OptionException) {
                    // if the arguments fail to validate, print argument help
                    Console.WriteLine();
                    logger.Write(ex.Message, LogLevel.Error);
                    commands.WriteHelp();
                    Console.WriteLine();
                    logger.Write(ex.Message, LogLevel.Error);
                    return -1;
                }
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