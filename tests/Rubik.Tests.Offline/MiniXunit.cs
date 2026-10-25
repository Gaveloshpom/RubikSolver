using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Xunit
{
    // Minimal stand-in for the xunit API (Fact/Theory/InlineData/Assert), enough for these tests.
    // Used only because nuget.org was not reachable in the container where the tests were written.
    // On a machine with NuGet access just reference the real xunit: the test sources
    // stay the same (this is the same Xunit namespace).

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class FactAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TheoryAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class InlineDataAttribute : Attribute
    {
        public object[] Data { get; }
        public InlineDataAttribute(params object[] data) => Data = data;
    }

    public class XunitAssertException : Exception
    {
        public XunitAssertException(string message) : base(message) { }
    }

    public static class Assert
    {
        public static void True(bool condition, string message = null) =>
            Check(condition, message ?? "Expected true but was false.");

        public static void False(bool condition, string message = null) =>
            Check(!condition, message ?? "Expected false but was true.");

        public static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new XunitAssertException($"Expected: {expected}\nActual:   {actual}");
        }

        public static void Equal(string expected, string actual)
        {
            if (expected != actual)
                throw new XunitAssertException($"Expected: {Escape(expected)}\nActual:   {Escape(actual)}");
        }

        public static TException Throws<TException>(Action action) where TException : Exception
        {
            try { action(); }
            catch (TException ex) { return ex; }
            catch (Exception ex)
            {
                throw new XunitAssertException($"Expected {typeof(TException).Name} but got {ex.GetType().Name}: {ex.Message}");
            }
            throw new XunitAssertException($"Expected {typeof(TException).Name} but no exception was thrown.");
        }

        private static void Check(bool ok, string message)
        {
            if (!ok) throw new XunitAssertException(message);
        }

        private static string Escape(string s) => s?.Replace("\n", "\\n") ?? "<null>";
    }

    /// <summary>Finds [Fact]/[Theory] in the assembly via reflection, runs them and prints a summary.</summary>
    public static class MiniRunner
    {
        public static int RunAll(Assembly asm)
        {
            int passed = 0, failed = 0;
            var failures = new List<string>();

            foreach (var type in asm.GetTypes().Where(t => t.IsClass && !t.IsAbstract))
            {
                object instance = null;
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    var fact = method.GetCustomAttribute<FactAttribute>();
                    var theory = method.GetCustomAttribute<TheoryAttribute>();
                    if (fact == null && theory == null) continue;

                    instance ??= Activator.CreateInstance(type);

                    var cases = theory == null
                        ? new List<object[]> { Array.Empty<object>() }
                        : method.GetCustomAttributes<InlineDataAttribute>().Select(a => a.Data).ToList();

                    foreach (var args in cases)
                    {
                        string name = $"{type.Name}.{method.Name}({string.Join(", ", args)})";
                        try
                        {
                            method.Invoke(instance, args);
                            passed++;
                        }
                        catch (TargetInvocationException tie)
                        {
                            failed++;
                            failures.Add($"{name}\n    {tie.InnerException?.Message}");
                        }
                    }
                }
            }

            Console.WriteLine($"\nPassed: {passed}, Failed: {failed}\n");
            if (failures.Count > 0)
            {
                Console.WriteLine("Failures:");
                foreach (var f in failures) Console.WriteLine("  " + f);
            }
            return failed == 0 ? 0 : 1;
        }
    }
}
