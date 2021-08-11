using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using NuGet.Resources;

namespace NuGet
{
    internal static class ManifestReader
    {
        private static readonly string[] RequiredElements = new string[] { "id", "version", "authors", "description" };

        public static Manifest ReadManifest(XDocument document)
        {
            var metadataElement = document.Root.ElementsNoNamespace("metadata").FirstOrDefault();
            if (metadataElement == null)
            {
                throw new InvalidDataException(
                    String.Format(CultureInfo.CurrentCulture, NuGetResources.Manifest_RequiredElementMissing, "metadata"));
            }

            return new Manifest
            {
                Metadata = ReadMetadata(metadataElement),
                Files = ReadFilesList(document.Root.ElementsNoNamespace("files").FirstOrDefault())
            };
        }

        private static ManifestMetadata ReadMetadata(XElement xElement)
        {
            var manifestMetadata = new ManifestMetadata();
            manifestMetadata.DependencySets = new List<ManifestDependencySet>();
            manifestMetadata.ReferenceSets = new List<ManifestReferenceSet>();
            manifestMetadata.MinClientVersionString = xElement.GetOptionalAttributeValue("minClientVersion");

            // we store all child elements under <metadata> so that we can easily check for required elements.
            var allElements = new HashSet<string>();

            XNode node = xElement.FirstNode;
            while (node != null)
            {
                var element = node as XElement;
                if (element != null)
                {
                    ReadMetadataValue(manifestMetadata, element, allElements);
                }
                node = node.NextNode;
            }

            // now check for required elements, which include <id>, <version>, <authors> and <description>
            foreach (var requiredElement in RequiredElements)
            {
                if (!allElements.Contains(requiredElement))
                {
                    throw new InvalidDataException(
                        String.Format(CultureInfo.CurrentCulture, NuGetResources.Manifest_RequiredElementMissing, requiredElement));
                }
            }

            return manifestMetadata;
        }

        [SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]    
        private static void ReadMetadataValue(ManifestMetadata manifestMetadata, XElement element, HashSet<string> allElements)
        {
            if (element.Value == null)
            {
                return;
            }

            allElements.Add(element.Name.LocalName);

            string value = element.Value.SafeTrim();
            switch (element.Name.LocalName)
            {
                case "id":
                    manifestMetadata.Id = value;
                    break;
                case "version":
                    manifestMetadata.Version = value;
                    break;
                case "authors":
                    manifestMetadata.Authors = value;
                    break;
                case "owners":
                    manifestMetadata.Owners = value;
                    break;
                case "licenseUrl":
                    manifestMetadata.LicenseUrl = value;
                    break;
                case "projectUrl":
                    manifestMetadata.ProjectUrl = value;
                    break;
                case "iconUrl":
                    manifestMetadata.IconUrl = value;
                    break;
                case "requireLicenseAcceptance":
                    manifestMetadata.RequireLicenseAcceptance = XmlConvert.ToBoolean(value);
                    break;
                case "developmentDependency":
                    manifestMetadata.DevelopmentDependency = XmlConvert.ToBoolean(value);
                    break;
                case "description":
                    manifestMetadata.Description = value;
                    break;
                case "summary":
                    manifestMetadata.Summary = value;
                    break;
                case "releaseNotes":
                    manifestMetadata.ReleaseNotes = value;
                    break;
                case "copyright":
                    manifestMetadata.Copyright = value;
                    break;
                case "language":
                    manifestMetadata.Language = value;
                    break;
                case "title":
                    manifestMetadata.Title = value;
                    break;
                case "tags":
                    manifestMetadata.Tags = value;
                    break;
                case "dependencies":
                    manifestMetadata.DependencySets = ReadDependencySets(element);
                    break;
                case "frameworkAssemblies":
                    manifestMetadata.FrameworkAssemblies = ReadFrameworkAssemblies(element);
                    break;
                case "references":
                    manifestMetadata.ReferenceSets = ReadReferenceSets(element);
                    break;
            }
        }

        private static List<ManifestReferenceSet> ReadReferenceSets(XElement referencesElement)
        {
            if (!referencesElement.HasElements)
            {
                return new List<ManifestReferenceSet>(0);
            }

            if (referencesElement.ElementsNoNamespace("group").Any() &&
                referencesElement.ElementsNoNamespace("reference").Any())
            {
                throw new InvalidDataException(NuGetResources.Manifest_ReferencesHasMixedElements);
            }

            var references = ReadReference(referencesElement, throwIfEmpty: false);
            if (references.Count > 0)
            {
                // old format, <reference> is direct child of <references>
                var referenceSet = new ManifestReferenceSet
                {
                    References = references
                };
                return new List<ManifestReferenceSet> { referenceSet };
            }
            else
            {
                var groups = referencesElement.ElementsNoNamespace("group");
                return (from element in groups
                        select new ManifestReferenceSet
                        {
                            TargetFramework = element.GetOptionalAttributeValue("targetFramework").SafeTrim(),
                            References = ReadReference(element, throwIfEmpty: true)
                        }).ToList();
            }
        }

