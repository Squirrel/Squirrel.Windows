using System;
using System.Diagnostics;
using System.Xml.Linq;

namespace NuGet.Runtime
{
    public class AssemblyBinding : IEquatable<AssemblyBinding>
    {
        private const string Namespace = "urn:schemas-microsoft-com:asm.v1";
        private string _oldVersion;
        private string _culture;

        internal AssemblyBinding()
        {
        }

        public AssemblyBinding(IAssembly assembly)
        {
            Name = assembly.Name;
            PublicKeyToken = assembly.PublicKeyToken;
            NewVersion = assembly.Version.ToString();
            AssemblyNewVersion = assembly.Version;
            Culture = assembly.Culture;
        }

        public string Name
        {
            get;
            private set;
        }

        public string Culture
        {
            get
            {
                return _culture ?? "neutral";
            }
            set
            {
                _culture = value;
            }
        }

        public string PublicKeyToken
        {
            get;
            private set;
        }

        public string ProcessorArchitecture
        {
            get;
            private set;
        }

        public string NewVersion
        {
            get;
            private set;
        }

        public string OldVersion
        {
            get
            {
                // If not old version as specified, we make all versions of this assembly
                // point to the new version
                return _oldVersion ?? "0.0.0.0-" + NewVersion;
            }
            set
            {
                _oldVersion = value;
            }
        }

        public Version AssemblyNewVersion
        {
            get;
            private set;
        }

        // These properties aren't meant for use, just used for round tripping existing 
        // <dependentAssembly /> elements
        public string CodeBaseHref
        {
            get;
            private set;
        }

        public string CodeBaseVersion
        {
            get;
            private set;
        }

        public string PublisherPolicy
        {
            get;
            private set;
        }

        public XElement ToXElement()
        {
            // We're going to generate the fragment below.
            //<dependentAssembly> 
            //   <assemblyIdentity name="{Name}" 
            //                     publicKeyToken="{PublicKeyToken}" 
            //                     culture="{Culture}" 
            //                     processorArchitecture="{ProcessorArchitecture}" />
            //
            //   <bindingRedirect oldVersion="{OldVersion}" 
            //                    newVersion="{NewVersion}"/>
            //
            //   <publisherPolicy apply="{PublisherPolicy}" />
            //
            //   <codeBase href="{CodeBaseHref}" version="{CodeBaseVersion}" />
            //</dependentAssembly>
            XElement dependenyAssembly = new XElement(GetQualifiedName("dependentAssembly"),
                             new XElement(GetQualifiedName("assemblyIdentity"),
                                 new XAttribute("name", Name),
                                 new XAttribute("publicKeyToken", PublicKeyToken),
                                 new XAttribute("culture", Culture),
                                 new XAttribute("processorArchitecture", ProcessorArchitecture ?? String.Empty)),
                             new XElement(GetQualifiedName("bindingRedirect"),
                                 new XAttribute("oldVersion", OldVersion),
                                 new XAttribute("newVersion", NewVersion)));

            if (!String.IsNullOrEmpty(PublisherPolicy))
            {
                dependenyAssembly.Add(new XElement(GetQualifiedName("publisherPolicy"),
                                        new XAttribute("apply", PublisherPolicy)));
            }

            if (!String.IsNullOrEmpty(CodeBaseHref))
            {
                Debug.Assert(!String.IsNullOrEmpty(CodeBaseVersion));
                dependenyAssembly.Add(new XElement(GetQualifiedName("codeBase"),
                                          new XAttribute("href", CodeBaseHref),
                                          new XAttribute("version", CodeBaseVersion)));
            }


            // Remove empty attributes
            dependenyAssembly.RemoveAttributes(a => String.IsNullOrEmpty(a.Value));


            return dependenyAssembly;
        }

        public override string ToString()
        {
            return ToXElement().ToString();
        }

        public static AssemblyBinding Parse(XContainer dependentAssembly)
        {
            if (dependentAssembly == null)
            {
                throw new ArgumentNullException("dependentAssembly");
            }
            // This code parses a <dependentAssembly /> element of an <assemblyBinding /> section in config

            // Create a new assembly binding a fill up
            AssemblyBinding binding = new AssemblyBinding();

            // Parses this schema http://msdn.microsoft.com/en-us/library/0ash1ksb.aspx
            XElement assemblyIdentity = dependentAssembly.Element(GetQualifiedName("assemblyIdentity"));
            if (assemblyIdentity != null)
            {
                // <assemblyIdentity /> http://msdn.microsoft.com/en-us/library/b0yt6ck0.aspx
                binding.Name = assemblyIdentity.Attribute("name").Value;
                binding.Culture = assemblyIdentity.GetOptionalAttributeValue("culture");
                binding.PublicKeyToken = assemblyIdentity.GetOptionalAttributeValue("publicKeyToken");
                binding.ProcessorArchitecture = assemblyIdentity.GetOptionalAttributeValue("processorArchitecture");
            }

            XElement bindingRedirect = dependentAssembly.Element(GetQualifiedName("bindingRedirect"));
            if (bindingRedirect != null)
            {
                // <bindingRedirect /> http://msdn.microsoft.com/en-us/library/eftw1fys.aspx
                binding.OldVersion = bindingRedirect.Attribute("oldVersion").Value;
                binding.NewVersion = bindingRedirect.Attribute("newVersion").Value;
            }

            XElement codeBase = dependentAssembly.Element(GetQualifiedName("codeBase"));
            if (codeBase != null)
            {
                // <codeBase /> http://msdn.microsoft.com/en-us/library/efs781xb.aspx
                binding.CodeBaseHref = codeBase.Attribute("href").Value;
                binding.CodeBaseVersion = codeBase.Attribute("version").Value;
            }

            XElement publisherPolicy = dependentAssembly.Element(GetQualifiedName("publisherPolicy"));
            if (publisherPolicy != null)
            {
                // <publisherPolicy /> http://msdn.microsoft.com/en-us/library/cf9025zt.aspx
                binding.PublisherPolicy = publisherPolicy.Attribute("apply").Value;
            }

            return binding;
        }

        public static XName GetQualifiedName(string name)
        {
            return XName.Get(name, Namespace);
        }

        public bool Equals(AssemblyBinding other)
        {
            return SafeEquals(Name, other.Name) &&
                   SafeEquals(PublicKeyToken, other.PublicKeyToken) &&
                   SafeEquals(Culture, other.Culture) &&
                   SafeEquals(ProcessorArchitecture, other.ProcessorArchitecture);
        }

        private static bool SafeEquals(object a, object b)
        {
            if (a != null && b != null)
            {
                return a.Equals(b);
            }

            if (a == null && b == null)
            {
                return true;
            }

            return false;
        }

        public override bool Equals(object obj)
        {
            var other = obj as AssemblyBinding;
            if (other != null)
            {
                return Equals(other);
            }
            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            var combiner = new HashCodeCombiner();

            // assemblyIdentity
            combiner.AddObject(Name);
            combiner.AddObject(PublicKeyToken);
            combiner.AddObject(Culture);
            combiner.AddObject(ProcessorArchitecture);

            return combiner.CombinedHash;
        }
    }
}
