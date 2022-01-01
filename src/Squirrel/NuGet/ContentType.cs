using System;
using System.Linq;
using System.Xml;

namespace Squirrel.NuGet
{
    internal static class ContentType
    {
        public const string ContentTypeFileName = "[Content_Types].xml";

        public static void Clean(XmlDocument doc)
        {
            var typesElement = doc.FirstChild.NextSibling;
            if (typesElement.Name.ToLowerInvariant() != "types") {
                throw new Exception("Invalid ContentTypes file, expected root node should be 'Types'");
            }

            var children = typesElement.ChildNodes.OfType<XmlElement>();

            foreach (var child in children) {
                if (child.GetAttribute("Extension") == "") {
                    typesElement.RemoveChild(child);
                }
            }
        }

        public static void Merge(XmlDocument doc)
        {
            var elements = new[] {
                Tuple.Create("Default", "diff", "application/octet" ),
                Tuple.Create("Default", "bsdiff", "application/octet" ),
                Tuple.Create("Default", "exe", "application/octet" ),
                Tuple.Create("Default", "dll", "application/octet" ),
                Tuple.Create("Default", "ico", "application/octet" ),
                Tuple.Create("Default", "shasum", "text/plain" ),
            };

            var typesElement = doc.FirstChild.NextSibling;
            if (typesElement.Name.ToLowerInvariant() != "types") {
                throw new Exception("Invalid ContentTypes file, expected root node should be 'Types'");
            }

            var existingTypes = typesElement.ChildNodes.OfType<XmlElement>()
                .Select(k => Tuple.Create(k.Name,
                    k.GetAttribute("Extension").ToLowerInvariant(),
                    k.GetAttribute("ContentType").ToLowerInvariant()));

            var toAdd = elements
                .Where(x => existingTypes.All(t => t.Item2 != x.Item2.ToLowerInvariant()))
                .Select(element => {
                    var ret = doc.CreateElement(element.Item1, typesElement.NamespaceURI);

                    var ext = doc.CreateAttribute("Extension"); ext.Value = element.Item2;
                    var ct = doc.CreateAttribute("ContentType"); ct.Value = element.Item3;

                    ret.Attributes.Append(ext);
                    ret.Attributes.Append(ct);

                    return ret;
                });

            foreach (var v in toAdd) typesElement.AppendChild(v);
        }
    }
}
