using System;
using System.Collections.Generic;

namespace NuGet
{
    [Serializable]
    public class AssemblyMetadata
    {
        public AssemblyMetadata(Dictionary<string, string> properties = null)
        {
            this.Properties = properties ??
                // Just like parameter replacements, these are also case insensitive, for consistency.
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string Name { get; set; }
        public SemanticVersion Version { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Company { get; set; }
        public string Copyright { get; set; }

        /// <summary>
        /// Supports extra metadata properties specified for an assembly 
        /// using AssemblyMetadataAttribute.
        /// </summary>
        public Dictionary<string, string> Properties { get; private set; }
    }
}
