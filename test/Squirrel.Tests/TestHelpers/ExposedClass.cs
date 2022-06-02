using System;
using System.Collections.Generic;
using System.Linq;
using System.Dynamic;
using System.Reflection;

// Lovingly stolen from http://exposedobject.codeplex.com/

namespace Squirrel.Tests.TestHelpers
{
    public class ExposedClass : DynamicObject
    {
        private Type m_type;
        private Dictionary<string, Dictionary<int, List<MethodInfo>>> m_staticMethods;
        private Dictionary<string, Dictionary<int, List<MethodInfo>>> m_genStaticMethods;

        private ExposedClass(Type type)
        {
            m_type = type;

            m_staticMethods =
                m_type
                    .GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                    .Where(m => !m.IsGenericMethod)
                    .GroupBy(m => m.Name)
                    .ToDictionary(
                        p => p.Key,
                        p => p.GroupBy(r => r.GetParameters().Length).ToDictionary(r => r.Key, r => r.ToList()));

            m_genStaticMethods =
                m_type
                    .GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                    .Where(m => m.IsGenericMethod)
                    .GroupBy(m => m.Name)
                    .ToDictionary(
                        p => p.Key,
                        p => p.GroupBy(r => r.GetParameters().Length).ToDictionary(r => r.Key, r => r.ToList()));
        }

        public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
        {
            // Get type args of the call
            Type[] typeArgs = ExposedObjectHelper.GetTypeArgs(binder);
            if (typeArgs != null && typeArgs.Length == 0) typeArgs = null;

            //
            // Try to call a non-generic instance method
            //
            if (typeArgs == null
                    && m_staticMethods.ContainsKey(binder.Name)
                    && m_staticMethods[binder.Name].ContainsKey(args.Length)
                    && ExposedObjectHelper.InvokeBestMethod(args, null, m_staticMethods[binder.Name][args.Length], out result))
            {
                return true;
            }

            //
            // Try to call a generic instance method
            //
            if (m_staticMethods.ContainsKey(binder.Name)
                    && m_staticMethods[binder.Name].ContainsKey(args.Length))
            {
                List<MethodInfo> methods = new List<MethodInfo>();

                foreach (var method in m_genStaticMethods[binder.Name][args.Length])
                {
                    if (method.GetGenericArguments().Length == typeArgs.Length)
                    {
                        methods.Add(method.MakeGenericMethod(typeArgs));
                    }
                }

                if (ExposedObjectHelper.InvokeBestMethod(args, null, methods, out result))
                {
                    return true;
                }
            }

            result = null;
            return false;
        }
        public override bool TrySetMember(SetMemberBinder binder, object value)
        {
            var propertyInfo = m_type.GetProperty(
                binder.Name,
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);

            if (propertyInfo != null)
            {
                propertyInfo.SetValue(null, value, null);
                return true;
            }

            var fieldInfo = m_type.GetField(
                binder.Name,
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);

            if (fieldInfo != null)
            {
                fieldInfo.SetValue(null, value);
                return true;
            }

            return false;
        }

        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            var propertyInfo = m_type.GetProperty(
                binder.Name,
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);

            if (propertyInfo != null)
            {
                result = propertyInfo.GetValue(null, null);
                return true;
            }

            var fieldInfo = m_type.GetField(
                binder.Name,
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);

            if (fieldInfo != null)
            {
                result = fieldInfo.GetValue(null);
                return true;
            }

            result = null;
            return false;
        }

        public static dynamic From(Type type)
        {
            return new ExposedClass(type);
        }
    }
}
