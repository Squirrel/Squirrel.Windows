namespace NuGet
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.CompilerServices;


    /// <summary>
    ///   A strongly-typed resource class, for looking up localized strings, etc.
    /// </summary>
    [CompilerGenerated()]
    internal static class CommonResources
    {

        private static global::System.Resources.ResourceManager resourceMan;

        private static global::System.Globalization.CultureInfo resourceCulture;

        /// <summary>
        ///   Returns the cached ResourceManager instance used by this class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Resources.ResourceManager ResourceManager
        {
            get
            {
                if (object.ReferenceEquals(resourceMan, null))
                {
                    // Find the CommonResources.resources file's full resource name in this assembly
                    string commonResourcesName = Assembly.GetExecutingAssembly().GetManifestResourceNames().First(s => s.EndsWith("CommonResources.resources", StringComparison.OrdinalIgnoreCase));

                    // Trim off the ".resources"
                    commonResourcesName = commonResourcesName.Substring(0, commonResourcesName.Length - 10);

                    // Load the resource manager
                    global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager(commonResourcesName, typeof(CommonResources).Assembly);
                    resourceMan = temp;
                }
                return resourceMan;
            }
        }

        /// <summary>
        ///   Overrides the current thread's CurrentUICulture property for all
        ///   resource lookups using this strongly typed resource class.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "Property may not be used in every assembly it is imported into")]
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Globalization.CultureInfo Culture
        {
            get
            {
                return resourceCulture;
            }
            set
            {
                resourceCulture = value;
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to {0} cannot be null or an empty string.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "Property may not be used in every assembly it is imported into")]
        internal static string Argument_Cannot_Be_Null_Or_Empty
        {
            get
            {
                return ResourceManager.GetString("Argument_Cannot_Be_Null_Or_Empty", resourceCulture);
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to {0} must be between {1} and {2}.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "Property may not be used in every assembly it is imported into")]
        internal static string Argument_Must_Be_Between
        {
            get
            {
                return ResourceManager.GetString("Argument_Must_Be_Between", resourceCulture);
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to {0} must be a valid value from the {1} enumeration.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "Property may not be used in every assembly it is imported into")]
        internal static string Argument_Must_Be_Enum_Member
        {
            get
            {
                return ResourceManager.GetString("Argument_Must_Be_Enum_Member", resourceCulture);
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to {0} must be greater than {1}.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "Property may not be used in every assembly it is imported into")]
        internal static string Argument_Must_Be_GreaterThan
        {
            get
            {
                return ResourceManager.GetString("Argument_Must_Be_GreaterThan", resourceCulture);
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to {0} must be greater than or equal to {1}.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "Property may not be used in every assembly it is imported into")]
        internal static string Argument_Must_Be_GreaterThanOrEqualTo
        {
            get
            {
                return ResourceManager.GetString("Argument_Must_Be_GreaterThanOrEqualTo", resourceCulture);
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to {0} must be less than {1}.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "Property may not be used in every assembly it is imported into")]
        internal static string Argument_Must_Be_LessThan
        {
            get
            {
                return ResourceManager.GetString("Argument_Must_Be_LessThan", resourceCulture);
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to {0} must be less than or equal to {1}.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "Property may not be used in every assembly it is imported into")]
        internal static string Argument_Must_Be_LessThanOrEqualTo
        {
            get
            {
                return ResourceManager.GetString("Argument_Must_Be_LessThanOrEqualTo", resourceCulture);
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to {0} cannot be an empty string, it must either be null or a non-empty string.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "Property may not be used in every assembly it is imported into")]
        internal static string Argument_Must_Be_Null_Or_Non_Empty
        {
            get
            {
                return ResourceManager.GetString("Argument_Must_Be_Null_Or_Non_Empty", resourceCulture);
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to This project references NuGet package(s) that are missing on this computer. Use NuGet Package Restore to download them.  For more information, see http://go.microsoft.com/fwlink/?LinkID=322105. The missing file is {0}.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "Property may not be used in every assembly it is imported into")]
        internal static string EnsureImportedMessage
        {
            get
            {
                return ResourceManager.GetString("EnsureImportedMessage", resourceCulture);
            }
        }
    }
}
