using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

namespace NuGet
{
    internal static class NetPortableProfileTableSerializer
    {
        // It's like JSON.Net but without the dependency! Yeah... right. :)
        private static readonly DataContractJsonSerializer _serializer = new DataContractJsonSerializer(typeof(IEnumerable<PortableProfile>));

        internal static void Serialize(NetPortableProfileTable portableProfileTable, Stream output)
        {
            // We need to convert the profile entries to surrogates to make them easily and simply serializable
            var surrogates = portableProfileTable.Profiles.Select(p => new PortableProfile()
            {
                Name = p.Name,
                FrameworkVersion = p.FrameworkVersion,
                SupportedFrameworks = p.SupportedFrameworks.Select(f => f.FullName).ToArray(),
                OptionalFrameworks = p.OptionalFrameworks.Select(f => f.FullName).ToArray()
            });
            _serializer.WriteObject(output, surrogates);
        }

        internal static NetPortableProfileTable Deserialize(Stream input)
        {
            var surrogates = (IEnumerable<PortableProfile>)_serializer.ReadObject(input);
            return new NetPortableProfileTable(surrogates.Select(p => new NetPortableProfile(
                p.FrameworkVersion,
                p.Name, 
                p.SupportedFrameworks.Select(f => new FrameworkName(f)), 
                p.OptionalFrameworks.Select(f => new FrameworkName(f)))));
        }

        // Surrogate class for serialization/deserialization
        [DataContract]
        private class PortableProfile
        {
            [DataMember]
            public string Name { get; set; }
            [DataMember]
            public string FrameworkVersion { get; set; }
            [DataMember]
            public string[] SupportedFrameworks { get; set; }
            [DataMember]
            public string[] OptionalFrameworks { get; set; }
        }
    }
}
