// https://github.com/egramtel/dotnet-bundle/blob/master/DotNet.Bundle/StructureBuilder.cs

using System;
using System.IO;
using Squirrel.SimpleSplat;

namespace Squirrel.CommandLine.OSX
{
    public class StructureBuilder : IEnableLogger
    {
        private readonly string _id;
        private readonly string _outputDir;
        private readonly string _appDir;

        public StructureBuilder(string appDir)
        {
            _appDir = appDir;
        }

        public StructureBuilder(string id, string outputDir)
        {
            _id = id;
            _outputDir = outputDir;
        }

        public string AppDirectory => _appDir ?? Path.Combine(Path.Combine(_outputDir, _id + ".app"));

        public string ContentsDirectory => Path.Combine(AppDirectory, "Contents");

        public string MacosDirectory => Path.Combine(ContentsDirectory, "MacOS");

        public string ResourcesDirectory => Path.Combine(ContentsDirectory, "Resources");

        public void Build()
        {
            if (string.IsNullOrEmpty(_outputDir))
                throw new NotSupportedException();

            Directory.CreateDirectory(_outputDir);

            if (Directory.Exists(AppDirectory)) {
                Directory.Delete(AppDirectory, true);
            }

            Directory.CreateDirectory(AppDirectory);
            Directory.CreateDirectory(ContentsDirectory);
            Directory.CreateDirectory(MacosDirectory);
            Directory.CreateDirectory(ResourcesDirectory);
        }
    }
}
