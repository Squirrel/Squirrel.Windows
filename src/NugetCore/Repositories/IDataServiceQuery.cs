using System;
using System.Collections.Generic;
using System.Data.Services.Client;
using System.Linq;
using System.Linq.Expressions;

namespace NuGet
{
    [CLSCompliant(false)]
    public interface IDataServiceQuery<out T> : IDataServiceQuery
    {
        IEnumerator<T> GetEnumerator();
        IQueryable<T> AsQueryable();
    }

    [CLSCompliant(false)]
    public interface IDataServiceQuery
    {
        bool RequiresBatch(Expression expression);
        DataServiceRequest GetRequest(Expression expression);
        IDataServiceQuery<TElement> CreateQuery<TElement>(Expression expression);
        TResult Execute<TResult>(Expression expression);
        object Execute(Expression expression);
    }
}