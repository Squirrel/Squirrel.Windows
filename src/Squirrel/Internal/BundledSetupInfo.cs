using System;
using System.Text;
using Microsoft.NET.HostModel;

namespace Squirrel.Lib
{
    internal class BundledSetupInfo
    {
        public string AppId { get; set; }
        public string AppFriendlyName { get; set; } = "This Application";
        public string[] RequiredFrameworks { get; set; } = new string[0];
        public string BundledPackageName { get; set; }
        public byte[] BundledPackageBytes { get; set; }
        public byte[] SplashImageBytes { get; set; }
        public byte[] SetupIconBytes { get; set; }

        private const string RESOURCE_TYPE = "DATA";
        private static readonly ushort RESOURCE_LANG = 0x0409;

        public static BundledSetupInfo ReadFromFile(string exePath)
        {
            var bundle = new BundledSetupInfo();
            using var reader = new ResourceReader(exePath);
            bundle.AppId = ReadString(reader, 200);
            bundle.AppFriendlyName = ReadString(reader, 201);
            bundle.SplashImageBytes = ReadBytes(reader, 202);
            bundle.RequiredFrameworks = ReadString(reader, 203)?.Split(',') ?? new string[0];
            bundle.BundledPackageName = ReadString(reader, 204);
            bundle.BundledPackageBytes = ReadBytes(reader, 205);
            bundle.SetupIconBytes = ReadBytes(reader, 206);

            return bundle;
        }

        private static byte[] ReadBytes(ResourceReader reader, int idx)
        {
            return reader.ReadResource(RESOURCE_TYPE, new IntPtr(idx), RESOURCE_LANG);
        }

        private static string ReadString(ResourceReader reader, int idx)
        {
            var bytes = ReadBytes(reader, idx);
            if (bytes == null) return null;
            return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        }

        public void WriteToFile(string exePath)
        {
            using var writer = new ResourceUpdater(exePath);
            WriteValue(writer, 200, AppId);
            WriteValue(writer, 201, AppFriendlyName);
            WriteValue(writer, 202, SplashImageBytes);
            WriteValue(writer, 203, String.Join(",", RequiredFrameworks ?? new string[0]));
            WriteValue(writer, 204, BundledPackageName);
            WriteValue(writer, 205, BundledPackageBytes);
            WriteValue(writer, 206, SetupIconBytes);
            writer.Update();
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
            updater.AddResource(buf, RESOURCE_TYPE, new IntPtr(idx), RESOURCE_LANG);
        }
    }
}
