using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace NuGet
{
    public static class XElementExtensions
    {
        public static string GetOptionalAttributeValue(this XElement element, string localName, string namespaceName = null)
        {
            XAttribute attr;
            if (String.IsNullOrEmpty(namespaceName))
            {
                attr = element.Attribute(localName);
            }
            else
            {
                attr = element.Attribute(XName.Get(localName, namespaceName));
            }
            return attr != null ? attr.Value : null;
        }

        public static string GetOptionalElementValue(this XContainer element, string localName, string namespaceName = null)
        {
            XElement child;
            if (String.IsNullOrEmpty(namespaceName))
            {
                child = element.ElementsNoNamespace(localName).FirstOrDefault();
            }
            else
            {
                child = element.Element(XName.Get(localName, namespaceName));
            }
            return child != null ? child.Value : null;
        }

        public static IEnumerable<XElement> ElementsNoNamespace(this XContainer container, string localName)
        {
            return container.Elements().Where(e => e.Name.LocalName == localName);
        }

        public static IEnumerable<XElement> ElementsNoNamespace(this IEnumerable<XContainer> source, string localName)
        {
            return source.Elements().Where(e => e.Name.LocalName == localName);
        }

        // REVIEW: We can use a stack if the perf is bad for Except and MergeWith
        public static XElement Except(this XElement source, XElement target)
        {
            if (target == null)
            {
                return source;
            }

            var attributesToRemove = from e in source.Attributes()
                                     where AttributeEquals(e, target.Attribute(e.Name))
                                     select e;
            // Remove the attributes
            foreach (var a in attributesToRemove.ToList())
            {
                a.Remove();
            }

            foreach (var sourceChildNode in source.Nodes().ToList())
            {
                var sourceChildComment = sourceChildNode as XComment;
                if (sourceChildComment != null)
                {
                    bool hasMatchingComment = HasComment(target, sourceChildComment);
                    if (hasMatchingComment)
                    {
                        sourceChildComment.Remove();
                    }
                    continue;
                }

                var sourceChild = sourceChildNode as XElement;
                if (sourceChild != null)
                {
                    var targetChild = FindElement(target, sourceChild);
                    if (targetChild != null && !HasConflict(sourceChild, targetChild))
                    {
                        Except(sourceChild, targetChild);
                        bool hasContent = sourceChild.HasAttributes || sourceChild.HasElements;
                        if (!hasContent)
                        {
                            // Remove the element if there is no content
                            sourceChild.Remove();
                            targetChild.Remove();
                        }
                    }
                }
            }
            return source;
        }

        public static XElement MergeWith(this XElement source, XElement target)
        {
            return MergeWith(source, target, null);
        }

        [SuppressMessage("Microsoft.Design", "CA1006:DoNotNestGenericTypesInMemberSignatures", Justification = "No reason to create a new type")]
        public static XElement MergeWith(this XElement source, XElement target, IDictionary<XName, Action<XElement, XElement>> nodeActions)
        {
            if (target == null)
            {
                return source;
            }

            // Merge the attributes
            foreach (var targetAttribute in target.Attributes())
            {
                var sourceAttribute = source.Attribute(targetAttribute.Name);
                if (sourceAttribute == null)
                {
                    source.Add(targetAttribute);
                }
            }

            var pendingComments = new Queue<XComment>();

            // Go through the elements to be merged
            foreach (var targetChildNode in target.Nodes())
            {
                var targetChildComment = targetChildNode as XComment;
                if (targetChildComment != null)
                {
                    // always add comment to source
                    pendingComments.Enqueue(targetChildComment);
                    continue;
                }

                var targetChild = targetChildNode as XElement;
                if (targetChild != null)
                {
                    var sourceChild = FindElement(source, targetChild);
                    if (sourceChild != null)
                    {
                        // when we see an element, add all the previous comments before the child element
                        AddContents(pendingComments, sourceChild.AddBeforeSelf);
                    }

                    if (sourceChild != null && !HasConflict(sourceChild, targetChild))
                    {
                        // Other wise merge recursively
                        sourceChild.MergeWith(targetChild, nodeActions);
                    }
                    else
                    {
                        Action<XElement, XElement> nodeAction;
                        if (nodeActions != null && nodeActions.TryGetValue(targetChild.Name, out nodeAction))
                        {
                            nodeAction(source, targetChild);
                        }
                        else
                        {
                            // If that element is null then add that node
                            source.Add(targetChild);

                            var newlyAddedElement = source.Elements().Last();
                            Debug.Assert(newlyAddedElement.Name == targetChild.Name);

                            // when we see an element, add all the previous comments before the child element
                            AddContents(pendingComments, newlyAddedElement.AddBeforeSelf);
                        }
                    }
                }
            }

            // now add all remaining comments at the end
            AddContents(pendingComments, source.Add);
            return source;
        }

        private static XElement FindElement(XElement source, XElement targetChild)
        {
            // Get all of the elements in the source that match this name
            var sourceElements = source.Elements(targetChild.Name).ToList();

            // Try to find the best matching element based on attribute names and values
            sourceElements.Sort((a, b) => Compare(targetChild, a, b));

            return sourceElements.FirstOrDefault();
        }

        private static bool HasComment(XElement element, XComment comment)
        {
            return element.Nodes().Any(node => node.NodeType == XmlNodeType.Comment &&
                                               ((XComment)node).Value.Equals(comment.Value, StringComparison.Ordinal));                                                
        }

        private static int Compare(XElement target, XElement left, XElement right)
        {
            Debug.Assert(left.Name == right.Name);

            // First check how much attribute names and values match
            int leftExactMathes = CountMatches(left, target, AttributeEquals);
            int rightExactMathes = CountMatches(right, target, AttributeEquals);

            if (leftExactMathes == rightExactMathes)
            {
                // Then check which names match
                int leftNameMatches = CountMatches(left, target, (a, b) => a.Name == b.Name);
                int rightNameMatches = CountMatches(right, target, (a, b) => a.Name == b.Name);

                return rightNameMatches.CompareTo(leftNameMatches);
            }

            return rightExactMathes.CompareTo(leftExactMathes);
        }

        private static int CountMatches(XElement left, XElement right, Func<XAttribute, XAttribute, bool> matcher)
        {
            return (from la in left.Attributes()
                    from ta in right.Attributes()
                    where matcher(la, ta)
                    select la).Count();
        }

        private static bool HasConflict(XElement source, XElement target)
        {
            // Get all attributes as name value pairs
            var sourceAttr = source.Attributes().ToDictionary(a => a.Name, a => a.Value);
            // Loop over all the other attributes and see if there are
            foreach (var targetAttr in target.Attributes())
            {
                string sourceValue;
                // if any of the attributes are in the source (names match) but the value doesn't match then we've found a conflict
                if (sourceAttr.TryGetValue(targetAttr.Name, out sourceValue) && sourceValue != targetAttr.Value)
                {
                    return true;
                }
            }
            return false;
        }

        public static void RemoveAttributes(this XElement element, Func<XAttribute, bool> condition)
        {
            element.Attributes()
                   .Where(condition)
                   .ToList()
                   .Remove();

            element.Descendants()
                   .ToList()
                   .ForEach(e => RemoveAttributes(e, condition));
        }

        public static void AddIndented(this XContainer container, XContainer content)
        {
            string oneIndentLevel = container.ComputeOneLevelOfIndentation();

            XText leadingText = container.PreviousNode as XText;
            string parentIndent = leadingText != null ? leadingText.Value : Environment.NewLine;

            content.IndentChildrenElements(parentIndent + oneIndentLevel, oneIndentLevel);

            AddLeadingIndentation(container, parentIndent, oneIndentLevel);
            container.Add(content);
            AddTrailingIndentation(container, parentIndent);
        }

        private static void AddTrailingIndentation(XContainer container, string containerIndent)
        {
            container.Add(new XText(containerIndent));
        }

        private static void AddLeadingIndentation(XContainer container, string containerIndent, string oneIndentLevel)
        {
            bool containerIsSelfClosed = !container.Nodes().Any();
            XText lastChildText = container.LastNode as XText;
            if (containerIsSelfClosed || lastChildText == null)
            {
                container.Add(new XText(containerIndent + oneIndentLevel));
            }
            else
            {
                lastChildText.Value += oneIndentLevel;
            }
        }

        private static void IndentChildrenElements(this XContainer container, string containerIndent, string oneIndentLevel)
        {
            string childIndent = containerIndent + oneIndentLevel;
            foreach (XElement element in container.Elements())
            {
                element.AddBeforeSelf(new XText(childIndent));
                element.IndentChildrenElements(childIndent + oneIndentLevel, oneIndentLevel);
            }

            if (container.Elements().Any())
                container.Add(new XText(containerIndent));
        }

        public static void RemoveIndented(this XNode element)
        {
            // NOTE: this method is tested by BindinRedirectManagerTest and SettingsTest
            XText textBeforeOrNull = element.PreviousNode as XText;
            XText textAfterOrNull = element.NextNode as XText;
            string oneIndentLevel = element.ComputeOneLevelOfIndentation();
            bool isLastChild = !element.ElementsAfterSelf().Any();

            element.Remove();

            if (textAfterOrNull != null && textAfterOrNull.IsWhiteSpace())
                textAfterOrNull.Remove();

            if (isLastChild && textBeforeOrNull != null && textBeforeOrNull.IsWhiteSpace())
                textBeforeOrNull.Value = textBeforeOrNull.Value.Substring(0, textBeforeOrNull.Value.Length - oneIndentLevel.Length);
        }

        private static bool IsWhiteSpace(this XText textNode)
        {
            return string.IsNullOrWhiteSpace(textNode.Value);
        }

        private static string ComputeOneLevelOfIndentation(this XNode node)
        {
            var depth = node.Ancestors().Count();
            XText textBeforeOrNull = node.PreviousNode as XText;
            if (depth == 0 || textBeforeOrNull == null || !textBeforeOrNull.IsWhiteSpace())
                return "  ";

            string indentString = textBeforeOrNull.Value.Trim(Environment.NewLine.ToCharArray());
            char lastChar = indentString.LastOrDefault();
            char indentChar = (lastChar == '\t' ? '\t' : ' ');
            int indentLevel = Math.Max(1, indentString.Length/depth);
            return new string(indentChar, indentLevel);
        }

        private static bool AttributeEquals(XAttribute source, XAttribute target)
        {
            if (source == null && target == null)
            {
                return true;
            }

            if (source == null || target == null)
            {
                return false;
            }
            return source.Name == target.Name && source.Value == target.Value;
        }

        private static void AddContents<T>(Queue<T> pendingComments, Action<T> action)
        {
            while (pendingComments.Count > 0)
            {
                action(pendingComments.Dequeue());
            }
        }
    }
}