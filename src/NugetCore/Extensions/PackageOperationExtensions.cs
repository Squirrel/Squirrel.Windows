using System;
using System.Collections.Generic;
using System.Linq;

namespace NuGet
{
    public static class PackageOperationExtensions
    {
        /// <summary>
        /// Calculates the canonical list of operations.
        /// </summary>
        public static IList<PackageOperation> Reduce(this IEnumerable<PackageOperation> operations)
        {
            // Convert the list of operations to a dictionary from (Action, Id, Version) -> [Operations]
            // We keep track of the index so that we preserve the ordering of the operations
            var operationLookup = operations.Select((o, index) => new IndexedPackageOperation(index, o))
                                            .ToLookup(o => GetOperationKey(o.Operation))
                                            .ToDictionary(g => g.Key,
                                                          g => g.ToList());

            // Given a list of operations we're going to eliminate the ones that have opposites (i.e. 
            // if the list contains +A 1.0 and -A 1.0, then we eliminate them both entries).
            foreach (var operation in operations)
            {
                // We get the opposing operation for the current operation:
                // if o is +A 1.0 then the opposing key is - A 1.0
                var opposingKey = GetOpposingOperationKey(operation);

                // We can't use TryGetValue since the value of the dictionary entry
                // is a List of an anonymous type.
                if (operationLookup.ContainsKey(opposingKey))
                {
                    // If we find an opposing entry, we remove it from the list of candidates
                    var opposingOperations = operationLookup[opposingKey];
                    opposingOperations.RemoveAt(0);

                    // Remove the list from the dictionary if nothing is in it
                    if (!opposingOperations.Any())
                    {
                        operationLookup.Remove(opposingKey);
                    }
                }
            }

            // Create the final list of operations and order them by their original index
            return operationLookup.SelectMany(o => o.Value)
                                  .ToList()
                                  .Reorder();
        }

        /// <summary>
        /// Reorders package operations so that operations occur in the same order as index with additional 
        /// handling of satellite packages so that are always processed relative to the corresponding core package.
        /// </summary>
        private static IList<PackageOperation> Reorder(this List<IndexedPackageOperation> operations)
        {
            operations.Sort((a, b) => a.Index - b.Index);

            var satellitePackageOperations = new List<IndexedPackageOperation>();
            for (int i = 0; i < operations.Count; i++)
            {
                var operation = operations[i];
                if (operation.Operation.Package.IsSatellitePackage())
                {
                    satellitePackageOperations.Add(operation);
                    operations.RemoveAt(i);
                    i--;
                }
            }

            if (satellitePackageOperations.Count > 0)
            {
                // For satellite packages, we need to ensure that the package is uninstalled prior to uninstalling the core package. This is because the satellite package has to remove 
                // satellite files from the lib directory so that the core package does not leave any files left over. The reverse is true for install operations. As a trivial fix, we are
                // going to trivially move all uninstall satellite operations to the beginning of our reduced list and all install operations at the end.
                operations.InsertRange(0, satellitePackageOperations.Where(s => s.Operation.Action == PackageAction.Uninstall));
                operations.AddRange(satellitePackageOperations.Where(s => s.Operation.Action == PackageAction.Install));
            }
            return operations.Select(o => o.Operation)
                             .ToList();
        }


        private static object GetOperationKey(PackageOperation operation)
        {
            return Tuple.Create(operation.Action, operation.Package.Id, operation.Package.Version);
        }

        private static object GetOpposingOperationKey(PackageOperation operation)
        {
            return Tuple.Create(operation.Action == PackageAction.Install ?
                                PackageAction.Uninstall :
                                PackageAction.Install, operation.Package.Id, operation.Package.Version);
        }

        private sealed class IndexedPackageOperation
        {
            public IndexedPackageOperation(int index, PackageOperation operation)
            {
                Index = index;
                Operation = operation;
            }

            public int Index { get; set; }

            public PackageOperation Operation { get; set; }
        }
    }
}
