using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Services.Client;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows;
using NuGet.Resources;

namespace NuGet
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling")]    
    public class DataServicePackageRepository : 
        PackageRepositoryBase, 
        IHttpClientEvents, 
        IServiceBasedRepository, 
        ICloneableRepository, 
        ICultureAwareRepository, 
        IOperationAwareRepository,
        IPackageLookup,
        ILatestPackageLookup,
        IWeakEventListener
    {
        private const string FindPackagesByIdSvcMethod = "FindPackagesById";
        private const string PackageServiceEntitySetName = "Packages";
        private const string SearchSvcMethod = "Search";
        private const string GetUpdatesSvcMethod = "GetUpdates";

        private IDataServiceContext _context;
        private readonly IHttpClient _httpClient;
        private readonly PackageDownloader _packageDownloader;
        private CultureInfo _culture;
        private Tuple<string, string, string> _currentOperation;
        private event EventHandler<WebRequestEventArgs> _sendingRequest;

        public DataServicePackageRepository(Uri serviceRoot)
            : this(new HttpClient(serviceRoot))
        {
        }

        public DataServicePackageRepository(IHttpClient client)
            : this(client, new PackageDownloader())
        {
        }

        public DataServicePackageRepository(IHttpClient client, PackageDownloader packageDownloader)
        {
            if (client == null)
            {
                throw new ArgumentNullException("client");
            }
            if (packageDownloader == null)
            {
                throw new ArgumentNullException("packageDownloader");
            }

            _httpClient = client;
            _httpClient.AcceptCompression = true;
            
            _packageDownloader = packageDownloader;

            if (EnvironmentUtility.RunningFromCommandLine || EnvironmentUtility.IsMonoRuntime)
            {
                _packageDownloader.SendingRequest += OnPackageDownloaderSendingRequest;
            }
            else
            {
                // weak event pattern            
                SendingRequestEventManager.AddListener(_packageDownloader, this);
            }
        }

        private void OnPackageDownloaderSendingRequest(object sender, WebRequestEventArgs e)
        {
            // add headers for the metric service
            if (_currentOperation != null)
            {
                string operation = _currentOperation.Item1;
                string mainPackageId = _currentOperation.Item2;
                string mainPackageVersion = _currentOperation.Item3;

                if (!String.IsNullOrEmpty(mainPackageId) && !String.IsNullOrEmpty(_packageDownloader.CurrentDownloadPackageId))
                {
                    if (!mainPackageId.Equals(_packageDownloader.CurrentDownloadPackageId, StringComparison.OrdinalIgnoreCase))
                    {
                        operation = operation + "-Dependency";
                    }
                }

                // add the id and version to the headers
                if (!String.IsNullOrEmpty(_packageDownloader.CurrentDownloadPackageId) && !String.IsNullOrEmpty(_packageDownloader.CurrentDownloadPackageVersion))
                {
                    e.Request.Headers[RepositoryOperationNames.PackageId] = _packageDownloader.CurrentDownloadPackageId;
                    e.Request.Headers[RepositoryOperationNames.PackageVersion] = _packageDownloader.CurrentDownloadPackageVersion;
                }

                e.Request.Headers[RepositoryOperationNames.OperationHeaderName] = operation;

                if (!operation.Equals(_currentOperation.Item1, StringComparison.OrdinalIgnoreCase))
                {
                    e.Request.Headers[RepositoryOperationNames.DependentPackageHeaderName] = mainPackageId;
                    if (!String.IsNullOrEmpty(mainPackageVersion))
                    {
                        e.Request.Headers[RepositoryOperationNames.DependentPackageVersionHeaderName] = mainPackageVersion;
                    }
                }

                RaiseSendingRequest(e);
            }
        }

        // Just forward calls to the package downloader
        public event EventHandler<ProgressEventArgs> ProgressAvailable
        {
            add
            {
                _packageDownloader.ProgressAvailable += value;
            }
            remove
            {
                _packageDownloader.ProgressAvailable -= value;
            }
        }

        public event EventHandler<WebRequestEventArgs> SendingRequest
        {
            add
            {
                _packageDownloader.SendingRequest += value;
                _httpClient.SendingRequest += value;
                _sendingRequest += value;
            }
            remove
            {
                _packageDownloader.SendingRequest -= value;
                _httpClient.SendingRequest -= value;
                _sendingRequest -= value;
            }
        }

        public CultureInfo Culture
        {
            get
            {
                if (_culture == null)
                {
                    // TODO: Technically, if this is a remote server, we have to return the culture of the server
                    // instead of invariant culture. However, there is no trivial way to retrieve the server's culture,
                    // So temporarily use Invariant culture here. 
                    _culture = _httpClient.Uri.IsLoopback ? CultureInfo.CurrentCulture : CultureInfo.InvariantCulture;
                }
                return _culture;
            }
        }

        // Do NOT delete this property. It is used by the functional test.
        public PackageDownloader PackageDownloader
        {
            get { return _packageDownloader; }
        }

        public override string Source
        {
            get
            {
                return _httpClient.Uri.OriginalString;
            }
        }

        public override bool SupportsPrereleasePackages
        {
            get
            {
                return Context.SupportsProperty("IsAbsoluteLatestVersion");
            }
        }

        // Don't initialize the Context at the constructor time so that
        // we don't make a web request if we are not going to actually use it
        // since getting the Uri property of the RedirectedHttpClient will
        // trigger that functionality.
        internal IDataServiceContext Context
        {
            private get
            {
                if (_context == null)
                {
                    _context = new DataServiceContextWrapper(_httpClient.Uri);
                    _context.SendingRequest += OnSendingRequest;
                    _context.ReadingEntity += OnReadingEntity;
                    _context.IgnoreMissingProperties = true;
                }
                return _context;
            }
            set
            {
                _context = value;
            }
        }

        private void OnReadingEntity(object sender, ReadingWritingEntityEventArgs e)
        {
            var package = (DataServicePackage)e.Entity;

            var downloadUri = e.Data.Element(e.Data.Name.Namespace.GetName("content"))
                .Attribute(System.Xml.Linq.XName.Get("src")).Value;
            package.DownloadUrl = new Uri(downloadUri);
            package.Downloader = _packageDownloader;
        }

        private void OnSendingRequest(object sender, SendingRequest2EventArgs e)
        {
            var shimRequest = new ShimDataRequestMessage(e);

            // Initialize the request
            _httpClient.InitializeRequest(shimRequest.WebRequest);


            RaiseSendingRequest(new WebRequestEventArgs(shimRequest.WebRequest));
        }

        private void RaiseSendingRequest(WebRequestEventArgs e)
        {
            if (_sendingRequest != null)
            {
                _sendingRequest(this, e);
            }
        }

        public override IQueryable<IPackage> GetPackages()
        {
            // REVIEW: Is it ok to assume that the package entity set is called packages?
            return new SmartDataServiceQuery<DataServicePackage>(Context, PackageServiceEntitySetName);
        }

        public IQueryable<IPackage> Search(string searchTerm, IEnumerable<string> targetFrameworks, bool allowPrereleaseVersions, bool includeDelisted)
        {
            if (!Context.SupportsServiceMethod(SearchSvcMethod))
            {
                // If there's no search method then we can't filter by target framework
                var q = GetPackages()
                    .Find(searchTerm)
                    .FilterByPrerelease(allowPrereleaseVersions);

                // filter out delisted packages if includeDelisted is false.
                if (includeDelisted == false)
                {
                    q = q.Where(p => p.IsListed());
                }

                return q.AsQueryable();
            }

            // Create a '|' separated string of framework names
            string targetFrameworkString = String.Join("|", targetFrameworks);

            var searchParameters = new Dictionary<string, object> {
                { "searchTerm", "'" + UrlEncodeOdataParameter(searchTerm) + "'" },
                { "targetFramework", "'" + UrlEncodeOdataParameter(targetFrameworkString) + "'" },
            };

            if (SupportsPrereleasePackages)
            {
                searchParameters.Add("includePrerelease", ToLowerCaseString(allowPrereleaseVersions));
            }

            if (includeDelisted)
            {
                searchParameters.Add("includeDelisted", "true");
            }

            // Create a query for the search service method
            var query = Context.CreateQuery<DataServicePackage>(SearchSvcMethod, searchParameters);
            return new SmartDataServiceQuery<DataServicePackage>(Context, query);
        }

        public bool Exists(string packageId, SemanticVersion version)
        {
            IQueryable<DataServicePackage> query = Context.CreateQuery<DataServicePackage>(PackageServiceEntitySetName).AsQueryable();

            foreach (string versionString in version.GetComparableVersionStrings())
            {
                try
                {
                    var packages = query.Where(p => p.Id == packageId && p.Version == versionString)
                                    .Select(p => p.Id)      // since we only want to check for existence, no need to get all attributes
                                    .ToArray();

                    if (packages.Length == 1)
                    {
                        return true;
                    }
                }
                catch (DataServiceQueryException)
                {
                    // DataServiceQuery exception will occur when the (id, version) 
                    // combination doesn't exist.
                }
            }

            return false;
        }

        public IPackage FindPackage(string packageId, SemanticVersion version)
        {
            IQueryable<DataServicePackage> query = Context.CreateQuery<DataServicePackage>(PackageServiceEntitySetName).AsQueryable();

            foreach (string versionString in version.GetComparableVersionStrings())
            {
                try
                {
                    var packages = query.Where(p => p.Id == packageId && p.Version == versionString).ToArray();
                    Debug.Assert(packages == null || packages.Length <= 1);
                    if (packages.Length != 0)
                    {
                        return packages[0];
                    }
                }
                catch (DataServiceQueryException)
                {
                    // DataServiceQuery exception will occur when the (id, version) 
                    // combination doesn't exist.
                }
            }

            return null;
        }

        public IEnumerable<IPackage> FindPackagesById(string packageId)
        {
            try
            {
                if (!Context.SupportsServiceMethod(FindPackagesByIdSvcMethod))
                {
                    // If there's no search method then we can't filter by target framework
                    return PackageRepositoryExtensions.FindPackagesByIdCore(this, packageId);
                }

                var serviceParameters = new Dictionary<string, object> {
                    { "id", "'" + UrlEncodeOdataParameter(packageId) + "'" }
                };

                // Create a query for the search service method
                var query = Context.CreateQuery<DataServicePackage>(FindPackagesByIdSvcMethod, serviceParameters);
                return new SmartDataServiceQuery<DataServicePackage>(Context, query);
            }
            catch (Exception ex)
            {
                var message = string.Format(
                    CultureInfo.CurrentCulture,
                    NuGetResources.ErrorLoadingPackages,
                    _httpClient.OriginalUri,
                    ex.Message);
                throw new InvalidOperationException(message, ex);
            }
        }

        public IEnumerable<IPackage> GetUpdates(
            IEnumerable<IPackageName> packages, 
            bool includePrerelease, 
            bool includeAllVersions, 
            IEnumerable<FrameworkName> targetFrameworks,
            IEnumerable<IVersionSpec> versionConstraints)
        {
            if (!Context.SupportsServiceMethod(GetUpdatesSvcMethod))
            {
                // If there's no search method then we can't filter by target framework
                return PackageRepositoryExtensions.GetUpdatesCore(this, packages, includePrerelease, includeAllVersions, targetFrameworks, versionConstraints);
            }

            // Pipe all the things!
            string ids = String.Join("|", packages.Select(p => p.Id));
            string versions = String.Join("|", packages.Select(p => p.Version.ToString()));
            string targetFrameworksValue = targetFrameworks.IsEmpty() ? "" : String.Join("|", targetFrameworks.Select(VersionUtility.GetShortFrameworkName));
            string versionConstraintsValue = versionConstraints.IsEmpty() ? "" : String.Join("|", versionConstraints.Select(v => v == null ? "" : v.ToString()));

            var serviceParameters = new Dictionary<string, object> {
                { "packageIds", "'" + ids + "'" },
                { "versions", "'" + versions + "'" },
                { "includePrerelease", ToLowerCaseString(includePrerelease) },
                { "includeAllVersions", ToLowerCaseString(includeAllVersions) },
                { "targetFrameworks", "'" + UrlEncodeOdataParameter(targetFrameworksValue) + "'" },
                { "versionConstraints", "'" + UrlEncodeOdataParameter(versionConstraintsValue) + "'" }
            };

            var query = Context.CreateQuery<DataServicePackage>(GetUpdatesSvcMethod, serviceParameters);
            return new SmartDataServiceQuery<DataServicePackage>(Context, query);
        }

        public IPackageRepository Clone()
        {
            return new DataServicePackageRepository(_httpClient, _packageDownloader);
        }

        public IDisposable StartOperation(string operation, string mainPackageId, string mainPackageVersion)
        {
            Tuple<string, string, string> oldOperation = _currentOperation;
            _currentOperation = Tuple.Create(operation, mainPackageId, mainPackageVersion);
            return new DisposableAction(() =>
            {
                _currentOperation = oldOperation;
            });
        }

        public bool TryFindLatestPackageById(string id, out SemanticVersion latestVersion)
        {
            latestVersion = null;

            try
            {
                var serviceParameters = new Dictionary<string, object> {
                    { "id", "'" + UrlEncodeOdataParameter(id) + "'" }
                };

                // Create a query for the search service method
                var query = Context.CreateQuery<DataServicePackage>(FindPackagesByIdSvcMethod, serviceParameters);
                var packages = (IQueryable<DataServicePackage>)query.AsQueryable();

                var latestPackage = packages.Where(p => p.IsLatestVersion)
                                            .Select(p => new { p.Id, p.Version })
                                            .FirstOrDefault();

                if (latestPackage != null)
                {
                    latestVersion = new SemanticVersion(latestPackage.Version);
                    return true;
                }
            }
            catch (DataServiceQueryException)
            {
            }

            return false;
        }

        public bool TryFindLatestPackageById(string id, bool includePrerelease, out IPackage package)
        {
            try
            {
                var serviceParameters = new Dictionary<string, object> {
                    { "id", "'" + UrlEncodeOdataParameter(id) + "'" }
                };

                // Create a query for the search service method
                var query = Context.CreateQuery<DataServicePackage>(FindPackagesByIdSvcMethod, serviceParameters);
                var packages = (IQueryable<DataServicePackage>)query.AsQueryable();

                if (includePrerelease)
                {
                    package = packages.Where(p => p.IsAbsoluteLatestVersion).OrderByDescending(p => p.Version).FirstOrDefault();
                }
                else
                {
                    package = packages.Where(p => p.IsLatestVersion).OrderByDescending(p => p.Version).FirstOrDefault();
                }

                return package != null;
            }
            catch (DataServiceQueryException)
            {
                package = null;
                return false;
            }
        }

        private static string UrlEncodeOdataParameter(string value)
        {
            if (!String.IsNullOrEmpty(value))
            {
                // OData requires that a single quote MUST be escaped as 2 single quotes.
                // In .NET 4.5, Uri.EscapeDataString() escapes single quote as %27. Thus we must replace %27 with 2 single quotes.
                // In .NET 4.0, Uri.EscapeDataString() doesn't escape single quote. Thus we must replace it with 2 single quotes.
                return Uri.EscapeDataString(value).Replace("'", "''").Replace("%27", "''");
            }

            return value;
        }

        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase", Justification = "OData expects a lower case value.")]
        private static string ToLowerCaseString(bool value)
        {
            return value.ToString().ToLowerInvariant();
        }

        public bool ReceiveWeakEvent(Type managerType, object sender, EventArgs e)
        {
            if (managerType == typeof(SendingRequestEventManager))
            {
                OnPackageDownloaderSendingRequest(sender, (WebRequestEventArgs)e);
                return true;
            }
            else
            {
                return false;
            } 
        }
    }
}