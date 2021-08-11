using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Xml.Serialization;
using NuGet.Resources;

namespace NuGet
{
    [XmlType("metadata")]
    public class ManifestMetadata : IPackageMetadata, IValidatableObject
    {
        private string _owners;
        private string _minClientVersionString;

        [XmlAttribute("minClientVersion")]
        [ManifestVersion(5)]
        public string MinClientVersionString
        {
            get { return _minClientVersionString; }
            set
            {
                Version version = null;
                if (!String.IsNullOrEmpty(value) && !System.Version.TryParse(value, out version))
                {
                    throw new InvalidDataException(NuGetResources.Manifest_InvalidMinClientVersion);
                }

                _minClientVersionString = value;
                MinClientVersion = version;
            }
        }

        [XmlIgnore]
        public Version MinClientVersion { get; private set; }

        [Required(ErrorMessageResourceType = typeof(NuGetResources), ErrorMessageResourceName = "Manifest_RequiredMetadataMissing")]
        [XmlElement("id")]
        public string Id { get; set; }

        [Required(ErrorMessageResourceType = typeof(NuGetResources), ErrorMessageResourceName = "Manifest_RequiredMetadataMissing")]
        [XmlElement("version")]
        public string Version { get; set; }

        [XmlElement("title")]
        public string Title { get; set; }

        [Required(ErrorMessageResourceType = typeof(NuGetResources), ErrorMessageResourceName = "Manifest_RequiredMetadataMissing")]
        [XmlElement("authors")]
        public string Authors { get; set; }

        [XmlElement("owners")]
        public string Owners
        {
            get
            {
                // Fallback to authors
                return _owners ?? Authors;
            }
            set
            {
                _owners = value;
            }
        }

        [SuppressMessage("Microsoft.Design", "CA1056:UriPropertiesShouldNotBeStrings", Justification = "Xml deserialziation can't handle uris")]
        [XmlElement("licenseUrl")]
        public string LicenseUrl { get; set; }

        [SuppressMessage("Microsoft.Design", "CA1056:UriPropertiesShouldNotBeStrings", Justification = "Xml deserialziation can't handle uris")]
        [XmlElement("projectUrl")]
        public string ProjectUrl { get; set; }

        [SuppressMessage("Microsoft.Design", "CA1056:UriPropertiesShouldNotBeStrings", Justification = "Xml deserialziation can't handle uris")]
        [XmlElement("iconUrl")]
        public string IconUrl { get; set; }

        [XmlElement("requireLicenseAcceptance")]
        public bool RequireLicenseAcceptance { get; set; }

        [XmlElement("developmentDependency")]
        [System.ComponentModel.DefaultValue(false)]
        public bool DevelopmentDependency { get; set; }

        [Required(ErrorMessageResourceType = typeof(NuGetResources), ErrorMessageResourceName = "Manifest_RequiredMetadataMissing")]
        [XmlElement("description")]
        public string Description { get; set; }

        [XmlElement("summary")]
        public string Summary { get; set; }

        [XmlElement("releaseNotes")]
        [ManifestVersion(2)]
        public string ReleaseNotes { get; set; }

        [XmlElement("copyright")]
        [ManifestVersion(2)]
        public string Copyright { get; set; }

        [XmlElement("language")]
        public string Language { get; set; }

        [XmlElement("tags")]
        public string Tags { get; set; }

