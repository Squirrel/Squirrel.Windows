using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NuGet.Commands;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Versioning;
using Squirrel.SimpleSplat;

namespace SquirrelCli
{
    internal class NugetConsole : NuGet.Common.ILogger, IEnableLogger
    {
        public void Pack(string nuspecPath, string baseDirectory, string outputDirectory)
        {
            this.Log().Info($"Starting to package '{nuspecPath}'");
            var args = new PackArgs() {
                Deterministic = true,
                BasePath = baseDirectory,
                OutputDirectory = outputDirectory,
                Path = nuspecPath,
                Exclude = Enumerable.Empty<string>(),
                Arguments = Enumerable.Empty<string>(),
                Logger = this,
                ExcludeEmptyDirectories = true,
                NoDefaultExcludes = true,
                NoPackageAnalysis = true,
            };

            var c = new PackCommandRunner(args, null);
            if (!c.RunPackageBuild())
                throw new Exception("Error creating nuget package.");
        }

        #region NuGet.Common.ILogger
        public void Log(NuGet.Common.LogLevel level, string data)
        {
            this.Log().Info(data);
        }

        public void Log(NuGet.Common.ILogMessage message)
        {
            this.Log().Info(message.Message);
        }

        public Task LogAsync(NuGet.Common.LogLevel level, string data)
        {
            this.Log().Info(data);
            return Task.CompletedTask;
        }

        public Task LogAsync(NuGet.Common.ILogMessage message)
        {
            this.Log().Info(message.Message);
            return Task.CompletedTask;
        }

        public void LogDebug(string data)
        {
            this.Log().Debug(data);
        }

        public void LogError(string data)
        {
            this.Log().Error(data);
        }

        public void LogInformation(string data)
        {
            this.Log().Info(data);
        }

        public void LogInformationSummary(string data)
        {
            this.Log().Info(data);
        }

        public void LogMinimal(string data)
        {
            this.Log().Info(data);
        }

        public void LogVerbose(string data)
        {
            this.Log().Debug(data);
        }

        public void LogWarning(string data)
        {
            this.Log().Warn(data);
        }
        #endregion NuGet.Common.ILogger
    }
}