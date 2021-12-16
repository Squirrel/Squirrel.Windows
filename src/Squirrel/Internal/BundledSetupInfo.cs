using System;
using System.Text;
using Microsoft.NET.HostModel;

namespace Squirrel.Lib
{
    internal class BundledSetupInfo
    {
        public string AppName { get; set; } = "This Application";
        public string[] RequiredFrameworks { get; set; } = new string[0];
        public string BundledPackageName { get; set; }
        public byte[] BundledPackageBytes { get; set; }
        public byte[] SplashImageBytes { get; set; }
        public byte[] SetupIconBytes { get; set; }

        private const string RESOURCE_TYPE = "DATA";

        public static BundledSetupInfo ReadFromFile(string exePath)
        {
            var bundle = new BundledSetupInfo();
            using var reader = new ResourceReader(exePath);
            bundle.AppName = ReadString(reader, 201);
            bundle.SplashImageBytes = ReadBytes(reader, 202);
            bundle.RequiredFrameworks = ReadString(reader, 203)?.Split(',');
            bundle.BundledPackageName = ReadString(reader, 204);
            bundle.BundledPackageBytes = ReadBytes(reader, 205);
            bundle.SetupIconBytes = ReadBytes(reader, 206);
            return bundle;
        }

        private static byte[] ReadBytes(ResourceReader reader, int idx)
        {
            return reader.ReadResource(new IntPtr(idx), RESOURCE_TYPE);
        }

        private static string ReadString(ResourceReader reader, int idx)
        {
            var bytes = ReadBytes(reader, idx);
            if (bytes == null) return null;
            return Encoding.Unicode.GetString(bytes);
        }

        public void WriteToFile(string exePath)
        {
            using var writer = new ResourceUpdater(exePath);
            WriteValue(writer, 201, AppName);
            WriteValue(writer, 202, SplashImageBytes);
            WriteValue(writer, 203, String.Join(",", RequiredFrameworks));
            WriteValue(writer, 204, BundledPackageName);
            WriteValue(writer, 205, BundledPackageBytes);
            WriteValue(writer, 206, SetupIconBytes);
        }

        private void WriteValue(ResourceUpdater updater, int idx, string str)
        {
            if (String.IsNullOrWhiteSpace(str)) return;
            var bytes = Encoding.Unicode.GetBytes(String.Concat(str, "\0\0"));
            WriteValue(updater, idx, bytes);
        }

        private void WriteValue(ResourceUpdater updater, int idx, byte[] buf)
        {
            if (buf == null || buf.Length == 0) return;
            updater.AddResource(buf, RESOURCE_TYPE, new IntPtr(idx));
        }
    }
}
