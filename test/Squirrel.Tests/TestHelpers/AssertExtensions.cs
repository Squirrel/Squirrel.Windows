using System;
using System.Collections;
using System.Globalization;
using System.IO;
using Xunit;

namespace Squirrel.Tests.TestHelpers
{
    public static class AssertExtensions
    {
        public static void ShouldBeAboutEqualTo(this DateTimeOffset expected, DateTimeOffset current)
        {
            Assert.Equal(expected.Date, current.Date);
            Assert.Equal(expected.Offset, current.Offset);
            Assert.Equal(expected.Hour, current.Hour);
            Assert.Equal(expected.Minute, current.Minute);
            Assert.Equal(expected.Second, current.Second);
        }

        public static void ShouldBeFalse(this bool currentObject)
        {
            Assert.False(currentObject);
        }

        public static void ShouldBeNull(this object currentObject)
        {
            Assert.Null(currentObject);
        }

        public static void ShouldBeEmpty(this IEnumerable items)
        {
            Assert.Empty(items);
        }

        public static void ShouldNotBeEmpty(this IEnumerable items)
        {
            Assert.NotEmpty(items);
        }

        public static void ShouldBeTrue(this bool currentObject)
        {
            Assert.True(currentObject);
        }

        public static void ShouldEqual(this object compareFrom, object compareTo)
        {
            Assert.Equal(compareTo, compareFrom);
        }

        public static void ShouldEqual<T>(this T compareFrom, T compareTo)
        {
            Assert.Equal(compareTo, compareFrom);
        }

        public static void ShouldBeSameAs<T>(this T actual, T expected)
        {
            Assert.Same(expected, actual);
        }

        public static void ShouldNotBeSameAs<T>(this T actual, T expected)
        {
            Assert.NotSame(expected, actual);
        }

        public static void ShouldBeAssignableFrom<T>(this object instance) where T : class
        {
            Assert.IsAssignableFrom<T>(instance);
        }

        public static void ShouldBeType(this object instance, Type type)
        {
            Assert.IsType(type, instance);
        }

        public static void ShouldBeType<T>(this object instance)
        {
            Assert.IsType<T>(instance);
        }

        public static void ShouldNotBeType<T>(this object instance)
        {
            Assert.IsNotType<T>(instance);
        }

        public static void ShouldContain(this string current, string expectedSubstring, StringComparison comparison)
        {
            Assert.Contains(expectedSubstring, current, comparison);
        }

        public static void ShouldStartWith(this string current, string expectedSubstring, StringComparison comparison)
        {
            Assert.True(current.StartsWith(expectedSubstring, comparison));
        }

        public static void ShouldNotBeNull(this object currentObject)
        {
            Assert.NotNull(currentObject);
        }

        public static void ShouldNotBeNullNorEmpty(this string value)
        {
            Assert.NotNull(value);
            Assert.NotEmpty(value);
        }

        public static void ShouldNotEqual(this object compareFrom, object compareTo)
        {
            Assert.NotEqual(compareTo, compareFrom);
        }

        public static void ShouldBeGreaterThan<T>(this T current, T other) where T : IComparable
        {
            Assert.True(current.CompareTo(other) > 0, current + " is not greater than " + other);
        }

        public static void ShouldBeLessThan<T>(this T current, T other) where T : IComparable
        {
            Assert.True(current.CompareTo(other) < 0, current + " is not less than " + other);
        }

        static string ToSafeString(this char c)
        {
            if (Char.IsControl(c) || Char.IsWhiteSpace(c)) {
                switch (c) {
                case '\r':
                    return @"\r";
                case '\n':
                    return @"\n";
                case '\t':
                    return @"\t";
                case '\a':
                    return @"\a";
                case '\v':
                    return @"\v";
                case '\f':
                    return @"\f";
                default:
                    return String.Format("\\u{0:X};", (int) c);
                }
            }
            return c.ToString(CultureInfo.InvariantCulture);
        }
    }

    public enum DiffStyle
    {
        Full,
        Minimal
    }
}
