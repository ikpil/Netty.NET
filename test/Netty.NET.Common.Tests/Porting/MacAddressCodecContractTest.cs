using System;
using System.Globalization;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class MacAddressCodecContractTest
{
    [Theory]
    [InlineData("ab", new byte[] { 0xab })]
    [InlineData("00:aa:11:bb:22:cc", new byte[] { 0, 0xaa, 0x11, 0xbb, 0x22, 0xcc })]
    [InlineData("00:aa:11:ff:fe:bb:22:cc", new byte[] { 0, 0xaa, 0x11, 0xff, 0xfe, 0xbb, 0x22, 0xcc })]
    public void CanonicalFormattingUsesLowercaseHexAndColonSeparators(string expected, byte[] bytes)
    {
        byte[] snapshot = (byte[])bytes.Clone();
        Assert.Equal(expected, MacAddressUtil.FormatAddress(bytes));
        Assert.Equal(snapshot, bytes);
    }

    [Fact]
    public void EveryUnsignedByteFormatsWithoutLosingLeadingZeros()
    {
        var bytes = new byte[256];
        var expected = new string[256];
        for (int value = 0; value < bytes.Length; value++)
        {
            bytes[value] = (byte)value;
            expected[value] = Convert.ToString(value, 16).PadLeft(2, '0');
        }
        Assert.Equal(string.Join(':', expected), MacAddressUtil.FormatAddress(bytes));
    }

    [Fact]
    public void EmptyAddressHasAnEmptyNativeRepresentation()
    {
        Assert.Same(string.Empty, MacAddressUtil.FormatAddress(Array.Empty<byte>()));
    }

    [Fact]
    public void NullInputsIdentifyTheirNativeArguments()
    {
        Assert.Equal("value", Assert.Throws<ArgumentNullException>(() => MacAddressUtil.ParseMAC(null)).ParamName);
        Assert.Equal("addr", Assert.Throws<ArgumentNullException>(() => MacAddressUtil.FormatAddress(null)).ParamName);
    }

    [Fact]
    public void MachineIdParsingRetainsWidthAndReturnsIndependentOwnedBytes()
    {
        foreach (string spelling in new[] { "00-aA-11-bB-22-cC", "00:aA:11:bB:22:cC",
                     "00-aA-11-fF-fE-bB-22-cC", "00:aA:11:fF:fE:bB:22:cC" })
        {
            byte[] first = MacAddressUtil.ParseMAC(spelling);
            byte[] second = MacAddressUtil.ParseMAC(spelling);
            Assert.NotSame(first, second);
            Assert.Equal(first, second);
            Assert.Equal(spelling.Length == 17 ? 6 : 8, first.Length);
            first[0] = 0xff;
            Assert.Equal(0, second[0]);
            Assert.Equal(spelling.Replace('-', ':').ToLowerInvariant(), MacAddressUtil.FormatAddress(second));
        }
    }

    [Fact]
    public void FormattingAndParsingDoNotDependOnTheCurrentCulture()
    {
        CultureInfo prior = CultureInfo.CurrentCulture;
        try
        {
            foreach (string name in new[] { "ar-SA", "tr-TR", "en-US" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                byte[] bytes = MacAddressUtil.ParseMAC("FA-00-01-80-AB-FF");
                Assert.Equal("fa:00:01:80:ab:ff", MacAddressUtil.FormatAddress(bytes));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = prior;
        }
    }

    [Fact]
    public void InvalidAddressSyntaxDoesNotProduceAPartialMachineId()
    {
        foreach (string input in new[] { "", "ab", "00:aa:11:bb:22", "00:aa:11:bb:22:cc:dd",
                     "00:aa-11:bb:22:cc", "00/aa/11/bb/22/cc", "00:aa:11:bb:22:cg",
                     "00:aa:11:bb:22: c", "00:aa:11:bb:22:ｃｃ", "00:aa:11:bb:22:cc:" })
            Assert.Throws<ArgumentException>(() => MacAddressUtil.ParseMAC(input));
    }
}
