using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace NuGet
{
    internal class AggregateQuery<TVal> : IQueryable<TVal>, IQueryProvider, IOrderedQueryable<TVal>
    {
        private const int QueryCacheSize = 30;

        private readonly IEnumerable<IQueryable<TVal>> _queryables;
        private readonly Expression _expression;
        private readonly IEqualityComparer<TVal> _equalityComparer;
        private readonly IList<IEnumerable<TVal>> _subQueries;
        private readonly bool _ignoreFailures;
        private readonly ILogger _logger;

        public AggregateQuery(IEnumerable<IQueryable<TVal>> queryables, IEqualityComparer<TVal> equalityComparer, ILogger logger, bool ignoreFailures)
        {
            _queryables = queryables;
            _equalityComparer = equalityComparer;
            _expression = Expression.Constant(this);
            _ignoreFailures = ignoreFailures;
            _logger = logger;
            _subQueries = GetSubQueries(_expression);
        }

        /// <summary>
        /// This constructor is used by unit tests.
        /// </summary>
        private AggregateQuery(IEnumerable<IQueryable<TVal>> queryables,
                               IEqualityComparer<TVal> equalityComparer,
                               IList<IEnumerable<TVal>> subQueries,
                               Expression expression,
                               ILogger logger,
                               bool ignoreInvalidRepositories)
        {
            _queryables = queryables;
            _equalityComparer = equalityComparer;
            _expression = expression;
            _subQueries = subQueries;
            _ignoreFailures = ignoreInvalidRepositories;
            _logger = logger;
        }

        public IEnumerator<TVal> GetEnumerator()
        {
            // Rewrite the expression for aggregation i.e. remove things that don't make sense to apply
            // after all initial expression has been applied.
            var aggregateQuery = GetAggregateEnumerable().AsQueryable();

            Expression aggregateExpression = RewriteForAggregation(aggregateQuery, Expression);
            return aggregateQuery.Provider.CreateQuery<TVal>(aggregateExpression).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public Type ElementType
        {
            get
            {
                return typeof(TVal);
            }
        }

        public Expression Expression
        {
            get
            {
                return _expression;
            }
        }

        public IQueryProvider Provider
        {
            get
            {
                return this;
            }
        }

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        {
            return (IQueryable<TElement>)CreateQuery(typeof(TElement), expression);
        }

        public IQueryable CreateQuery(Expression expression)
        {
            // Copied logic from EnumerableQuery
            if (expression == null)
            {
                throw new ArgumentNullException("expression");
            }

            Type elementType = QueryableUtility.FindGenericType(typeof(IQueryable<>), expression.Type);

            if (elementType == null)
            {
                throw new ArgumentException(String.Empty, "expression");
            }

            return CreateQuery(elementType, expression);
        }

        public TResult Execute<TResult>(Expression expression)
        {
            var results = (from queryable in _queryables
                           select TryExecute<TResult>(queryable, expression)).AsQueryable();

            if (QueryableUtility.IsQueryableMethod(expression, "Count"))
            {
                // HACK: This is in correct since we aren't removing duplicates but count is mostly for paging
                // so we don't care *that* much
                return (TResult)(object)results.Cast<int>().Sum();
            }

            return TryExecute<TResult>(results, expression);
        }

        public object Execute(Expression expression)
        {
            return Execute<object>(expression);
        }

        private IEnumerable<TVal> GetAggregateEnumerable()
        {
            // Used to pick the right element from each sub query in the right order
            var comparer = new OrderingComparer<TVal>(Expression);

            if (!comparer.CanCompare)
            {
                // If the original queries do not have sort expressions, we'll use the order of the subqueries to read results out.
                return _subQueries.SelectMany(query => _ignoreFailures ? query.SafeIterate() : query)
                                  .Distinct(_equalityComparer);
            }
            return ReadOrderedQueues(comparer);
        }

        /// <summary>
        /// Reads the minimal set of queries 
        /// </summary>
        /// <param name="comparer"></param>
        /// <returns></returns>
        private IEnumerable<TVal> ReadOrderedQueues(IComparer<TVal> comparer)
        {
            // Create lazy queues over each sub query so we can lazily pull items from it
            var lazyQueues = _subQueries.Select(query => new LazyQueue<TVal>(query.GetEnumerator())).ToList();

            // Used to keep track of everything we've seen so far (we never show duplicates)
            var seen = new HashSet<TVal>(_equalityComparer);
            do
            {
                TVal minElement = default(TVal);
                LazyQueue<TVal> minQueue = null;

                // Run tasks in parallel
                var tasks = (from queue in lazyQueues
                             select Task.Factory.StartNew<TaskResult>(() => ReadQueue(queue))
                             ).ToArray();

                // Wait for everything to complete
                Task.WaitAll(tasks);

                foreach (var task in tasks)
                {
                    if (task.Result.HasValue)
                    {
                        // Keep track of the minimum element in the list
                        if (minElement == null || comparer.Compare(task.Result.Value, minElement) < 0)
                        {
                            minElement = task.Result.Value;
                            minQueue = task.Result.Queue;
                        }
                    }
                    else
                    {
                        // Remove the enumerator if it's empty
                        lazyQueues.Remove(task.Result.Queue);
                    }
                }

                if (lazyQueues.Any())
                {
                    if (seen.Add(minElement))
                    {
                        yield return minElement;
                    }

                    // Clear the top of the enumerator we just peeked
                    minQueue.Dequeue();
                }

            } while (lazyQueues.Count > 0);
        }

        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "By definition we want to suppress all exceptions if the flag is set")]
        private TaskResult ReadQueue(LazyQueue<TVal> queue)
        {
            var result = new TaskResult { Queue = queue };
            TVal current;
            if (_ignoreFailures)
            {
                try
                {
                    result.HasValue = queue.TryPeek(out current);
                }
                catch (Exception ex)
                {
                    LogWarning(ex);
                    current = default(TVal);
                }
            }
            else
            {
                result.HasValue = queue.TryPeek(out current);
            }
            result.Value = current;

            return result;
        }

        private IList<IEnumerable<TVal>> GetSubQueries(Expression expression)
        {
            return _queryables.Select(query => GetSubQuery(query, expression)).ToList();
        }

        private IQueryable CreateQuery(Type elementType, Expression expression)
        {
            var queryType = typeof(AggregateQuery<>).MakeGenericType(elementType);
            var ctor = queryType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();

            var subQueries = _subQueries;

            // Only update subqueries for ordering and where clauses
            if (QueryableUtility.IsQueryableMethod(expression, "Where") ||
                QueryableUtility.IsOrderingMethod(expression))
            {
                subQueries = GetSubQueries(expression);
            }

            return (IQueryable)ctor.Invoke(new object[] { _queryables, _equalityComparer, subQueries, expression, _logger, _ignoreFailures });
        }

        private void LogWarning(Exception ex)
        {
            _logger.Log(MessageLevel.Warning, ExceptionUtility.Unwrap(ex).Message);
        }

        private static IEnumerable<TVal> GetSubQuery(IQueryable queryable, Expression expression)
        {
            expression = Rewrite(queryable, expression);

            IQueryable<TVal> newQuery = queryable.Provider.CreateQuery<TVal>(expression);

            // Create the query and only get up to the query cache size
            return new BufferedEnumerable<TVal>(newQuery, QueryCacheSize);
        }

        private static TResult Execute<TResult>(IQueryable queryable, Expression expression)
        {
            return queryable.Provider
                            .Execute<TResult>(Rewrite(queryable, expression));
        }

        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "By definition we want to suppress all exceptions if the flag is set")]
        private TResult TryExecute<TResult>(IQueryable queryable, Expression expression)
        {
            if (_ignoreFailures)
            {
                try
                {
                    return Execute<TResult>(queryable, expression);
                }
                catch (Exception ex)
                {
                    LogWarning(ex);
                    return default(TResult);
                }
            }
            return Execute<TResult>(queryable, expression);
        }

        private static Expression RewriteForAggregation(IQueryable queryable, Expression expression)
        {
            // Remove filters, and ordering from the aggregate query
            return new ExpressionRewriter(queryable, new[] { "Where", 
                                                             "OrderBy", 
                                                             "OrderByDescending",
                                                             "ThenBy",
                                                             "ThenByDescending" }).Visit(expression);
        }

        private static Expression Rewrite(IQueryable queryable, Expression expression)
        {
            // Remove all take an skip and take expression from individual linq providers
            return new ExpressionRewriter(queryable, new[] { "Skip", 
                                                             "Take" }).Visit(expression);
        }

        private class TaskResult
        {
            public LazyQueue<TVal> Queue { get; set; }

            public bool HasValue { get; set; }

            public TVal Value { get; set; }
        }
    }
}
