using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Netty.NET.Handler.Codec.Http3.Example;

// CLR: no Java counterpart (CLR-only, copied from examples/Netty.NET.Example/ExampleLauncher.cs). The HTTP/3 programs of
// codec-http3's test sources have one main(String[]) per class and are run as
// "java io.netty.handler.codec.http3.example.Http3ServerExample" or "java io.netty.handler.codec.http3.Http3SpecTestServer".
// C# allows several Main methods in one assembly only with an explicit startup object, so this launcher takes the Java
// class name (with or without the io.netty.handler.codec.http3 prefix) and the program's arguments:
//
//   dotnet run --project examples/Netty.NET.Handler.Codec.Http3.Examples -- example.Http3ServerExample 19000
//   dotnet run --project examples/Netty.NET.Handler.Codec.Http3.Examples -- example.Http3ClientExample -Dport=19000
//   dotnet run --project examples/Netty.NET.Handler.Codec.Http3.Examples -- Http3SpecTestServer 19002
//
// -Dname=value arguments are the Java system properties of the run: a program reads them with GetProperty, in place of
// System.getProperty and SystemPropertyUtil.get. Like JVM system properties they are not environment variables (on
// Windows the PATH and USERNAME variables would otherwise answer "path" and "userName"). The io.netty.* properties are
// also set as process environment variables, where Netty.NET.Common's SystemPropertyUtil reads the library's
// properties (io.netty.leakDetection.level and the like). The remaining arguments go to the program's Main. The Java
// package and class name are matched case-insensitively against the C# namespace (Netty.NET.Handler.Codec.Http3[.Example])
// and type name.
public static class ExampleLauncher
{
    private const string JavaPrefix = "io.netty.handler.codec.http3.";
    private const string ClrPrefix = "Netty.NET.Handler.Codec.Http3.";
    private const string LibraryPropertyPrefix = "io.netty.";

    private static readonly Dictionary<string, string> Properties = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// System.getProperty(key): the value of -Dkey=value, or null.
    /// </summary>
    public static string GetProperty(string key)
    {
        return GetProperty(key, null);
    }

    /// <summary>
    /// System.getProperty(key, def): the value of -Dkey=value, or def. user.dir, which the JVM sets to the working
    /// directory, is Environment.CurrentDirectory unless it is given.
    /// </summary>
    public static string GetProperty(string key, string def)
    {
        lock (Properties)
        {
            if (Properties.TryGetValue(key, out string value))
            {
                return value;
            }
        }

        return key == "user.dir" ? Environment.CurrentDirectory : def;
    }

    public static async Task<int> Main(string[] args)
    {
        var examples = typeof(ExampleLauncher).Assembly.GetTypes()
            .Where(type => type != typeof(ExampleLauncher) && FindMain(type) != null)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: <program class, e.g. example.Http3ServerExample> [-Dname=value ...] [arguments]");
            Console.Error.WriteLine("Programs:");

            foreach (var type in examples)
            {
                Console.Error.WriteLine("  " + JavaName(type));
            }

            return 1;
        }

        string name = args[0].StartsWith(JavaPrefix, StringComparison.Ordinal) ? args[0].Substring(JavaPrefix.Length) : args[0];
        var example = examples.FirstOrDefault(type => string.Equals(JavaName(type), name, StringComparison.OrdinalIgnoreCase));
        if (example == null)
        {
            Console.Error.WriteLine("Unknown program: " + args[0]);
            return 1;
        }

        var exampleArgs = args.Skip(1).Where(arg =>
        {
            if (!arg.StartsWith("-D", StringComparison.Ordinal))
            {
                return true;
            }

            int separator = arg.IndexOf('=');
            // "-Dssl" alone sets the property to an empty value, as the JVM does.
            string key = separator < 0 ? arg.Substring(2) : arg.Substring(2, separator - 2);
            string value = separator < 0 ? string.Empty : arg.Substring(separator + 1);

            lock (Properties)
            {
                Properties[key] = value;
            }

            if (key.StartsWith(LibraryPropertyPrefix, StringComparison.Ordinal))
            {
                Environment.SetEnvironmentVariable(key, value);
            }

            return false;
        }).ToArray();

        MethodInfo main = FindMain(example);
        object result = main.GetParameters().Length == 0 ? main.Invoke(null, null) : main.Invoke(null, new object[] { exampleArgs });
        if (result is Task task)
        {
            await task;
        }

        return 0;
    }

    private static MethodInfo FindMain(Type type)
    {
        return type.GetMethod("Main", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string[]) }, null);
    }

    // Netty.NET.Handler.Codec.Http3.Example.Http3ServerExample -> Example.Http3ServerExample and
    // Netty.NET.Handler.Codec.Http3.Http3SpecTestServer -> Http3SpecTestServer (compared case-insensitively).
    private static string JavaName(Type type)
    {
        string fullName = type.FullName ?? type.Name;
        return fullName.StartsWith(ClrPrefix, StringComparison.Ordinal) ? fullName.Substring(ClrPrefix.Length) : fullName;
    }
}
