using System;
using System.IO;
using Xunit;

namespace Netty.NET.Common.Tests.Porting;

[Collection("Leak detector globals")]
public class ResourceLeakDetectorFactoryContractTest
{
    private const string Property = "io.netty.customResourceLeakDetector";
    private static DefaultResourceLeakDetectorFactory createFactory(Type type) => createFactory(type?.AssemblyQualifiedName);
    private static DefaultResourceLeakDetectorFactory createFactory(string typeName)
    {
        string previous = Environment.GetEnvironmentVariable(Property);
        try
        {
            Environment.SetEnvironmentVariable(Property, typeName);
            return new DefaultResourceLeakDetectorFactory();
        }
        finally { Environment.SetEnvironmentVariable(Property, previous); }
    }

    public class Custom<T> : ResourceLeakDetector<T> where T : class
    {
        public readonly Type Resource;
        public readonly int Sampling;
        public readonly long? MaxActive;
        public Custom(Type resource, int sampling) : base(resource, sampling) { Resource = resource; Sampling = sampling; }
        public Custom(Type resource, int sampling, long maxActive) : base(resource, sampling, maxActive) { Resource = resource; Sampling = sampling; MaxActive = maxActive; }
    }
    public sealed class ClosedCustom : Custom<object>
    {
        public ClosedCustom(Type resource, int sampling) : base(resource, sampling) { }
        public ClosedCustom(Type resource, int sampling, long maxActive) : base(resource, sampling, maxActive) { }
    }
    public sealed class ModernOnly<T> : ResourceLeakDetector<T> where T : class
    {
        public ModernOnly(Type resource, int sampling) : base(resource, sampling) { }
    }
    public sealed class LegacyOnly<T> : ResourceLeakDetector<T> where T : class
    {
        public LegacyOnly(Type resource, int sampling, long maxActive) : base(resource, sampling, maxActive) { }
    }
    public sealed class Throwing<T> : ResourceLeakDetector<T> where T : class
    {
        public Throwing(Type resource, int sampling) : base(resource, sampling) => throw new InvalidOperationException("custom constructor");
        public Throwing(Type resource, int sampling, long maxActive) : base(resource, sampling, maxActive) => throw new InvalidOperationException("custom constructor");
    }
    public sealed class StreamOnly<T> : ResourceLeakDetector<T> where T : Stream
    {
        public StreamOnly(Type resource, int sampling) : base(resource, sampling) { }
    }
    public sealed class InvalidArity<T, U> : ResourceLeakDetector<T> where T : class
    {
        public InvalidArity(Type resource, int sampling) : base(resource, sampling) { }
    }
    public sealed class InitializationFailure : ResourceLeakDetector<object>
    {
        static InitializationFailure() => throw new InvalidOperationException("custom initializer");
        public InitializationFailure(Type resource, int sampling) : base(resource, sampling) { }
    }

    [Fact]
    public void OpenGenericProviderClosesForEachClrResourceTypeAndPreservesConstructorArguments()
    {
        var factory = createFactory(typeof(Custom<>));
        var modern = Assert.IsType<Custom<object>>(factory.newResourceLeakDetector<object>(typeof(object), 7));
        Assert.Same(typeof(object), modern.Resource);
        Assert.Equal(7, modern.Sampling);
        Assert.Null(modern.MaxActive);
        var legacy = Assert.IsType<Custom<string>>(factory.newResourceLeakDetector<string>(typeof(string), 13, 123));
        Assert.Same(typeof(string), legacy.Resource);
        Assert.Equal(13, legacy.Sampling);
        Assert.Equal(123, legacy.MaxActive);
        var defaults = Assert.IsType<Custom<object>>(factory.newResourceLeakDetector<object>(typeof(object)));
        Assert.Equal(ResourceLeakDetector.SAMPLING_INTERVAL, defaults.Sampling);
    }

    [Fact]
    public void ClosedProviderUsesMatchingGenericTypeAndFallsBackForIncompatibleTypes()
    {
        var factory = createFactory(typeof(ClosedCustom));
        Assert.IsType<ClosedCustom>(factory.newResourceLeakDetector<object>(typeof(object), 1));
        Assert.IsType<ClosedCustom>(factory.newResourceLeakDetector<object>(typeof(object), 1, 0));
        Assert.IsType<ResourceLeakDetector<string>>(factory.newResourceLeakDetector<string>(typeof(string), 1));
    }

