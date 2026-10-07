using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Net;

namespace Netty.NET.Common.Tests.Porting;

public class IpByteViewContractTest
{
    [Fact]
    public void NullArraysAndAddressesHaveNativeArgumentErrors()
    {
        Assert.Equal("ipAddress", Assert.Throws<ArgumentNullException>(() => NetUtil.Ipv4AddressToInt(null)).ParamName);
        Assert.Equal("ip", Assert.Throws<ArgumentNullException>(() => NetUtil.ToAddressString(null)).ParamName);
        Assert.Equal("ip", Assert.Throws<ArgumentNullException>(() => NetUtil.ToAddressString(null, true)).ParamName);
        Assert.Equal("bytes", Assert.Throws<ArgumentNullException>(() => NetUtil.BytesToIpAddress((byte[])null)).ParamName);
        Assert.Equal("bytes", Assert.Throws<ArgumentNullException>(() => NetUtil.BytesToIpAddress(null, 0, 4)).ParamName);
    }

    [Fact]
    public void ArraySlicesUseNativeBoundsAndKeepTheLengthContract()
    {
        byte[] bytes = new byte[16];
        Assert.Throws<ArgumentOutOfRangeException>(() => NetUtil.BytesToIpAddress(bytes, -1, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => NetUtil.BytesToIpAddress(bytes, 13, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => NetUtil.BytesToIpAddress(bytes, int.MaxValue, 16));
        Assert.Equal("length", Assert.Throws<ArgumentException>(() => NetUtil.BytesToIpAddress(bytes, 0, 5)).ParamName);
        Assert.Equal("length", Assert.Throws<ArgumentException>(() => NetUtil.BytesToIpAddress(bytes, 0, -1)).ParamName);
    }

    [Theory]
    [InlineData("0.0.0.0", 0)]
    [InlineData("1.2.3.4", 16909060)]
    [InlineData("127.255.255.255", int.MaxValue)]
    [InlineData("128.0.0.0", int.MinValue)]
    [InlineData("192.168.1.1", -1062731519)]
    [InlineData("255.255.255.255", -1)]
    public void IntegerBitsMatchNetworkOrderAndKeepTheSignedDomain(string host, int expected)
    {
        IPAddress address = IPAddress.Parse(host);
        Assert.Equal(expected, NetUtil.Ipv4AddressToInt(address));
        Assert.Equal(host, NetUtil.IntToIpAddress(expected));
        Assert.Equal(expected, NetUtil.Ipv4AddressToInt(IPAddress.Parse(NetUtil.IntToIpAddress(expected))));
        Assert.Throws<ArgumentException>(() => NetUtil.Ipv4AddressToInt(IPAddress.IPv6Loopback));
        Assert.Throws<ArgumentException>(() => NetUtil.Ipv4AddressToInt(address.MapToIPv6()));
    }

    [Fact]
    public void WarmIntegerExtractionDoesNotAllocateAddressArrays()
    {
        IPAddress address = IPAddress.Parse("192.168.1.1");
        for (int i = 0; i < 1000; i++) NetUtil.Ipv4AddressToInt(address);
        long sum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) sum += NetUtil.Ipv4AddressToInt(address);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(-1062731519000L, sum);
        Assert.Equal(0L, allocated);
    }

    [Fact]
    public void NativeByteViewApiIsAvailableAlongsideExistingArraySignatures()
    {
        Assert.NotNull(typeof(NetUtil).GetMethod("BytesToIpAddress", new[] { typeof(ReadOnlySpan<byte>) }));
        Assert.NotNull(typeof(NetUtil).GetMethod("BytesToIpAddress", new[] { typeof(byte[]) }));
        Assert.NotNull(typeof(NetUtil).GetMethod("BytesToIpAddress", new[] { typeof(byte[]), typeof(int), typeof(int) }));
    }

    [Theory]
    [InlineData("00000000", "0.0.0.0")]
    [InlineData("FF800102", "255.128.1.2")]
    [InlineData("00000000000000000000000000000000", "::")]
    [InlineData("20010000000000010000000000010001", "2001::1:0:0:1:1")]
    [InlineData("20010DB8000000010002000300000000", "2001:db8:0:1:2:3::")]
    [InlineData("00000000000100020003000400050006", "::1:2:3:4:5:6")]
    [InlineData("20010DB8000000010002000300040005", "2001:db8:0:1:2:3:4:5")]
    [InlineData("00000000000000000000FFFFC0000201", "::ffff:c000:201")]
    public void BoundedWireViewsKeepNettyCanonicalText(string hex, string expected)
    {
        byte[] payload = Convert.FromHexString(hex);
        byte[] packet = new byte[payload.Length + 4];
        Array.Fill(packet, (byte)0xCC);
        payload.CopyTo(packet, 2);
        byte[] snapshot = (byte[])packet.Clone();
        ReadOnlySpan<byte> view = packet.AsSpan(2, payload.Length);
        Assert.Equal(expected, NetUtil.BytesToIpAddress(view));
        Assert.Equal(expected, NetUtil.BytesToIpAddress(packet, 2, payload.Length));
        Assert.Equal(expected, NetUtil.BytesToIpAddress(payload));
        Assert.Equal(snapshot, packet);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(17)]
    public void ByteViewRequiresExactlyOneAddress(int length)
    {
        byte[] bytes = new byte[length];
        Assert.Equal("bytes", Assert.Throws<ArgumentException>(() => NetUtil.BytesToIpAddress(bytes.AsSpan())).ParamName);
    }

    [Fact]
    public void StackAndMemoryViewsProduceOwnedCultureIndependentText()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NativeDigits = new[] { "٠", "١", "٢", "٣", "٤", "٥", "٦", "٧", "٨", "٩" };
        culture.NumberFormat.NegativeSign = "~";
        try
        {
            CultureInfo.CurrentCulture = culture;
            Span<byte> stack = stackalloc byte[] { 255, 128, 2, 1 };
            string text = NetUtil.BytesToIpAddress(stack);
            stack.Clear();
            Assert.Equal("255.128.2.1", text);
            Assert.Equal(text, NetUtil.IntToIpAddress(unchecked((int)0xFF800201)));
            ReadOnlyMemory<byte> memory = new byte[] { 0xCC, 127, 0, 0, 1, 0xCC };
            Assert.Equal("127.0.0.1", NetUtil.BytesToIpAddress(memory.Span.Slice(1, 4)));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void NativeAddressFormattingOmitsScopeAndRetainsMappedPolicy()
    {
        IPAddress address = new IPAddress(Convert.FromHexString("00000000000000000000FFFFC0000201"), 4294967295);
        Assert.Equal("::ffff:c000:201", NetUtil.ToAddressString(address));
        Assert.Equal("::ffff:192.0.2.1", NetUtil.ToAddressString(address, true));
        Assert.Equal(4294967295L, address.ScopeId);
        Assert.Equal("[::ffff:c000:201]:443", NetUtil.ToSocketAddressString(new IPEndPoint(address, 443)));
    }

    [Fact]
    public void SeededPacketValuesMatchAnIndependentByteReference()
    {
        uint bits = 0x80000001;
        Span<byte> packet = stackalloc byte[4];
        for (int i = 0; i < 10000; i++)
        {
            bits = unchecked(bits * 1664525 + 1013904223);
            int signed = unchecked((int)bits);
            BinaryPrimitives.WriteInt32BigEndian(packet, signed);
            string expected = FormattableString.Invariant($"{bits >> 24}.{(bits >> 16) & 255}.{(bits >> 8) & 255}.{bits & 255}");
            Assert.Equal(expected, NetUtil.IntToIpAddress(signed));
            Assert.Equal(expected, NetUtil.BytesToIpAddress(packet));
            Assert.Equal(signed, NetUtil.Ipv4AddressToInt(new IPAddress(packet)));
            int subnetMask = unchecked((int)0xFFFFFF00);
            Assert.Equal(unchecked((int)(bits & 0xFFFFFF00)), NetUtil.Ipv4AddressToInt(new IPAddress(packet)) & subnetMask);
        }
    }
}
