using System;

namespace NuGet
{
    public class PackageIssue
    {
        public PackageIssue(string title, string description, string solution) :
            this(title, description, solution, PackageIssueLevel.Warning)
        {
        }

        public PackageIssue(string title, string description, string solution, PackageIssueLevel level)
        {
            if (String.IsNullOrEmpty(title))
            {
                throw new ArgumentException(CommonResources.Argument_Cannot_Be_Null_Or_Empty, "title");
            }

            if (String.IsNullOrEmpty(description))
            {
                throw new ArgumentException(CommonResources.Argument_Cannot_Be_Null_Or_Empty, "description");
            }

            Title = title;
            Description = description;
            Solution = solution;
            Level = level;
        }

        public PackageIssueLevel Level { get; private set; }
        public string Title { get; private set; }
        public string Description { get; private set; }
        public string Solution { get; private set; }

        public override string ToString()
        {
            return Title;
        }
    }
}