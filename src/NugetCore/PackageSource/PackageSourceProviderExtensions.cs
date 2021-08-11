using System;
using System.Collections.Generic;
using System.Linq;

namespace NuGet
{
    public static class PackageSourceProviderExtensions
    {
        public static AggregateRepository CreateAggregateRepository(
            this IPackageSourceProvider provider, 
            IPackageRepositoryFactory factory, 
            bool ignoreFailingRepositories)
        {
            return new AggregateRepository(
                factory, 
                provider.GetEnabledPackageSources().Select(s => s.Source), 
                ignoreFailingRepositories);
        }

        public static IPackageRepository CreatePriorityPackageRepository(
            this IPackageSourceProvider provider, 
            IPackageRepositoryFactory factory,
            IPackageRepository primaryRepository)
        {
            var nonActivePackageSources = provider.GetEnabledPackageSources()
                                          .Where(s => !s.Source.Equals(primaryRepository.Source, StringComparison.OrdinalIgnoreCase))
                                          .ToArray();

            if (nonActivePackageSources.Length == 0)
            {
                return primaryRepository;
            }

            var fallbackRepository = AggregateRepository.Create(factory, sources: nonActivePackageSources, ignoreFailingRepositories: true);

            return new PriorityPackageRepository(primaryRepository, fallbackRepository);
        }

        /// <summary>
        /// Resolves a package source by either Name or Source.
        /// </summary>
        public static string ResolveSource(this IPackageSourceProvider provider, string value)
        {
            var resolvedSource = (from source in provider.GetEnabledPackageSources()
                                  where source.Name.Equals(value, StringComparison.CurrentCultureIgnoreCase) || source.Source.Equals(value, StringComparison.OrdinalIgnoreCase)
                                  select source.Source
                                  ).FirstOrDefault();

            return resolvedSource ?? value;
        }

        public static IEnumerable<PackageSource> GetEnabledPackageSources(this IPackageSourceProvider provider)
        {
            return provider.LoadPackageSources().Where(p => p.IsEnabled);
        }
    }
}