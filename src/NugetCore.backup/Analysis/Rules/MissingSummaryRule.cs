using System;
using System.Collections.Generic;
using NuGet.Resources;

namespace NuGet.Analysis.Rules
{

    internal class MissingSummaryRule : IPackageRule
    {
        const int ThresholdDescriptionLength = 300;

        public IEnumerable<PackageIssue> Validate(IPackage package)
        {
            if (package.Description.Length > ThresholdDescriptionLength && String.IsNullOrEmpty(package.Summary))
            {
                yield return new PackageIssue(
                    AnalysisResources.MissingSummaryTitle,
                    AnalysisResources.MissingSummaryDescription,
                    AnalysisResources.MissingSummarySolution);
            }
        }
    }
}