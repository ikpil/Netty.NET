using System;
using System.Globalization;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Transport.Tests;

// The pinned sources concatenate numbers into messages ("index: " + index + " (expected: 1~31)"), which Java renders
// without consulting a locale; the port must not pick up the current culture's minus sign.
public class CultureIndependentMessageContractTest
{
    [Theory]
    [InlineData("ar-SA")]
    [InlineData("sv-SE")]
    public void WritabilityIndexMessageUsesAsciiSign(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        var channel = new EmbeddedChannel();
        try
        {
            var e = Assert.Throws<ArgumentException>(() => channel.OutboundBuffer.SetUserDefinedWritability(-1, true));
            Assert.Equal("index: -1 (expected: 1~31)", e.Message);
            Assert.Equal("index: 32 (expected: 1~31)",
                Assert.Throws<ArgumentException>(() => channel.OutboundBuffer.GetUserDefinedWritability(32)).Message);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            channel.FinishAndReleaseAll();
        }
    }

    [Theory]
    [InlineData("ar-SA")]
    [InlineData("sv-SE")]
    public void AdaptiveReceiveAllocatorMessagesUseAsciiSign(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            Assert.Equal("minimum : -1 (expected: > 0)",
                Assert.Throws<ArgumentException>(() => new AdaptiveRecvByteBufAllocator(-1, 1, 2)).Message);
            Assert.Equal("initial: -2",
                Assert.Throws<ArgumentException>(() => new AdaptiveRecvByteBufAllocator(1, -2, 2)).Message);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