        public static List<ManifestReference> ReadReference(XElement referenceElement, bool throwIfEmpty)
        {
            var references = (from element in referenceElement.ElementsNoNamespace("reference")
                              let fileAttribute = element.Attribute("file")
                              where fileAttribute != null && !String.IsNullOrEmpty(fileAttribute.Value)
                              select new ManifestReference { File = fileAttribute.Value.SafeTrim() }
                             ).ToList();

            if (throwIfEmpty && references.Count == 0)
            {
                throw new InvalidDataException(NuGetResources.Manifest_ReferencesIsEmpty);
            }

            return references;
        }

        private static List<ManifestFrameworkAssembly> ReadFrameworkAssemblies(XElement frameworkElement)
        {
            if (!frameworkElement.HasElements)
            {
                return new List<ManifestFrameworkAssembly>(0);
            }

            return (from element in frameworkElement.ElementsNoNamespace("frameworkAssembly")
                    let assemblyNameAttribute = element.Attribute("assemblyName")
                    where assemblyNameAttribute != null && !String.IsNullOrEmpty(assemblyNameAttribute.Value)
                    select new ManifestFrameworkAssembly
                    {
                        AssemblyName = assemblyNameAttribute.Value.SafeTrim(),
                        TargetFramework = element.GetOptionalAttributeValue("targetFramework").SafeTrim()
                    }).ToList();
        }

        private static List<ManifestDependencySet> ReadDependencySets(XElement dependenciesElement)
        {
            if (!dependenciesElement.HasElements)
            {
                return new List<ManifestDependencySet>();
            }

            // Disallow the <dependencies> element to contain both <dependency> and 
            // <group> child elements. Unfortunately, this cannot be enforced by XSD.
            if (dependenciesElement.ElementsNoNamespace("dependency").Any() &&
                dependenciesElement.ElementsNoNamespace("group").Any())
            {
                throw new InvalidDataException(NuGetResources.Manifest_DependenciesHasMixedElements);
            }

            var dependencies = ReadDependencies(dependenciesElement);
            if (dependencies.Count > 0)
            {
                // old format, <dependency> is direct child of <dependencies>
                var dependencySet = new ManifestDependencySet
                {
                    Dependencies = dependencies
                };
                return new List<ManifestDependencySet> { dependencySet };
            }
            else
            {
                var groups = dependenciesElement.ElementsNoNamespace("group");
                return (from element in groups
                        select new ManifestDependencySet
                        {
                            TargetFramework = element.GetOptionalAttributeValue("targetFramework").SafeTrim(),
                            Dependencies = ReadDependencies(element)
                        }).ToList();
            }
        }

        private static List<ManifestDependency> ReadDependencies(XElement containerElement)
        {


            // element is <dependency>
            return (from element in containerElement.ElementsNoNamespace("dependency")
                    let idElement = element.Attribute("id")
                    where idElement != null && !String.IsNullOrEmpty(idElement.Value)
                    select new ManifestDependency
                    {
                        Id = idElement.Value.SafeTrim(),
                        Version = element.GetOptionalAttributeValue("version").SafeTrim()
                    }).ToList();
        }

        private static List<ManifestFile> ReadFilesList(XElement xElement)
        {
            if (xElement == null)
            {
                return null;
            }

            List<ManifestFile> files = new List<ManifestFile>();
            foreach (var file in xElement.ElementsNoNamespace("file"))
            {
                var srcElement = file.Attribute("src");
                if (srcElement == null || String.IsNullOrEmpty(srcElement.Value))
                {
                    continue;
                }

                string target = file.GetOptionalAttributeValue("target").SafeTrim();
                string exclude = file.GetOptionalAttributeValue("exclude").SafeTrim();

                // Multiple sources can be specified by using semi-colon separated values. 
                files.AddRange(from source in srcElement.Value.Trim(';').Split(';')
                               select new ManifestFile { Source = source.SafeTrim(), Target = target.SafeTrim(), Exclude = exclude.SafeTrim() });
            }
            return files;
        }
    }
}