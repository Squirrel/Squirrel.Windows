using System;
using System.Collections.Generic;
using System.Data.Services.Client;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Xml.Linq;

namespace NuGet
{
    [CLSCompliant(false)]
    public class DataServiceContextWrapper : IDataServiceContext, IWeakEventListener
    {
        private const string MetadataKey = "DataServiceMetadata|";
        private static readonly MethodInfo _executeMethodInfo = typeof(DataServiceContext).GetMethod("Execute", new[] { typeof(Uri) });
        private readonly DataServiceContext _context;
        private readonly Uri _metadataUri;

        public DataServiceContextWrapper(Uri serviceRoot)
        {
            if (serviceRoot == null)
            {
                throw new ArgumentNullException("serviceRoot");
            }

            _context = new DataServiceContext(serviceRoot)
                       {
                           MergeOption = MergeOption.NoTracking
                       };

            _metadataUri = _context.GetMetadataUri();

            AttachEvents();
        }

        private DataServiceClientRequestMessage ShimWebRequests(DataServiceClientRequestMessageArgs args)
        {
            // Shim the requests if needed
            return HttpShim.Instance.ShimDataServiceRequest(args);
        }

        public bool ReceiveWeakEvent(Type managerType, object sender, EventArgs e)
        {
            if (managerType == typeof(Func<DataServiceClientRequestMessage, DataServiceClientRequestMessageArgs>))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private void AttachEvents()
        {
            _context.Configurations.RequestPipeline.OnMessageCreating += ShimWebRequests;
        }

        //private void DetachEvents()
        //{
        //    _context.Configurations.RequestPipeline.OnMessageCreating -= ShimWebRequests;
        //}

        //public void Dispose()
        //{
        //    DetachEvents();
        //}

        public Uri BaseUri
        {
            get
            {
                return _context.BaseUri;
            }
        }

        public event EventHandler<SendingRequest2EventArgs> SendingRequest
        {
            add
            {
                _context.SendingRequest2 += value;
            }
            remove
            {
                _context.SendingRequest2 -= value;
            }
        }

        public event EventHandler<ReadingWritingEntityEventArgs> ReadingEntity
        {
            add
            {
                _context.ReadingEntity += value;
            }
            remove
            {
                _context.ReadingEntity -= value;
            }
        }

        public bool IgnoreMissingProperties
        {
            get
            {
                return _context.IgnoreMissingProperties;
            }
            set
            {
                _context.IgnoreMissingProperties = value;
            }
        }

        private DataServiceMetadata ServiceMetadata
        {
            get
            {
                return MemoryCache.Instance.GetOrAdd(GetServiceMetadataKey(), () => GetDataServiceMetadata(_metadataUri), TimeSpan.FromMinutes(15));
            }
        }

        public IDataServiceQuery<T> CreateQuery<T>(string entitySetName, IDictionary<string, object> queryOptions)
        {
            var query = _context.CreateQuery<T>(entitySetName);
            foreach (var pair in queryOptions)
            {
                query = query.AddQueryOption(pair.Key, pair.Value);
            }
            return new DataServiceQueryWrapper<T>(this, query);
        }

        public IDataServiceQuery<T> CreateQuery<T>(string entitySetName)
        {
            return new DataServiceQueryWrapper<T>(this, _context.CreateQuery<T>(entitySetName));
        }

        public IEnumerable<T> Execute<T>(Type elementType, DataServiceQueryContinuation continuation)
        {
            // Get the generic execute method
            MethodInfo executeMethod = _executeMethodInfo.MakeGenericMethod(elementType);

            // Get the results from the continuation
            return (IEnumerable<T>)executeMethod.Invoke(_context, new object[] { continuation.NextLinkUri });
        }

        public IEnumerable<T> ExecuteBatch<T>(DataServiceRequest request)
        {
            return _context.ExecuteBatch(request)
                           .Cast<QueryOperationResponse>()
                           .SelectMany(o => o.Cast<T>());
        }

        public bool SupportsServiceMethod(string methodName)
        {
            return ServiceMetadata != null && ServiceMetadata.SupportedMethodNames.Contains(methodName);
        }

        public bool SupportsProperty(string propertyName)
        {
            return ServiceMetadata != null && ServiceMetadata.SupportedProperties.Contains(propertyName);
        }

        internal sealed class DataServiceMetadata
        {
            public HashSet<string> SupportedMethodNames { get; set; }

            public HashSet<string> SupportedProperties { get; set; }
        }

        private string GetServiceMetadataKey()
        {
            return MetadataKey + _metadataUri.OriginalString;
        }

        private static DataServiceMetadata GetDataServiceMetadata(Uri metadataUri)
        {
            if (metadataUri == null)
            {
                return null;
            }

            // Make a request to the metadata uri and get the schema
            var client = new HttpClient(metadataUri);

            using (MemoryStream stream = new MemoryStream())
            {
                client.DownloadData(stream);

                stream.Seek(0, SeekOrigin.Begin);
                return ExtractMetadataFromSchema(stream);
            }
        }

        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "If the docuument is in fails to parse in any way, we want to not fail.")]
        internal static DataServiceMetadata ExtractMetadataFromSchema(Stream schemaStream)
        {
            if (schemaStream == null)
            {
                return null;
            }

            XDocument schemaDocument;

            try
            {
                schemaDocument = XmlUtility.LoadSafe(schemaStream);
            }
            catch
            {
                // If the schema is malformed (for some reason) then just return empty list
                return null;
            }

            return ExtractMetadataInternal(schemaDocument);
        }

