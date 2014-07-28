using System;
using System.Linq;
using System.Xml;

namespace Squirrel.Core
{
    internal static class ContentType
    {
        public static void Merge(XmlDocument doc)
        {
            var elements = new [] {
                Tuple.Create("Default", "diff", "application/octet" ),
                Tuple.Create("Default", "exe", "application/octet" ),
                Tuple.Create("Default", "dll", "application/octet" ),
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

            elements
                .Where(x => existingTypes.All(t => t.Item2 != x.Item2.ToLowerInvariant()))
                .Select(element => {
                    var ret = doc.CreateElement(element.Item1, typesElement.NamespaceURI);
                    var ext = doc.CreateAttribute("Extension"); ext.Value = element.Item2;
                    var ct = doc.CreateAttribute("ContentType"); ct.Value = element.Item3;
                    new[] { ext, ct }.ForEach(x => ret.Attributes.Append(x));

                    return ret;
                }).ForEach(x => typesElement.AppendChild(x));
        }
    }
}
