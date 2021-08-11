using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace NuGet
{
    internal static class QueryableUtility
    {
        private static readonly string[] _orderMethods = new[] { "OrderBy", "ThenBy", "OrderByDescending", "ThenByDescending" };
        private static readonly MethodInfo[] _methods = typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static);

        private static MethodInfo GetQueryableMethod(Expression expression)
        {
            if (expression.NodeType == ExpressionType.Call)
            {
                var call = (MethodCallExpression)expression;
                if (call.Method.IsStatic && call.Method.DeclaringType == typeof(Queryable))
                {
                    return call.Method.GetGenericMethodDefinition();
                }
            }
            return null;
        }

        public static bool IsQueryableMethod(Expression expression, string method)
        {
            return _methods.Where(m => m.Name == method).Contains(GetQueryableMethod(expression));
        }

        public static bool IsOrderingMethod(Expression expression)
        {
            return _orderMethods.Any(method => IsQueryableMethod(expression, method));
        }

        public static Expression ReplaceQueryableExpression(IQueryable query, Expression expression)
        {
            return new ExpressionRewriter(query).Visit(expression);
        }

        public static Type FindGenericType(Type definition, Type type)
        {
            while ((type != null) && (type != typeof(object)))
            {
                if (type.IsGenericType && (type.GetGenericTypeDefinition() == definition))
                {
                    return type;
                }
                if (definition.IsInterface)
                {
                    foreach (Type interfaceType in type.GetInterfaces())
                    {
                        Type genericType = FindGenericType(definition, interfaceType);
                        if (genericType != null)
                        {
                            return genericType;
                        }
                    }
                }
                type = type.BaseType;
            }
            return null;
        }

        private class ExpressionRewriter : ExpressionVisitor
        {
            private readonly IQueryable _query;

            public ExpressionRewriter(IQueryable query)
            {
                _query = query;
            }

            protected override Expression VisitConstant(ConstantExpression node)
            {
                // Replace the query at the root of the expression
                if (typeof(IQueryable).IsAssignableFrom(node.Type))
                {
                    return _query.Expression;
                }
                return base.VisitConstant(node);
            }
        }
    }
}
