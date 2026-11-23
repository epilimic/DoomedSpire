// Just enough of MSTest for the upstream test files to compile and run without any package: the attributes and the three Assert methods they use.
using System;

namespace Microsoft.VisualStudio.TestTools.UnitTesting
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class TestClassAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class TestMethodAttribute : Attribute { }

    public sealed class AssertFailedException : Exception
    {
        public AssertFailedException(string message) : base(message) { }
    }

    public static class Assert
    {
        public static void IsTrue(bool condition, string message = null)
        {
            if (!condition) throw new AssertFailedException(message ?? "Assert.IsTrue failed");
        }

        public static void IsFalse(bool condition, string message = null)
        {
            if (condition) throw new AssertFailedException(message ?? "Assert.IsFalse failed");
        }

        public static void AreEqual<T>(T expected, T actual, string message = null)
        {
            if (!Equals(expected, actual)) throw new AssertFailedException(message ?? $"Assert.AreEqual failed. Expected:<{expected}>. Actual:<{actual}>.");
        }

        public static void AreEqual(double expected, double actual, double delta, string message = null)
        {
            if (Math.Abs(expected - actual) > delta) throw new AssertFailedException(message ?? $"Assert.AreEqual failed. Expected:<{expected}> +/- <{delta}>. Actual:<{actual}>.");
        }

        public static void AreEqual(float expected, float actual, float delta, string message = null)
        {
            if (Math.Abs(expected - actual) > delta) throw new AssertFailedException(message ?? $"Assert.AreEqual failed. Expected:<{expected}> +/- <{delta}>. Actual:<{actual}>.");
        }
    }
}
