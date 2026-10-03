using System;
using System.Net;
using System.Runtime.InteropServices;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class PlatformAndAddressContractTest
{
    [Fact]
    public void ClrProbesReportNativeCapabilities()
    {
        Assert.False(PlatformDependent.hasUnsafe());
        Assert.NotNull(PlatformDependent.getUnsafeUnavailabilityCause());
        Assert.Equal(IntPtr.Size, PlatformDependent.addressSize());
        Assert.False(PlatformDependent0.isVirtualThread(System.Threading.Thread.CurrentThread));
        if (!SystemPropertyUtil.contains("os.name") && OperatingSystem.IsWindows())
        {
            Assert.True(PlatformDependent.isWindows());
            Assert.Equal("windows", PlatformDependent.normalizedOs());
        }
        if (!SystemPropertyUtil.contains("os.arch") && RuntimeInformation.ProcessArchitecture == Architecture.X64)
            Assert.Equal("x86_64", PlatformDependent.normalizedArch());
        Assert.True(PlatformDependent.tmpdir().Exists);
    }

    [Fact]
    public void SafePrimitiveReadsAndEqualityRetainAllBytes()
    {
        byte[] data = { 99, 0xfe, 2, 3, 4, 5, 6, 7, 0x80 };
        Assert.Equal(BitConverter.ToInt16(data, 1), PlatformDependent.getShort(data, 1));
        Assert.Equal(BitConverter.ToInt32(data, 1), PlatformDependent.getInt(data, 1));
        Assert.Equal(BitConverter.ToInt64(data, 1), PlatformDependent.getLong(data, 1));
        Assert.True(PlatformDependent.equals(new byte[] { 1, 2 }, 0, new byte[] { 1, 2 }, 0, 2));
        Assert.False(PlatformDependent.equals(new byte[] { 1, 2 }, 0, new byte[] { 1, 3 }, 0, 2));
    }

    [Fact]
    public void AddressParsingAcceptsCharSequencesWithoutDnsAndFormattingOmitsTheZone()
    {
        ICharSequence mapped = new StringCharSequence("::ffff:5.6.7.8");
        Assert.True(NetUtil.isValidIpV6Address(mapped));
        Assert.Equal("::ffff:5.6.7.8", NetUtil.toAddressString(NetUtil.getByName(mapped), true));
        Assert.True(NetUtil.isValidIpV4Address(new StringCharSequence("127.0.0.1")));
        var scoped = NetUtil.createInetAddressFromIpAddressString("[fe80::1%42]");
        Assert.Equal(42, scoped.ScopeId);
        Assert.Equal("[fe80::1]:0", NetUtil.toSocketAddressString(new IPEndPoint(scoped, 0)));
        Assert.Throws<ArgumentException>(() => NetUtil.ipv4AddressToInt(IPAddress.IPv6Loopback));
        Assert.Equal(-1, "value".AsSpan(Math.Min(100, "value".Length)).IndexOf('e'));
        Assert.Equal(0, "value".AsSpan(Math.Max(0, -1)).IndexOf('v'));
        Assert.Equal("null_object", StringUtil.className(null));
    }
}