        private static DataServiceMetadata ExtractMetadataInternal(XDocument schemaDocument)
        {
            // Get all entity containers
            var entityContainers = from e in schemaDocument.Descendants()
                                   where e.Name.LocalName == "EntityContainer"
                                   select e;

            // Find the entity container with the Packages entity set
            var result = (from e in entityContainers
                          let entitySet = e.Elements().FirstOrDefault(el => el.Name.LocalName == "EntitySet")
                          let name = entitySet != null ? entitySet.Attribute("Name").Value : null
                          where name != null && name.Equals("Packages", StringComparison.OrdinalIgnoreCase)
                          select new { Container = e, EntitySet = entitySet }).FirstOrDefault();

            if (result == null)
            {
                return null;
            }
            var packageEntityContainer = result.Container;
            var packageEntityTypeAttribute = result.EntitySet.Attribute("EntityType");
            string packageEntityName = null;
            if (packageEntityTypeAttribute != null)
            {
                packageEntityName = packageEntityTypeAttribute.Value;
            }

            var metadata = new DataServiceMetadata
            {
                SupportedMethodNames = new HashSet<string>(
                                               from e in packageEntityContainer.Elements()
                                               where e.Name.LocalName == "FunctionImport"
                                               select e.Attribute("Name").Value, StringComparer.OrdinalIgnoreCase),
                SupportedProperties = new HashSet<string>(ExtractSupportedProperties(schemaDocument, packageEntityName),
                                                          StringComparer.OrdinalIgnoreCase)
            };
            return metadata;
        }

        private static IEnumerable<string> ExtractSupportedProperties(XDocument schemaDocument, string packageEntityName)
        {
            // The name is listed in the entity set listing as <EntitySet Name="Packages" EntityType="Gallery.Infrastructure.FeedModels.PublishedPackage" />
            // We need to extract the name portion to look up the entity type <EntityType Name="PublishedPackage" 
            packageEntityName = TrimNamespace(packageEntityName);

            var packageEntity = (from e in schemaDocument.Descendants()
                                 where e.Name.LocalName == "EntityType"
                                 let attribute = e.Attribute("Name")
                                 where attribute != null && attribute.Value.Equals(packageEntityName, StringComparison.OrdinalIgnoreCase)
                                 select e).FirstOrDefault();

            if (packageEntity != null)
            {
                return from e in packageEntity.Elements()
                       where e.Name.LocalName == "Property"
                       select e.Attribute("Name").Value;
            }
            return Enumerable.Empty<string>();
        }

        private static string TrimNamespace(string packageEntityName)
        {
            int lastIndex = packageEntityName.LastIndexOf('.');
            if (lastIndex > 0 && lastIndex < packageEntityName.Length)
            {
                packageEntityName = packageEntityName.Substring(lastIndex + 1);
            }
            return packageEntityName;
        }
    }
}
