using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class ClrTypeInitializationContractTest
{
    [Fact]
    public void ExactTypesFromAnotherLoadContextInitializeBeforeReturningAndOnlyOnce()
    {
        WithTypes((assembly, context) =>
        {
            Type target = Target(assembly, nameof(SimpleTarget));
            Assert.Same(context, AssemblyLoadContext.GetLoadContext(target.Assembly));
            Assert.Equal(0, Count(assembly, "simple"));
            Initialize(target, target);
            Assert.Equal(1, Count(assembly, "simple"));
            Initialize(target);
            Assert.Equal(1, Count(assembly, "simple"));
        });
    }

    [Fact]
    public void ConstructedGenericTypesHaveIndependentStaticInitialization()
    {
        WithTypes((assembly, _) =>
        {
            Type definition = Target(assembly, "GenericTarget`1");
            Type integers = definition.MakeGenericType(typeof(int));
            Type strings = definition.MakeGenericType(typeof(string));
            Initialize(integers, strings, integers);
            Assert.Equal(1, Count(assembly, typeof(int).FullName));
            Assert.Equal(1, Count(assembly, typeof(string).FullName));
        });
    }

    [Fact]
    public void ConcurrentCallsCompleteTheSameInitializerOnce()
    {
        WithTypes((assembly, _) =>
        {
            Type target = Target(assembly, nameof(SimpleTarget));
            Parallel.For(0, 64, _ => Initialize(target));
            Assert.Equal(1, Count(assembly, "simple"));
        });
    }

    [Fact]
    public void InitializationFailuresPropagateWithTheirCauseAndAreNotRetried()
    {
        WithTypes((assembly, _) =>
        {
            Type target = Target(assembly, nameof(FailingTarget));
            TypeInitializationException first = Assert.Throws<TypeInitializationException>(() => Initialize(target));
            Assert.IsType<InvalidOperationException>(first.InnerException);
            Assert.Equal("initializer cause", first.InnerException.Message);
            TypeInitializationException repeated = Assert.Throws<TypeInitializationException>(() => Initialize(target));
            Assert.Same(first.InnerException, repeated.InnerException);
            Assert.Equal(1, Count(assembly, "failure"));
        });
    }

    [Fact]
    public void OpenGenericTypesFailExplicitlyInsteadOfClaimingInitialization()
    {
        WithTypes((assembly, _) => Assert.Throws<ArgumentException>(() => Initialize(Target(assembly, "GenericTarget`1"))));
    }

    [Fact]
    public void NullListsAndEntriesAreInvalidRatherThanLoadFailures()
    {
        Assert.Throws<ArgumentNullException>(() => Initialize(null));
        Assert.Throws<ArgumentNullException>(() => Initialize(new Type[] { null }));
    }

    [Fact]
    public void TypesWithoutStaticConstructorsAndEmptyListsAreValid()
    {
        Initialize(Array.Empty<Type>());
        Initialize(typeof(object), typeof(IDisposable));
    }

    private static void Initialize(params Type[] types) => ClassInitializerUtil.TryInitialize(types);

    private static Type Target(Assembly assembly, string name) =>
        assembly.GetType(typeof(ClrTypeInitializationContractTest).FullName + "+" + name, throwOnError: true);

    private static int Count(Assembly assembly, string name)
    {
        Type tracker = Target(assembly, nameof(Tracker));
        var counts = (ConcurrentDictionary<string, int>)tracker.GetField(nameof(Tracker.Counts)).GetValue(null);
        return counts.TryGetValue(name, out int value) ? value : 0;
    }

    private static void WithTypes(Action<Assembly, AssemblyLoadContext> assertion)
    {
        var context = new AssemblyLoadContext("native initialization contract", isCollectible: true);
        try { assertion(context.LoadFromAssemblyPath(typeof(ClrTypeInitializationContractTest).Assembly.Location), context); }
        finally { context.Unload(); }
    }

    public static class Tracker
    {
        public static readonly ConcurrentDictionary<string, int> Counts = new();
        public static void Record(string name) => Counts.AddOrUpdate(name, 1, (_, count) => count + 1);
    }

    private class SimpleTarget
    {
        static SimpleTarget() => Tracker.Record("simple");
    }

    private class GenericTarget<T>
    {
        static GenericTarget() => Tracker.Record(typeof(T).FullName);
    }

    private class FailingTarget
    {
        static FailingTarget()
        {
            Tracker.Record("failure");
            throw new InvalidOperationException("initializer cause");
        }
    }
}
