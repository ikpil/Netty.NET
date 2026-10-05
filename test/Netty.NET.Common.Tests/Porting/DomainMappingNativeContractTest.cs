using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Netty.NET.Common.Tests.Porting;

public class DomainMappingNativeContractTest
{
    [Fact]
    public void NormalizedReplacementKeepsOriginalRegistrationOrder()
    {
        var builder = new DomainWildcardMappingBuilder<string>("default")
            .Add("*.NETTY.IO", "old")
            .Add("downloads.netty.io", "exact")
            .Add("*.netty.io", "new");
        Func<string, string> mapping = builder.Build();
        Assert.Equal("new", mapping("other.netty.io"));
        Assert.Equal("exact", mapping("DOWNLOADS.NETTY.IO"));
        Assert.Equal("ImmutableDomainWildcardMapping(default: default, map: {*.netty.io=new, downloads.netty.io=exact})",
            mapping.Target.ToString());
    }

    [Fact]
    public void EachBuildOwnsAnIndependentSnapshotWithSharedValueIdentity()
    {
        var fallback = new object();
        var firstValue = new object();
        var secondValue = new object();
        var builder = new DomainWildcardMappingBuilder<object>(fallback).Add("*.netty.io", firstValue);
        var first = builder.Build();
        builder.Add("*.netty.io", secondValue).Add("netty.io", secondValue);
        var second = builder.Build();
        Assert.Same(firstValue, first("a.netty.io"));
        Assert.Same(fallback, first("netty.io"));
        Assert.Same(secondValue, second("a.netty.io"));
        Assert.Same(secondValue, second("netty.io"));
        Assert.Same(fallback, first(null));
        Assert.Same(fallback, second("unregistered"));
        Assert.NotSame(first.Target, second.Target);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".netty.io")]
    [InlineData("*")]
    [InlineData("*.")]
    [InlineData("*netty.io")]
    public void InvalidRegistrationCannotChangeExistingEntries(string hostname)
    {
        var builder = new DomainWildcardMappingBuilder<string>("default").Add("netty.io", "existing");
        Assert.Throws<ArgumentException>(() => builder.Add(hostname, "bad"));
        Assert.Throws<ArgumentException>(() => builder.Add(hostname, null));
        Assert.Equal("existing", builder.Build()("netty.io"));
        Assert.Equal("ImmutableDomainWildcardMapping(default: default, map: {netty.io=existing})", builder.Build().Target.ToString());
    }

    [Fact]
    public void NullOutputCannotReplaceAnExistingNormalizedEntry()
    {
        var builder = new DomainWildcardMappingBuilder<string>("default").Add("NETTY.IO", "existing");
        Assert.Equal("output", Assert.Throws<ArgumentNullException>(() => builder.Add("netty.io", null)).ParamName);
        Assert.Equal("hostname", Assert.Throws<ArgumentNullException>(() => builder.Add(null, null)).ParamName);
        Assert.Equal("existing", builder.Build()("netty.io"));
    }

    [Theory]
    [InlineData("netty.io", "default")]
    [InlineData("a.netty.io", "wildcard")]
    [InlineData("a.b.netty.io", "default")]
    [InlineData(".netty.io", "wildcard")]
    [InlineData("a.netty.io.", "default")]
    [InlineData("NETTY.IO", "default")]
    public void WildcardsRetainThePinnedFirstDotRule(string input, string expected)
    {
        var mapping = new DomainWildcardMappingBuilder<string>("default").Add("*.netty.io", "wildcard").Build();
        Assert.Equal(expected, mapping(input));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    [InlineData("ja-JP")]
    public void NativeIdnNormalizationIsIndependentOfCallerCulture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var mapping = new DomainWildcardMappingBuilder<string>("default")
                .Add("*.BÜCHER.example", "wildcard")
                .Add("I.BÜCHER.EXAMPLE", "exact")
                .Add("xn--bcher-kva.example", "root")
                .Build();
            Assert.Equal("exact", mapping("i.xn--bcher-kva.example"));
            Assert.Equal("wildcard", mapping("other.bücher.example"));
            Assert.Equal("root", mapping("BÜCHER.EXAMPLE"));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void EmptySnapshotHasACompleteDiagnosticAndReturnsTheSameDefault()
    {
        var fallback = new object();
        var mapping = new DomainWildcardMappingBuilder<object>(0, fallback).Build();
        Assert.Same(fallback, mapping(null));
        Assert.Same(fallback, mapping(""));
        Assert.Same(fallback, mapping("netty.io"));
        Assert.Equal("ImmutableDomainWildcardMapping(default: " + fallback + ", map: {})", mapping.Target.ToString());
    }

    [Fact]
    public void NativeCallbacksAcceptCapturedStateVarianceAndPreserveFailureIdentity()
    {
        int calls = 0;
        int caller = Thread.CurrentThread.ManagedThreadId;
        var failure = new InvalidOperationException("selection failure");
        Func<object, string> source = input =>
        {
            Assert.Equal(caller, Thread.CurrentThread.ManagedThreadId);
            calls++;
            if (input == null) throw failure;
            return input.ToString();
        };
        Func<string, object> selection = source;
        Assert.Equal("netty.io", selection("netty.io"));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => selection(null)));
        Assert.Equal(2, calls);
        Func<string, object> domainSelection = new DomainNameMapping<object>(failure).Map;
        Assert.Same(failure, domainSelection("unregistered"));
    }

    [Fact]
    public async Task PublishedSnapshotSupportsConcurrentReadsDuringFurtherRegistration()
    {
        var oldValue = new object();
        var newValue = new object();
        var builder = new DomainWildcardMappingBuilder<object>(newValue).Add("*.netty.io", oldValue);
        var snapshot = builder.Build();
        var reads = Enumerable.Range(0, 32).Select(index => Task.Run(() =>
        {
            for (int attempt = 0; attempt < 100; attempt++) Assert.Same(oldValue, snapshot("a.netty.io"));
        })).ToArray();
        for (int index = 0; index < 100; index++) builder.Add("*.netty.io", newValue);
        await Task.WhenAll(reads);
        Assert.Same(newValue, builder.Build()("a.netty.io"));
    }

    [Fact]
    public void PublicBoundaryUsesFuncAndDoesNotExportTheSnapshotImplementation()
    {
        Assert.Equal(typeof(Func<string, object>), typeof(DomainWildcardMappingBuilder<object>).GetMethod("Build").ReturnType);
        var exported = typeof(DomainWildcardMappingBuilder<>).Assembly.GetExportedTypes();
        Assert.False(exported.Any(type => type.Name.StartsWith("IMapping", StringComparison.Ordinal)));
        Assert.False(exported.Any(type => type.Name.StartsWith("ImmutableDomainWildcardMapping", StringComparison.Ordinal)));
    }
}
