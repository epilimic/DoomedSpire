// Runs every [TestMethod] in this assembly and prints PASS / FAIL / SKIP. Exit code 1 if anything failed (skips do not count).
// A test is SKIPPED when it needs a WAD file that is not available (the commercial ones: see WadPath.cs).
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ManagedDoomTest
{
    public static class Program
    {
        /// <summary>The name of the missing WAD if this exception is "file not found" for one of the WadPath.MISSING-... paths, else null.</summary>
        private static string MissingWad(Exception e)
        {
            if (e is FileNotFoundException fnf)
            {
                var text = (fnf.FileName ?? "") + " " + fnf.Message;
                var i = text.IndexOf("MISSING-", StringComparison.Ordinal);
                if (i >= 0) return text.Substring(i + 8).Split(' ', '\'', '"')[0];
            }
            return null;
        }

        public static int Main(string[] args)
        {
            // The tests open "data/..." relative to this project's folder, wherever dotnet was started from.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Engine.Tests.csproj"))) dir = dir.Parent;
            if (dir != null) Directory.SetCurrentDirectory(dir.FullName);

            // The engine prints a few lines to the console every time it loads a level; keep only our own lines.
            var output = Console.Out;
            Console.SetOut(TextWriter.Null);
            var filter = args.FirstOrDefault(a => !a.StartsWith("--"));
            var verbose = args.Contains("--verbose");
            int pass = 0, fail = 0, skip = 0;
            var sw = Stopwatch.StartNew();

            var classes = Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => t.GetCustomAttribute<TestClassAttribute>() != null)
                .OrderBy(t => t.FullName);
            foreach (var type in classes)
            {
                var instance = Activator.CreateInstance(type);
                foreach (var method in type.GetMethods().Where(m => m.GetCustomAttribute<TestMethodAttribute>() != null).OrderBy(m => m.Name))
                {
                    var name = $"{type.Name}.{method.Name}";
                    if (filter != null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        method.Invoke(instance, null);
                        pass++;
                        if (verbose) output.WriteLine($"PASS {name}");
                    }
                    catch (TargetInvocationException tie) when (MissingWad(tie.InnerException) != null)
                    {
                        skip++;
                        output.WriteLine($"SKIP {name} (needs a WAD that is not available: {MissingWad(tie.InnerException)})");
                    }
                    catch (Exception e)
                    {
                        fail++;
                        var inner = e is TargetInvocationException t && t.InnerException != null ? t.InnerException : e;
                        output.WriteLine($"FAIL {name}: {inner.GetType().Name}: {inner.Message}");
                    }
                }
            }
            output.WriteLine($"\n{pass} passed, {fail} failed, {skip} skipped in {sw.Elapsed.TotalSeconds:0.0}s");
            return fail == 0 ? 0 : 1;
        }
    }
}
