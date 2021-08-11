using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Schema;
using NuGet.Resources;

namespace NuGet
{
    internal static class ManifestSchemaUtility
    {
        /// <summary>
        /// Baseline schema 
        /// </summary>
        internal const string SchemaVersionV1 = "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd";

        /// <summary>
        /// Added copyrights, references and release notes
        /// </summary>
        internal const string SchemaVersionV2 = "http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd";

        /// <summary>
        /// Used if the version is a semantic version.
        /// </summary>
        internal const string SchemaVersionV3 = "http://schemas.microsoft.com/packaging/2011/10/nuspec.xsd";

        /// <summary>
        /// Added 'targetFramework' attribute for 'dependency' elements.
        /// Allow framework folders under 'content' and 'tools' folders. 
        /// </summary>
        internal const string SchemaVersionV4 = "http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd";

        /// <summary>
        /// Added 'targetFramework' attribute for 'references' elements.
        /// Added 'minClientVersion' attribute
        /// </summary>
        internal const string SchemaVersionV5 = "http://schemas.microsoft.com/packaging/2013/01/nuspec.xsd";

        /// <summary>
        /// Allows XDT transformation
        /// </summary>
        internal const string SchemaVersionV6 = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";

        private static readonly string[] VersionToSchemaMappings = new[] {
            SchemaVersionV1,
            SchemaVersionV2,
            SchemaVersionV3,
            SchemaVersionV4,
            SchemaVersionV5,
            SchemaVersionV6
        };

        private static ConcurrentDictionary<string, XmlSchemaSet> _manifestSchemaSetCache = new ConcurrentDictionary<string, XmlSchemaSet>(StringComparer.OrdinalIgnoreCase);
        
        public static int GetVersionFromNamespace(string @namespace)
        {
            int index = Math.Max(0, Array.IndexOf(VersionToSchemaMappings, @namespace));

            // we count version from 1 instead of 0
            return index + 1;
        }

        public static string GetSchemaNamespace(int version)
        {
            // Versions are internally 0-indexed but stored with a 1 index so decrement it by 1
            if (version <= 0 || version > VersionToSchemaMappings.Length)
            {
                throw new InvalidOperationException(String.Format(CultureInfo.CurrentCulture, NuGetResources.UnknownSchemaVersion, version));
            }
            return VersionToSchemaMappings[version - 1];
        }

        public static XmlSchemaSet GetManifestSchemaSet(string schemaNamespace)
        {
            return _manifestSchemaSetCache.GetOrAdd(schemaNamespace, schema =>
                {
                    const string schemaResourceName = "NuGet.Authoring.nuspec.xsd";

                    string formattedContent;

                    // Update the xsd with the right schema namespace
                    var assembly = typeof(Manifest).Assembly;
                    using (var reader = new StreamReader(assembly.GetManifestResourceStream(schemaResourceName)))
                    {
                        string content = reader.ReadToEnd();
                        formattedContent = String.Format(CultureInfo.InvariantCulture, content, schema);
                    }

                    using (var reader = new StringReader(formattedContent))
                    {
                        var schemaSet = new XmlSchemaSet();

                        var settings = new XmlReaderSettings 
                        {
                            DtdProcessing = DtdProcessing.Prohibit,
                            XmlResolver = null
                        };

                        schemaSet.Add(schema, XmlReader.Create(reader, settings));
                        return schemaSet;
                    }
                });
        }

        public static bool IsKnownSchema(string schemaNamespace)
        {
            return VersionToSchemaMappings.Contains(schemaNamespace, StringComparer.OrdinalIgnoreCase);
        }
    }
}
