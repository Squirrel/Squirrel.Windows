using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace NuGet
{
    internal class ExpressionRewriter : ExpressionVisitor
    {
        private readonly IQueryable _rootQuery;
        private readonly IEnumerable<string> _methodsToExclude;

        public ExpressionRewriter(IQueryable rootQuery, IEnumerable<string> methodsToExclude)
        {
            _methodsToExclude = methodsToExclude;
            _rootQuery = rootQuery;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (_methodsToExclude.Any(method => QueryableUtility.IsQueryableMethod(node, method)))
            {
                return Visit(node.Arguments[0]);
            }
            return base.VisitMethodCall(node);
        }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            // Replace the query at the root of the expression
            if (typeof(IQueryable).IsAssignableFrom(node.Type))
            {
                return _rootQuery.Expression;
            }
            return base.VisitConstant(node);
        }
    }
}
