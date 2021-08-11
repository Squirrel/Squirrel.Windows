using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace NuGet
{
    public class ConfigurationDefaults
    {
        private ISettings _settingsManager = NullSettings.Instance;
        private const string ConfigurationDefaultsFile = "NuGetDefaults.config";
        private static readonly ConfigurationDefaults _instance = InitializeInstance();

        private bool _defaultPackageSourceInitialized;
        private List<PackageSource> _defaultPackageSources;
        private string _defaultPushSource;

        private static ConfigurationDefaults InitializeInstance()
        {
            var baseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NuGet");
            PhysicalFileSystem fileSystem = new PhysicalFileSystem(baseDirectory);
            return new ConfigurationDefaults(fileSystem, ConfigurationDefaultsFile);
        }

        /// <summary>
        /// An internal constructor MAINLY INTENDED FOR TESTING THE CLASS. But, the product code is only expected to use the static Instance property
        /// Only catches FileNotFoundException. Will throw all exceptions including other IOExceptions and XmlExceptions for invalid xml and so on
        /// </summary>
        /// <param name="fileSystem"></param>
        /// <param name="path"></param>
        internal ConfigurationDefaults(IFileSystem fileSystem, string path)
        {
            try
            {
                if (fileSystem.FileExists(path))
                {
                    _settingsManager = new Settings(fileSystem, path);
                }
            }
            catch (FileNotFoundException)
            {
            }

            // Intentionally, we don't catch all IOExceptions, XmlException or other file related exceptions like UnAuthorizedAccessException
            // This way, administrator will become aware of the failures when the ConfigurationDefaults file is not valid or permissions are not set properly
        }

        public static ConfigurationDefaults Instance
        {
            get
            {
                return _instance;
            }
        }

        public IEnumerable<PackageSource> DefaultPackageSources
        {
            get
            {
                if (_defaultPackageSources == null)
                {
                    _defaultPackageSources = new List<PackageSource>();
                    IList<SettingValue> disabledPackageSources = _settingsManager.GetSettingValues("disabledPackageSources", isPath: false);
                    IList<SettingValue> packageSources = _settingsManager.GetSettingValues("packageSources", isPath: false);

                    foreach (var settingValue in packageSources)
                    {
                        // In a SettingValue representing a package source, the Key represents the name of the package source and the Value its source
                        _defaultPackageSources.Add(new PackageSource(settingValue.Value,
                            settingValue.Key,
                            isEnabled: !disabledPackageSources.Any<SettingValue>(p => p.Key.Equals(settingValue.Key, StringComparison.CurrentCultureIgnoreCase)),
                            isOfficial: true));
                    }
                }
                return _defaultPackageSources;
            }
        }

        public string DefaultPushSource
        {
            get
            {
                if (_defaultPushSource == null && !_defaultPackageSourceInitialized)
                {
                    _defaultPackageSourceInitialized = true;
                    _defaultPushSource = _settingsManager.GetConfigValue("DefaultPushSource");
                }
                return _defaultPushSource;
            }
        }

        public string DefaultPackageRestoreConsent
        {
            get
            {
                return _settingsManager.GetValue("packageRestore", "enabled");
            }
        }
    }
}
