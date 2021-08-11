using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace NuGet
{
    public static class XmlUtility
    {
        public static XDocument LoadSafe(string filePath)
        {
            var settings = CreateSafeSettings();
            using (var reader = XmlReader.Create(filePath, settings))
            {
                return XDocument.Load(reader);
            }
        }

        public static XDocument LoadSafe(Stream input)
        {
            var settings = CreateSafeSettings();
            var reader = XmlReader.Create(input, settings);
            return XDocument.Load(reader);
        }

        public static XDocument LoadSafe(Stream input, bool ignoreWhiteSpace)
        {
            var settings = CreateSafeSettings(ignoreWhiteSpace);
            var reader = XmlReader.Create(input, settings);
            return XDocument.Load(reader);
        }

        public static XDocument LoadSafe(Stream input, LoadOptions options)
        {
            var settings = CreateSafeSettings();
            var reader = XmlReader.Create(input, settings);
            return XDocument.Load(reader, options);
        }

        private static XmlReaderSettings CreateSafeSettings(bool ignoreWhiteSpace = false)
        {
            var safeSettings = new XmlReaderSettings
            {
                XmlResolver = null,
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreWhitespace = ignoreWhiteSpace
            };

            return safeSettings;
        }

        internal static XDocument GetOrCreateDocument(XName rootName, IFileSystem fileSystem, string path)
        {
            if (fileSystem.FileExists(path))
            {
                try
                {
                    return GetDocument(fileSystem, path);
                }
                catch (FileNotFoundException)
                {
                    return CreateDocument(rootName, fileSystem, path);
                }
            }
            return CreateDocument(rootName, fileSystem, path);
        }

        public static XDocument CreateDocument(XName rootName, IFileSystem fileSystem, string path)
        {
            XDocument document = new XDocument(new XElement(rootName));
            // Add it to the file system
            fileSystem.AddFile(path, document.Save);
            return document;
        }

        internal static XDocument GetDocument(IFileSystem fileSystem, string path)
        {
            using (Stream configStream = fileSystem.OpenFile(path))
            {
                return XmlUtility.LoadSafe(configStream, LoadOptions.PreserveWhitespace);
            }
        }

        internal static bool TryParseDocument(string content, out XDocument document)
        {
            document = null;
            try
            {
                document = XDocument.Parse(content);
                return true;
            }
            catch (XmlException)
            {
                return false;
            }
        }
    }
}