    [Fact]
    public void ModernAndDeprecatedConstructorsAreIndependent()
    {
        var modern = createFactory(typeof(ModernOnly<>));
        Assert.IsType<ModernOnly<object>>(modern.newResourceLeakDetector<object>(typeof(object), 1));
        Assert.IsType<ResourceLeakDetector<object>>(modern.newResourceLeakDetector<object>(typeof(object), 1, 0));
        var legacy = createFactory(typeof(LegacyOnly<>));
        Assert.IsType<ResourceLeakDetector<object>>(legacy.newResourceLeakDetector<object>(typeof(object), 1));
        Assert.IsType<LegacyOnly<object>>(legacy.newResourceLeakDetector<object>(typeof(object), 1, 0));
    }

    [Fact]
    public void CustomConstructorFailuresFallBackForBothEntryPoints()
    {
        var factory = createFactory(typeof(Throwing<>));
        Assert.IsType<ResourceLeakDetector<object>>(factory.newResourceLeakDetector<object>(typeof(object), 1));
        Assert.IsType<ResourceLeakDetector<object>>(factory.newResourceLeakDetector<object>(typeof(object), 1, 0));
    }

    [Fact]
    public void GenericConstraintsAreCheckedPerRequestedType()
    {
        var factory = createFactory(typeof(StreamOnly<>));
        Assert.IsType<ResourceLeakDetector<object>>(factory.newResourceLeakDetector<object>(typeof(object), 1));
        Assert.IsType<StreamOnly<MemoryStream>>(factory.newResourceLeakDetector<MemoryStream>(typeof(MemoryStream), 1));
    }

    [Fact]
    public void MissingUnrelatedInvalidArityAndInitializationFailuresFallBack()
    {
        foreach (var type in new[] { typeof(object), typeof(InvalidArity<,>), typeof(InitializationFailure) })
            Assert.IsType<ResourceLeakDetector<object>>(createFactory(type).newResourceLeakDetector<object>(typeof(object), 1));
        Assert.IsType<ResourceLeakDetector<object>>(createFactory("Missing.Detector, Missing.Assembly").newResourceLeakDetector<object>(typeof(object), 1));
        Assert.IsType<ResourceLeakDetector<object>>(createFactory((string)null).newResourceLeakDetector<object>(typeof(object), 1));
    }

    private sealed class LegacyFactory : ResourceLeakDetectorFactory
    {
        internal Type Resource;
        internal int Sampling;
        internal long MaxActive;
        public override ResourceLeakDetector<T> newResourceLeakDetector<T>(Type resource, int sampling, long maxActive)
        {
            Resource = resource; Sampling = sampling; MaxActive = maxActive;
            return new ResourceLeakDetector<T>(resource, sampling, maxActive);
        }
    }
    [Fact]
    public void BaseAdapterValidatesSamplingAndDelegatesToLegacyFactory()
    {
        var factory = new LegacyFactory();
        factory.newResourceLeakDetector<object>(typeof(object), 3);
        Assert.Same(typeof(object), factory.Resource);
        Assert.Equal(3, factory.Sampling);
        Assert.Equal(long.MaxValue, factory.MaxActive);
        Assert.Throws<ArgumentException>(() => factory.newResourceLeakDetector<object>(typeof(object), 0));
        Assert.Throws<ArgumentException>(() => factory.newResourceLeakDetector<object>(typeof(object), -1));
        // The default override deliberately does not use the base adapter's validation.
        var defaults = createFactory((string)null);
        Assert.IsType<ResourceLeakDetector<object>>(defaults.newResourceLeakDetector<object>(typeof(object), 0));
    }

    [Fact]
    public void SingletonReplacementRejectsNullAndIsRestoredAfterUse()
    {
        var previous = ResourceLeakDetectorFactory.instance();
        var replacement = new LegacyFactory();
        try
        {
            ResourceLeakDetectorFactory.setResourceLeakDetectorFactory(replacement);
            Assert.Same(replacement, ResourceLeakDetectorFactory.instance());
            Assert.Throws<ArgumentNullException>(() => ResourceLeakDetectorFactory.setResourceLeakDetectorFactory(null));
            Assert.Same(replacement, ResourceLeakDetectorFactory.instance());
        }
        finally { ResourceLeakDetectorFactory.setResourceLeakDetectorFactory(previous); }
    }
}
