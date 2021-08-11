using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Services.Client;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Xml.Linq;
using NuGet.Resources;

namespace NuGet
{
    [CLSCompliant(false)]
    public class DataServiceQueryWrapper<T> : IDataServiceQuery<T>
    {
        /// <remarks>
        /// Corresponds to the default value of "maxQueryString" in system.webserver.
        /// </remarks>
        private const int MaxUrlLength = 2048;

        private readonly DataServiceQuery _query;
        private readonly IDataServiceContext _context;
        private readonly Type _concreteType;

        public DataServiceQueryWrapper(IDataServiceContext context, DataServiceQuery query)
            : this(context, query, typeof(T))
        {
        }

        public DataServiceQueryWrapper(IDataServiceContext context, DataServiceQuery query, Type concreteType)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            if (query == null)
            {
                throw new ArgumentNullException("query");
            }

            _context = context;
            _query = query;
            _concreteType = concreteType;
        }

        public bool RequiresBatch(Expression expression)
        {
            // Absolute uri returns the escaped url that would be sent to the server. Escaping exapnds the value and IIS uses this escaped query to determine if the 
            // query is of acceptable length. 
            string requestUri = GetRequestUri(expression).AbsoluteUri;
            return requestUri.Length >= MaxUrlLength;
        }

        public DataServiceRequest GetRequest(Expression expression)
        {
            return (DataServiceRequest)_query.Provider.CreateQuery(GetInnerExpression(expression));
        }

        public virtual Uri GetRequestUri(Expression expression)
        {
            return GetRequest(expression).RequestUri;
        }

        public TResult Execute<TResult>(Expression expression)
        {
            return Execute(() => _query.Provider.Execute<TResult>(GetInnerExpression(expression)));
        }

        public object Execute(Expression expression)
        {
            return Execute(() => _query.Provider.Execute(GetInnerExpression(expression)));
        }

        public IDataServiceQuery<TElement> CreateQuery<TElement>(Expression expression)
        {
            expression = GetInnerExpression(expression);

            var query = (DataServiceQuery)_query.Provider.CreateQuery<TElement>(expression);

            return new DataServiceQueryWrapper<TElement>(_context, query, typeof(T));
        }

        public IQueryable<T> AsQueryable()
        {
            return (IQueryable<T>)_query;
        }

        public IEnumerator<T> GetEnumerator()
        {
            return GetAll().GetEnumerator();
        }

        private IEnumerable<T> GetAll()
        {
            DataServiceQuery fixedQuery = _query;

            // Hack for WCF 5.6.1 to avoid using the interface
            if (typeof(T) == typeof(IPackage))
            {
                fixedQuery = (DataServiceQuery)_query.Provider.CreateQuery<DataServicePackage>(_query.Expression).Cast<DataServicePackage>();
            }

            IEnumerable results = Execute(fixedQuery.Execute);

            DataServiceQueryContinuation continuation;
            do
            {
                lock (_context)
                {
                    foreach (T item in results)
                    {
                        yield return item;
                    }
                }

                continuation = ((QueryOperationResponse)results).GetContinuation();

                if (continuation != null)
                {
                    results = _context.Execute<T>(_concreteType, continuation);
                }

            } while (continuation != null);
        }

        private Expression GetInnerExpression(Expression expression)
        {
            return QueryableUtility.ReplaceQueryableExpression(_query, expression);
        }

        public override string ToString()
        {
            return _query.ToString();
        }

        private TResult Execute<TResult>(Func<TResult> action)
        {
            try
            {
                return action();
            }
            catch (Exception exception)
            {
                string message = ExtractMessageFromClientException(exception);
                if (!String.IsNullOrEmpty(message))
                {
                    throw new InvalidOperationException(message, exception);
                }
                
                throw new InvalidOperationException(
                    String.Format(CultureInfo.CurrentCulture,
                    NuGetResources.InvalidFeed,
                    _context.BaseUri), exception);
            }
        }

        private static string ExtractMessageFromClientException(Exception exception)
        {
            var dataServiceQueryException = exception as DataServiceQueryException;
            if (dataServiceQueryException != null && dataServiceQueryException.InnerException != null)
            {
                var dataServiceClientException = dataServiceQueryException.InnerException as DataServiceClientException;
                XDocument document;
                if (dataServiceQueryException != null && 
                    XmlUtility.TryParseDocument(dataServiceClientException.Message, out document) && 
                    document.Root.Name.LocalName.Equals("error", StringComparison.OrdinalIgnoreCase))
                {
                    return document.Root.GetOptionalElementValue("message");
                }
            }
            return null;
        }
    }
}
