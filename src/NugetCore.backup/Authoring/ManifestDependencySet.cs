using System.Collections.Generic;
using System.Xml;
using System.Xml.Serialization;

namespace NuGet
{
    public class ManifestDependencySet
    {
        [XmlAttribute("targetFramework")]
        public string TargetFramework { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification="This is required by XML serializer")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists", Justification = "This is required by XML serializer.")]
        [XmlElement("dependency")]
        public List<ManifestDependency> Dependencies { get; set; }
    }
}