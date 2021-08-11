namespace NuGet
{
    /// <summary>
    /// Controls which version of a dependency is selected.
    /// </summary>
    /// <example>
    /// Suppose package A has a dependency on B [2.1, 4.0). Available versions of B
    /// are 2.1.1, 2.1.4, 2.2.0, 2.4.0, 3.1.0, 4.0.
    /// Then the version of B selected is
    /// - 2.1.1, when Lowest
    /// - 2.1.4, when HighestPatch
    /// - 2.4.0, when HighestMinor
    /// - 3.1.0, when Highest
    /// </example>
    public enum DependencyVersion
    {
        /// <summary>
        /// The lowest matching version available.
        /// </summary>
        Lowest,

        /// <summary>
        /// The version with the highest patch number.
        /// </summary>
        HighestPatch,

        /// <summary>
        /// The version with the highest minor version.
        /// </summary>
        HighestMinor,

        /// <summary>
        /// The highest matching version available.
        /// </summary>
        Highest
    }
}
