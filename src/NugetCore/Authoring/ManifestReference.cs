using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Xml.Serialization;
using NuGet.Resources;

namespace NuGet
{
    [XmlType("reference")]
    public class ManifestReference : IValidatableObject, IEquatable<ManifestReference>
    {
        private static readonly char[] _referenceFileInvalidCharacters = Path.GetInvalidFileNameChars();

        [Required(ErrorMessageResourceType = typeof(NuGetResources), ErrorMessageResourceName = "Manifest_RequiredMetadataMissing")]
        [XmlAttribute("file")]
        public string File { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (String.IsNullOrEmpty(File))
            {
                yield return new ValidationResult(NuGetResources.Manifest_RequiredElementMissing, new[] { "File" });
            }
            else if (File.IndexOfAny(_referenceFileInvalidCharacters) != -1)
            {
                yield return new ValidationResult(String.Format(CultureInfo.CurrentCulture, NuGetResources.Manifest_InvalidReferenceFile, File));
            }
        }

        public bool Equals(ManifestReference other)
        {
            return other != null && String.Equals(File, other.File, StringComparison.OrdinalIgnoreCase);
        }

        public override int GetHashCode()
        {
            return File == null ? 0 : File.GetHashCode();
        }
    }
}
