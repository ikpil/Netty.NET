using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Netty.NET.Common.Tests.Porting;

public class IpLiteralViewContractTest
{
    [Fact]
    public void CreationApisOfferNativeViewsAndKeepStringCalls()
    {
        foreach (string name in new[] { "CreateByteArrayFromIpAddressString", "CreateInetAddressFromIpAddressString" })
        {
            Assert.NotNull(typeof(NetUtil).GetMethod(name, new[] { typeof(ReadOnlySpan<char>) }));
            Assert.NotNull(typeof(NetUtil).GetMethod(name, new[] { typeof(string) }));
        }
    }

    [Fact]
    public void StringNullErrorsIdentifyTheActualCreationArgument()
    {
        Assert.Equal("ipAddressString", Assert.Throws<ArgumentNullException>(() =>
            NetUtil.CreateByteArrayFromIpAddressString((string)null)).ParamName);
        Assert.Equal("ipAddressString", Assert.Throws<ArgumentNullException>(() =>
            NetUtil.CreateInetAddressFromIpAddressString((string)null)).ParamName);
    }

    [Fact]
    public void WarmBracketRemovalAddsNoTemporaryStringAllocation()
    {
        static (long Allocated, long Sum) Measure(string host)
        {
            for (int i = 0; i < 1000; i++)
            {
                NetUtil.CreateByteArrayFromIpAddressString(host);
                NetUtil.CreateInetAddressFromIpAddressString(host);
            }
            long sum = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                sum += NetUtil.CreateByteArrayFromIpAddressString(host)[15];
                sum += NetUtil.CreateInetAddressFromIpAddressString(host).ScopeId;
            }
            return (GC.GetAllocatedBytesForCurrentThread() - before, sum);
        }
        var bare = Measure("fe80::1%42");
        var bracketed = Measure("[fe80::1%42]");
        Assert.Equal(43000L, bare.Sum);
        Assert.Equal(bare.Sum, bracketed.Sum);
        Assert.True(bracketed.Allocated <= bare.Allocated);
    }

    [Theory]
    [InlineData("127.0.0.1", "7F000001", 0L)]
    [InlineData("001.002.003.004", "01020304", 0L)]
    [InlineData("::1", "00000000000000000000000000000001", 0L)]
    [InlineData("[fe80::1%42]", "FE800000000000000000000000000001", 42L)]
    [InlineData("[fe80::1%+42]", "FE800000000000000000000000000001", 42L)]
    [InlineData("fe80::1%-0", "FE800000000000000000000000000001", 0L)]
    [InlineData("fe80::1%4294967295", "FE800000000000000000000000000001", 4294967295L)]
    [InlineData("fe80::1%4294967296", "FE800000000000000000000000000001", -1L)]
    [InlineData("fe80::1%eth0", "FE800000000000000000000000000001", -1L)]
    [InlineData("fe80::1%", "FE800000000000000000000000000001", -1L)]
    [InlineData("fe80::1%42\0", "FE800000000000000000000000000001", -1L)]
    [InlineData("[::ffff:192.0.2.1]", "00000000000000000000FFFFC0000201", 0L)]
    [InlineData("fe80::1%-1", "FE800000000000000000000000000001", -1L)]
    public void BoundedViewsPreserveWireBytesAndNativeScopePolicy(string host, string hex, long scope)
    {
        char[] buffer = ("x" + host + "z").ToCharArray();
        ReadOnlySpan<char> selected = buffer.AsSpan(1, host.Length);
        byte[] expected = Convert.FromHexString(hex);
        byte[] bytes = NetUtil.CreateByteArrayFromIpAddressString(selected);
        IPAddress address = NetUtil.CreateInetAddressFromIpAddressString(selected);
        Assert.Equal(expected, bytes);
        Assert.Equal(NetUtil.CreateByteArrayFromIpAddressString(host), bytes);
        Assert.Equal(NetUtil.CreateInetAddressFromIpAddressString(host), address);
        Array.Fill(buffer, 'x');
        Assert.Equal(expected, bytes);
        if (scope < 0)
        {
            Assert.Null(address);
            return;
        }
        Assert.NotNull(address);
        Assert.Equal(expected, address.GetAddressBytes());
        Assert.Equal(expected.Length == 4 ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6, address.AddressFamily);
        if (expected.Length == 16) Assert.Equal(scope, address.ScopeId);
        Array.Fill(bytes, (byte)0xCC);
        Assert.Equal(expected, address.GetAddressBytes());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("127.1")]
    [InlineData("::ffff:111.22.")]
    [InlineData("111.22.3.")]
    [InlineData("[::1")]
    [InlineData("Example.invalid")]
    public void InvalidViewsReturnNullWithoutResolution(string host)
    {
        ReadOnlySpan<char> selected = ("x" + host + "z").AsSpan(1, host.Length);
        Assert.Null(NetUtil.CreateByteArrayFromIpAddressString(selected));
        Assert.Null(NetUtil.CreateInetAddressFromIpAddressString(selected));
    }

    [Fact]
    public void StackViewsAndRepeatedResultsHaveIndependentOwnership()
    {
        Span<char> stack = stackalloc char[15];
        "[fe80::1%00042]".AsSpan().CopyTo(stack);
        ReadOnlySpan<char> selected = stack;
        byte[] first = NetUtil.CreateByteArrayFromIpAddressString(selected);
        byte[] second = NetUtil.CreateByteArrayFromIpAddressString(selected);
        IPAddress address = NetUtil.CreateInetAddressFromIpAddressString(selected);
        IPAddress other = NetUtil.CreateInetAddressFromIpAddressString(selected);
        Assert.NotSame(first, second);
        Assert.NotSame(address, other);
        stack.Clear();
        first[15] = 99;
        address.ScopeId = 1;
        Assert.Equal((byte)1, second[15]);
        Assert.Equal((byte)1, other.GetAddressBytes()[15]);
        Assert.Equal(42L, other.ScopeId);
    }

    [Fact]
    public void DefaultViewsAreEmptyParseFailures()
    {
        Assert.Null(NetUtil.CreateByteArrayFromIpAddressString(default(ReadOnlySpan<char>)));
        Assert.Null(NetUtil.CreateInetAddressFromIpAddressString(default(ReadOnlySpan<char>)));
    }

    [Fact]
    public void ScopedViewsKeepInvariantSignsAndAsciiDigits()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.PositiveSign = "POS";
        try
        {
            CultureInfo.CurrentCulture = culture;
            Assert.Equal(42L, NetUtil.CreateInetAddressFromIpAddressString("[fe80::1%+42]".AsSpan()).ScopeId);
            foreach (string zone in new[] { "POS42", " 42", "42 ", "\u0664\u0662", "42\0" })
            {
                ReadOnlySpan<char> selected = ("[fe80::1%" + zone + "]").AsSpan();
                Assert.Null(NetUtil.CreateInetAddressFromIpAddressString(selected));
                Assert.Equal(IPAddress.Parse("fe80::1").GetAddressBytes(), NetUtil.CreateByteArrayFromIpAddressString(selected));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
