using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Netty.NET.Common.Tests.Porting;

public class DomainSnapshotContractTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryDictionaryMutationRouteIsReadOnly(bool immutable)
    {
        var mapping = immutable
            ? new DomainNameMappingBuilder<string>("default").Add("netty.io", "original").Build()
            : new DomainNameMapping<string>("default").Add("netty.io", "original");
        var view = mapping.AsMap();
        var dictionary = Assert.IsAssignableFrom<IDictionary<string, string>>(view);
        Assert.Throws<NotSupportedException>(() => dictionary["netty.io"] = "changed");
        Assert.Throws<NotSupportedException>(() => dictionary.Add("other.io", "changed"));
        Assert.Throws<NotSupportedException>(() => dictionary.Remove("netty.io"));
        Assert.Throws<NotSupportedException>(() => dictionary.Clear());
        Assert.True(dictionary.IsReadOnly);
        Assert.Equal("original", mapping.Map("netty.io"));
        Assert.Equal("original", view["netty.io"]);
    }

    [Fact]
    public void MutableOwnerKeepsTheSameLiveReadOnlyViewAndRegistrationOrder()
    {
        var owner = new DomainNameMapping<string>("default").Add("NETTY.IO", "first");
        var view = owner.AsMap();
        owner.Add("downloads.netty.io", "second").Add("netty.io", "replacement");
        Assert.Same(view, owner.AsMap());
        Assert.Equal(new[] { "netty.io", "downloads.netty.io" }, view.Keys);
        Assert.Equal("replacement", view["netty.io"]);
        Assert.Equal("replacement", owner.Map("NETTY.IO"));
        Assert.Equal("DomainNameMapping(default: default, map: {netty.io=replacement, downloads.netty.io=second})", owner.ToString());
    }

    [Fact]
    public void NormalizedCollisionsKeepFirstLookupLastViewAndBothDiagnosticEntries()
    {
        var builder = new DomainNameMappingBuilder<string>("default")
            .Add("NETTY.IO", "first").Add("netty.io", "last");
        var mapping = builder.Build();
        Assert.Equal("first", mapping.Map("NeTtY.iO"));
        Assert.Equal(1, mapping.AsMap().Count);
        Assert.Equal("last", mapping.AsMap()["netty.io"]);
        Assert.Equal("ImmutableDomainNameMapping(default: default, map: {netty.io=first, netty.io=last})", mapping.ToString());
        builder.Add("NETTY.IO", "replacement");
        Assert.Equal("first", mapping.Map("netty.io"));
        Assert.Equal("replacement", builder.Build().Map("netty.io"));
    }

    [Fact]
    public void BuildSnapshotsPreserveValueAndDefaultIdentityWithoutFollowingBuilderChanges()
    {
        var fallback = new object();
        var firstValue = new object();
        var secondValue = new object();
        var builder = new DomainNameMappingBuilder<object>(fallback).Add("*.netty.io", firstValue);
        var first = builder.Build();
        builder.Add("*.netty.io", secondValue).Add("other.io", secondValue);
        var second = builder.Build();
        Assert.Same(firstValue, first.Map("a.b.netty.io"));
        Assert.Same(fallback, first.Map("other.io"));
        Assert.Same(secondValue, second.Map("other.io"));
        Assert.Same(secondValue, second.Map("a.b.netty.io"));
        Assert.Same(fallback, first.Map(null));
        Assert.Same(fallback, second.Map(null));
        Assert.Equal(1, first.AsMap().Count);
        Assert.Equal(2, second.AsMap().Count);
        Assert.Throws<NotSupportedException>(() => first.Add(null, null));
    }

    [Theory]
    [InlineData("", "wildcard")]
    [InlineData("net", "wildcard")]
    [InlineData(".netty.io", "wildcard")]
    [InlineData("x.netty.io", "wildcard")]
    [InlineData("x.y.netty.io", "wildcard")]
    [InlineData("other", "default")]
    public void LegacyWildcardLookupRetainsItsDistinctPrefixAndDepthRules(string input, string expected)
    {
        Assert.Equal(expected, new DomainNameMapping<string>("default").Add("*.netty.io", "wildcard").Map(input));
        Assert.Equal(expected, new DomainNameMappingBuilder<string>("default").Add("*.netty.io", "wildcard").Build().Map(input));
    }

    [Fact]
    public void PublicNormalizationUsesNativeNullArgumentValidation()
    {
        Assert.Equal("hostname", Assert.Throws<ArgumentNullException>(() => DomainNameMapping<object>.NormalizeHostname(null)).ParamName);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    [InlineData("ja-JP")]
    public void HostNormalizationMatchesTheNativeIdnPolicyAndOrdinalAsciiCase(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        var policy = new IdnMapping { AllowUnassigned = true, UseStd3AsciiRules = false };
        string[] corpus = { "NETTY.IO", "", "_SERVICE.NETTY.IO", "a..b", "BÜCHER.example", "faß.de", "βόλος.example", "ｅｘａｍｐｌｅ.com", "a\u3002b", "😀.example", "a\u200cb.example", "\u00ad", "\uD800.example" };
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            foreach (string input in corpus)
            {
                string expected;
                try { expected = (input.Any(value => value > 127) ? policy.GetAscii(input) : input).ToLowerInvariant(); }
                catch (ArgumentException)
                {
                    Assert.Throws<ArgumentException>(() => DomainNameMapping<object>.NormalizeHostname(input));
                    continue;
                }
                Assert.Equal(expected, DomainNameMapping<object>.NormalizeHostname(input));
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void InternalSnapshotAndDuplicateAliasDoNotExpandThePublicApi()
    {
        var types = typeof(DomainNameMapping<>).Assembly.GetExportedTypes();
        Assert.False(types.Any(type => type.Name.StartsWith("ImmutableDomainNameMapping", StringComparison.Ordinal)));
        Assert.False(types.Any(type => type.Name.StartsWith("DomainMappingBuilder", StringComparison.Ordinal)));
        Assert.Null(typeof(DomainNameMapping<string>).GetConstructor(new[] { typeof(IDictionary<string, string>), typeof(string) }));
    }

    [Fact]
    public void FailedBuilderRegistrationCannotReplaceExistingValues()
    {
        var builder = new DomainNameMappingBuilder<string>("default").Add("netty.io", "original");
        Assert.Equal("output", Assert.Throws<ArgumentNullException>(() => builder.Add("netty.io", null)).ParamName);
        Assert.Equal("hostname", Assert.Throws<ArgumentNullException>(() => builder.Add(null, null)).ParamName);
        Assert.Equal("original", builder.Build().Map("netty.io"));
    }
}
