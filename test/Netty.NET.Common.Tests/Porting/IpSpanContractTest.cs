using System;
using System.Linq;
using System.Net;
using System.Reflection;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class IpSpanContractTest
{
    [Fact]
    public void NativeTextViewsReplaceSequenceAliasesAndTestOnlyExports()
    {
        MethodInfo[] methods = typeof(NetUtil).GetMethods(BindingFlags.Public | BindingFlags.Static);
        Assert.False(methods.Any(m => m.Name is "GetIPv6ByName" or "ValidIpV4ToBytes"));
        Assert.False(methods.Any(m => m.GetParameters().Any(p => p.ParameterType == typeof(ICharSequence))));
        foreach (string name in new[] { "IsValidIpV4Address", "IsValidIpV6Address", "GetByName" })
            Assert.NotNull(typeof(NetUtil).GetMethod(name, new[] { typeof(ReadOnlySpan<char>) }));
        Assert.NotNull(typeof(NetUtil).GetMethod("GetByName", new[] { typeof(ReadOnlySpan<char>), typeof(bool) }));
    }

    [Fact]
    public void StringNullArgumentsKeepNativeErrorsInsteadOfBecomingEmptyViews()
    {
        Assert.Equal("ip", Assert.Throws<ArgumentNullException>(() => NetUtil.IsValidIpV4Address((string)null)).ParamName);
        Assert.Equal("ip", Assert.Throws<ArgumentNullException>(() => NetUtil.IsValidIpV6Address((string)null)).ParamName);
        Assert.Equal("ip", Assert.Throws<ArgumentNullException>(() => NetUtil.GetByName((string)null)).ParamName);
        Assert.Equal("ip", Assert.Throws<ArgumentNullException>(() => NetUtil.GetByName((string)null, false)).ParamName);
    }

    [Theory]
    [InlineData("127.0.0.1", true, false)]
    [InlineData("001.002.003.004", true, false)]
    [InlineData("127.1", false, false)]
    [InlineData("0x7f000001", false, false)]
    [InlineData("::1", false, true)]
    [InlineData("[2001:DB8::1]", false, true)]
    [InlineData("[fe80::1%42]", false, true)]
    [InlineData("::ffff:192.0.2.1", false, true)]
    [InlineData("::ffff:111.22.", false, false)]
    [InlineData("111.22.3.", false, false)]
    [InlineData("Example.invalid", false, false)]
    [InlineData("", false, false)]
    public void BoundedSpanValidationPreservesStrictLiteralSyntax(string host, bool ipv4, bool ipv6)
    {
        ReadOnlySpan<char> view = ("prefix:" + host + ":suffix").AsSpan(7, host.Length);
        Assert.Equal(ipv4, NetUtil.IsValidIpV4Address(view));
        Assert.Equal(ipv6, NetUtil.IsValidIpV6Address(view));
        Assert.Equal(NetUtil.IsValidIpV4Address(host), NetUtil.IsValidIpV4Address(view));
        Assert.Equal(NetUtil.IsValidIpV6Address(host), NetUtil.IsValidIpV6Address(view));
    }

    [Theory]
    [InlineData("127.0.0.1", true, "00000000000000000000FFFF7F000001")]
    [InlineData("127.0.0.1", false, null)]
    [InlineData("::1", false, "00000000000000000000000000000001")]
    [InlineData("::ffff:192.0.2.1", true, "00000000000000000000FFFFC0000201")]
    [InlineData("::ffff:192.0.2.1", false, null)]
    [InlineData("2001:db8::1", true, "20010DB8000000000000000000000001")]
    [InlineData("[::1]", true, null)]
    [InlineData("fe80::1%42", true, null)]
    [InlineData("Example.invalid", true, null)]
    public void SpanMappingUsesOnlyTheSelectedInputAndOwnsTheResult(string host, bool mapped, string hex)
    {
        char[] buffer = ("x" + host + "z").ToCharArray();
        ReadOnlySpan<char> view = buffer.AsSpan(1, host.Length);
        IPAddress address = NetUtil.GetByName(view, mapped);
        IPAddress expectedFromString = NetUtil.GetByName(host, mapped);
        Assert.Equal(expectedFromString, address);
        if (hex == null)
        {
            Assert.Null(address);
            return;
        }
        Assert.NotNull(address);
        Assert.Equal(Convert.FromHexString(hex), address.GetAddressBytes());
        Array.Fill(buffer, 'x');
        Assert.Equal(Convert.FromHexString(hex), address.GetAddressBytes());
        Assert.Equal(0L, address.ScopeId);
    }

    [Fact]
    public void StackAndSequenceViewsWorkWithoutStringConversion()
    {
        Span<char> stack = stackalloc char[11];
        "192.0.2.128".AsSpan().CopyTo(stack);
        ReadOnlySpan<char> selected = stack[..11];
        Assert.True(NetUtil.IsValidIpV4Address(selected));
        Assert.False(NetUtil.IsValidIpV6Address(selected));
        IPAddress mapped = NetUtil.GetByName(selected);
        Assert.True(mapped.IsIPv4MappedToIPv6);
        Assert.Equal("192.0.2.128", mapped.MapToIPv4().ToString());

        var sequence = new StringCharSequence("x::1y", 1, 3);
        Assert.True(NetUtil.IsValidIpV6Address(sequence.AsSpan()));
        Assert.Equal(IPAddress.IPv6Loopback, NetUtil.GetByName(sequence.AsSpan()));
        Assert.False(NetUtil.IsValidIpV4Address(ReadOnlySpan<char>.Empty));
        Assert.False(NetUtil.IsValidIpV6Address(ReadOnlySpan<char>.Empty));
        Assert.Null(NetUtil.GetByName(ReadOnlySpan<char>.Empty));
    }

    [Fact]
    public void WarmSpanValidatorsDoNotAllocateTemporaryStrings()
    {
        ReadOnlySpan<char> four = "001.002.003.004";
        ReadOnlySpan<char> six = "::ffff:192.0.2.1";
        for (int i = 0; i < 1000; i++)
        {
            NetUtil.IsValidIpV4Address(four);
            NetUtil.IsValidIpV6Address(six);
        }
        int valid = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            if (NetUtil.IsValidIpV4Address(four)) valid++;
            if (NetUtil.IsValidIpV6Address(six)) valid++;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(2000, valid);
        Assert.Equal(0L, allocated);
    }
}
