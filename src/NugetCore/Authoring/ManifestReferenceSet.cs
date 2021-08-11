using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Xml.Serialization;

namespace NuGet
{
    public class ManifestReferenceSet : IValidatableObject
    {
        [XmlAttribute("targetFramework")]
        public string TargetFramework { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "This is required by XML serializer")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists", Justification = "This is required by XML serializer.")]
        [XmlElement("reference")]
        public List<ManifestReference> References { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (References != null)
            {
                return References.SelectMany(r => r.Validate(validationContext));
            }

            return Enumerable.Empty<ValidationResult>();
        }
    }
}