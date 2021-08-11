using System;
using System.Collections.Generic;
using System.Data.Services.Client;

namespace NuGet
{
    [CLSCompliant(false)]
    public interface IDataServiceContext
    {
        Uri BaseUri { get; }
        bool IgnoreMissingProperties { get; set; }
        bool SupportsServiceMethod(string methodName);
        bool SupportsProperty(string propertyName);

        event EventHandler<SendingRequest2EventArgs> SendingRequest;
        event EventHandler<ReadingWritingEntityEventArgs> ReadingEntity;

        IDataServiceQuery<T> CreateQuery<T>(string entitySetName);
        IDataServiceQuery<T> CreateQuery<T>(string entitySetName, IDictionary<string, object> queryOptions);
        IEnumerable<T> ExecuteBatch<T>(DataServiceRequest request);

        IEnumerable<T> Execute<T>(Type elementType, DataServiceQueryContinuation continuation);
    }
}