        /// <summary>
        /// This property should be used only by the XML serializer. Do not use it in code.
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "value", Justification="The propert setter is not supported.")]
        [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists", Justification = "It's easier to create a list")]
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "This is needed for xml serialization")]
        [XmlArray("dependencies", IsNullable = false)]
        [XmlArrayItem("group", typeof(ManifestDependencySet))]
        [XmlArrayItem("dependency", typeof(ManifestDependency))]
        public List<object> DependencySetsSerialize
        {
            get
            {
                if (DependencySets == null || DependencySets.Count == 0)
                {
                    return null;
                }

                if (DependencySets.Any(set => set.TargetFramework != null))
                {
                    return DependencySets.Cast<object>().ToList();
                }
                else
                {
                    return DependencySets.SelectMany(set => set.Dependencies).Cast<object>().ToList();
                }
            }
            set
            {
                // this property is only used for serialization.
                throw new InvalidOperationException();
            }
        }

        [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists", Justification = "It's easier to create a list")]
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "This is needed for xml serialization")]
        [XmlIgnore]
        public List<ManifestDependencySet> DependencySets { get; set; }

        [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists", Justification = "It's easier to create a list")]
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "This is needed for xml serialization")]
        [XmlArray("frameworkAssemblies")]
        [XmlArrayItem("frameworkAssembly")]
        public List<ManifestFrameworkAssembly> FrameworkAssemblies { get; set; }

        /// <summary>
        /// This property should be used only by the XML serializer. Do not use it in code.
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "value", Justification = "The propert setter is not supported.")]
        [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists", Justification = "It's easier to create a list")]
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "This is needed for xml serialization")]
        [XmlArray("references", IsNullable = false)]
        [XmlArrayItem("group", typeof(ManifestReferenceSet))]
        [XmlArrayItem("reference", typeof(ManifestReference))]
        [ManifestVersion(2)]
        public List<object> ReferenceSetsSerialize
        {
            get
            {
                if (ReferenceSets == null || ReferenceSets.Count == 0)
                {
                    return null;
                }

                if (ReferenceSets.Any(set => set.TargetFramework != null))
                {
                    return ReferenceSets.Cast<object>().ToList();
                }
                else
                {
                    return ReferenceSets.SelectMany(set => set.References).Cast<object>().ToList();
                }
            }
            set
            {
                // this property is only used for serialization.
                throw new InvalidOperationException();
            }
        }

        [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists", Justification = "It's easier to create a list")]
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "This is needed for xml serialization")]
        [XmlIgnore]
        public List<ManifestReferenceSet> ReferenceSets { get; set; }

        SemanticVersion IPackageName.Version
        {
            get
            {
                if (Version == null)
                {
                    return null;
                }
                return new SemanticVersion(Version);
            }
        }

        Uri IPackageMetadata.IconUrl
        {
            get
            {
                if (IconUrl == null)
                {
                    return null;
                }
                return new Uri(IconUrl);
            }
        }

        Uri IPackageMetadata.LicenseUrl
        {
            get
            {
                if (LicenseUrl == null)
                {
                    return null;
                }
                return new Uri(LicenseUrl);
            }
        }

        Uri IPackageMetadata.ProjectUrl
        {
            get
            {
                if (ProjectUrl == null)
                {
                    return null;
                }
                return new Uri(ProjectUrl);
            }
        }

        IEnumerable<string> IPackageMetadata.Authors
        {
            get
            {
                if (String.IsNullOrEmpty(Authors))
                {
                    return Enumerable.Empty<string>();
                }
                return Authors.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            }
        }

        IEnumerable<string> IPackageMetadata.Owners
        {
            get
            {
                if (String.IsNullOrEmpty(Owners))
                {
                    return Enumerable.Empty<string>();
                }
                return Owners.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            }
        }

        IEnumerable<PackageDependencySet> IPackageMetadata.DependencySets
        {
            get
            {
                if (DependencySets == null)
                {
                    return Enumerable.Empty<PackageDependencySet>();
                }
                
                var dependencySets = DependencySets.Select(CreatePackageDependencySet);

                // group the dependency sets with the same target framework together.
                var dependencySetGroups = dependencySets.GroupBy(set => set.TargetFramework);
                var groupedDependencySets = dependencySetGroups.Select(group => new PackageDependencySet(group.Key, group.SelectMany(g => g.Dependencies)))
                                                               .ToList();
                // move the group with the null target framework (if any) to the front just for nicer display in UI
                int nullTargetFrameworkIndex = groupedDependencySets.FindIndex(set => set.TargetFramework == null);
                if (nullTargetFrameworkIndex > -1)
                {
                    var nullFxDependencySet = groupedDependencySets[nullTargetFrameworkIndex];
                    groupedDependencySets.RemoveAt(nullTargetFrameworkIndex);
                    groupedDependencySets.Insert(0, nullFxDependencySet);
                }

                return groupedDependencySets;
            }
        }

        ICollection<PackageReferenceSet> IPackageMetadata.PackageAssemblyReferences
        {
            get
            {
                if (ReferenceSets == null)
                {
                    return new PackageReferenceSet[0];
                }

                var referenceSets = ReferenceSets.Select(r => new PackageReferenceSet(r));

                var referenceSetGroups = referenceSets.GroupBy(set => set.TargetFramework);
                var groupedReferenceSets = referenceSetGroups.Select(group => new PackageReferenceSet(group.Key, group.SelectMany(g => g.References)))
                                                             .ToList();

                int nullTargetFrameworkIndex = groupedReferenceSets.FindIndex(set => set.TargetFramework == null);
                if (nullTargetFrameworkIndex > -1)
                {
                    var nullFxReferenceSet = groupedReferenceSets[nullTargetFrameworkIndex];
                    groupedReferenceSets.RemoveAt(nullTargetFrameworkIndex);
                    groupedReferenceSets.Insert(0, nullFxReferenceSet);
                }

                return groupedReferenceSets;
            }
        }

        IEnumerable<FrameworkAssemblyReference> IPackageMetadata.FrameworkAssemblies
        {
            get
            {
                if (FrameworkAssemblies == null)
                {
                    return Enumerable.Empty<FrameworkAssemblyReference>();
                }

                return from frameworkReference in FrameworkAssemblies
                       select new FrameworkAssemblyReference(frameworkReference.AssemblyName, ParseFrameworkNames(frameworkReference.TargetFramework));
            }
        }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (!String.IsNullOrEmpty(Id))
            {
                if (Id.Length > PackageIdValidator.MaxPackageIdLength)
                {
                    yield return new ValidationResult(String.Format(CultureInfo.CurrentCulture, NuGetResources.Manifest_IdMaxLengthExceeded));
                }
                else if(!PackageIdValidator.IsValidPackageId(Id))
                {
                    yield return new ValidationResult(String.Format(CultureInfo.CurrentCulture, NuGetResources.InvalidPackageId, Id));
                }
            }

            if (LicenseUrl == String.Empty)
            {
                yield return new ValidationResult(
                    String.Format(CultureInfo.CurrentCulture, NuGetResources.Manifest_UriCannotBeEmpty, "LicenseUrl"));
            }

            if (IconUrl == String.Empty)
            {
                yield return new ValidationResult(
                    String.Format(CultureInfo.CurrentCulture, NuGetResources.Manifest_UriCannotBeEmpty, "IconUrl"));
            }

            if (ProjectUrl == String.Empty)
            {
                yield return new ValidationResult(
                    String.Format(CultureInfo.CurrentCulture, NuGetResources.Manifest_UriCannotBeEmpty, "ProjectUrl"));
            }

            if (RequireLicenseAcceptance && String.IsNullOrWhiteSpace(LicenseUrl))
            {
                yield return new ValidationResult(NuGetResources.Manifest_RequireLicenseAcceptanceRequiresLicenseUrl);
            }
        }

        private static IEnumerable<FrameworkName> ParseFrameworkNames(string frameworkNames)
        {
            if (String.IsNullOrEmpty(frameworkNames))
            {
                return Enumerable.Empty<FrameworkName>();
            }

            return frameworkNames.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(VersionUtility.ParseFrameworkName);
        }

        private static PackageDependencySet CreatePackageDependencySet(ManifestDependencySet manifestDependencySet) 
        {
            FrameworkName targetFramework = manifestDependencySet.TargetFramework == null
                                            ? null 
                                            : VersionUtility.ParseFrameworkName(manifestDependencySet.TargetFramework);

            var dependencies = from d in manifestDependencySet.Dependencies
                               select new PackageDependency(
                                   d.Id,
                                   String.IsNullOrEmpty(d.Version) ? null : VersionUtility.ParseVersionSpec(d.Version));

            return new PackageDependencySet(targetFramework, dependencies);
        }
    }
}