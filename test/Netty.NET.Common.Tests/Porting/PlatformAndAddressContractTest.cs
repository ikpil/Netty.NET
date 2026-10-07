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
        Assert.Equal(OperatingSystem.IsAndroid(), PlatformDependent.IsAndroid());
        var allocator = new NativeMemoryAllocator(8);
        using (var owner = allocator.Allocate(8, true))
        {
            Assert.Equal(0L, MemoryMarshal.Read<long>(owner.Memory.Span));
            owner.Memory.Span[0] = 0xa5;
            Assert.Equal(0xa5, owner.Memory.Span[0]);
            Assert.Equal(8L, allocator.ReservedBytes);
        }
        Assert.Equal(0L, allocator.ReservedBytes);
        Assert.Equal(OperatingSystem.IsWindows(), PlatformDependent.IsWindows());
        Assert.Equal(OperatingSystem.IsMacOS(), PlatformDependent.IsOsx());
        if (OperatingSystem.IsWindows())
        {
            Assert.True(PlatformDependent.IsWindows());
            Assert.Equal("windows", PlatformDependent.NormalizedOs());
        }
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
            Assert.Equal("x86_64", PlatformDependent.NormalizedArch());
        Assert.True(PlatformDependent.Tmpdir().Exists);
    }

    [Fact]
    public void SafePrimitiveReadsAndEqualityRetainAllBytes()
    {
        byte[] data = { 99, 0xfe, 2, 3, 4, 5, 6, 7, 0x80 };
        Assert.Equal(BitConverter.ToInt16(data, 1), MemoryMarshal.Read<short>(data.AsSpan(1, sizeof(short))));
        Assert.Equal(BitConverter.ToInt32(data, 1), MemoryMarshal.Read<int>(data.AsSpan(1, sizeof(int))));
        Assert.Equal(BitConverter.ToInt64(data, 1), MemoryMarshal.Read<long>(data.AsSpan(1, sizeof(long))));
        Assert.True(PlatformDependent.Equals(new byte[] { 1, 2 }, 0, new byte[] { 1, 2 }, 0, 2));
        Assert.False(PlatformDependent.Equals(new byte[] { 1, 2 }, 0, new byte[] { 1, 3 }, 0, 2));
    }

    [Fact]
    public void AddressParsingAcceptsCharSequencesWithoutDnsAndFormattingOmitsTheZone()
    {
        ReadOnlySpan<char> mapped = "::ffff:5.6.7.8".AsSpan();
        Assert.True(NetUtil.IsValidIpV6Address(mapped));
        Assert.Equal("::ffff:5.6.7.8", NetUtil.ToAddressString(NetUtil.GetByName(mapped), true));
        Assert.True(NetUtil.IsValidIpV4Address("127.0.0.1".AsSpan()));
        var scoped = NetUtil.CreateInetAddressFromIpAddressString("[fe80::1%42]");
        Assert.Equal(42, scoped.ScopeId);
        Assert.Equal("[fe80::1]:0", NetUtil.ToSocketAddressString(new IPEndPoint(scoped, 0)));
        Assert.Throws<ArgumentException>(() => NetUtil.Ipv4AddressToInt(IPAddress.IPv6Loopback));
        Assert.Equal(-1, "value".AsSpan(Math.Min(100, "value".Length)).IndexOf('e'));
        Assert.Equal(0, "value".AsSpan(Math.Max(0, -1)).IndexOf('v'));
        Assert.Equal("null_object", StringUtil.ClassName(null));
    }
}
