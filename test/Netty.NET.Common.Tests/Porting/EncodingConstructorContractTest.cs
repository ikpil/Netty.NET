using System;
using System.Text;
using System.Threading;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class EncodingConstructorContractTest
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void NativeEncodingConstructorsRespectByteOrderAndExcludeAutomaticPreambles(int kind)
    {
        Encoding encoding = CreateEncoding(kind);
        // Literal bytes are independent of Encoding.GetBytes and the implementation under test.
        byte[] expected = kind switch
        {
            0 or 1 => new byte[] { 0x00, 0x41, 0x00, 0xe9, 0xd8, 0x3d, 0xde, 0x00 },
            2 => new byte[] { 0x41, 0x00, 0xe9, 0x00, 0x3d, 0xd8, 0x00, 0xde },
            3 => new byte[] { 0x41, 0xc3, 0xa9, 0xf0, 0x9f, 0x98, 0x80 },
            // CLR single-byte replacement emits one '?' per UTF-16 surrogate; Java replaces the pair once.
            4 => new byte[] { 0x41, 0xe9, 0x3f, 0x3f },
            _ => new byte[] { 0x41, 0x3f, 0x3f, 0x3f }
        };
        AssertAllInputs("!Aé😀!", 1, 4, encoding, expected);
        AssertAllInputs("!Aé😀!", 1, 0, encoding, Array.Empty<byte>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CallerReplacementHandlesMalformedAndUnmappableInputAcrossAllConstructors(bool ascii)
    {
        Encoding encoding = (Encoding)(ascii ? Encoding.ASCII : Encoding.UTF8).Clone();
        var replacement = new EncoderReplacementFallback("[bad]");
        encoding.EncoderFallback = replacement;
        // ASCII treats é as unmappable; UTF-8 represents it. Both replace each lone surrogate.
        byte[] expected = ascii
            ? new byte[] { 65, 91, 98, 97, 100, 93, 91, 98, 97, 100, 93, 91, 98, 97, 100, 93, 66 }
            : new byte[] { 65, 91, 98, 97, 100, 93, 0xc3, 0xa9, 91, 98, 97, 100, 93, 66 };
        AssertAllInputs("!A\ud800é\udc00B!", 1, 5, encoding, expected);
        Assert.Same(replacement, encoding.EncoderFallback);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    public void StrictFailureDoesNotChangeCallerPolicyOrContaminateTheNextOperation(bool ascii, int invalidKind)
    {
        Encoding encoding = (Encoding)(ascii ? Encoding.ASCII : Encoding.UTF8).Clone();
        encoding.EncoderFallback = EncoderFallback.ExceptionFallback;
        string invalid = invalidKind switch { 0 => "\ud800", 1 => "\udc00", 2 => "é", _ => "😀" };
        Assert.Throws<EncoderFallbackException>(() => new AsciiString(invalid, encoding));
        Assert.Throws<EncoderFallbackException>(() => new AsciiString(invalid.ToCharArray(), encoding));
        Assert.Throws<EncoderFallbackException>(() => new AsciiString(new StringCharSequence(invalid), encoding));
        AssertAllInputs("!ok!", 1, 2, encoding, new byte[] { 111, 107 });
        Assert.Same(EncoderFallback.ExceptionFallback, encoding.EncoderFallback);
    }

    [Fact]
    public void DefaultClrUtf8ReplacementRemainsExplicitlyDifferentFromJavaQuestionMarkReplacement()
    {
        AssertAllInputs("!\ud800!", 1, 1, new UTF8Encoding(false), new byte[] { 0xef, 0xbf, 0xbd });
        Encoding javaReplacement = (Encoding)new UTF8Encoding(false).Clone();
        javaReplacement.EncoderFallback = new EncoderReplacementFallback("?");
        AssertAllInputs("!\ud800!", 1, 1, javaReplacement, new byte[] { 0x3f });
    }

    [Fact]
    public void StrictEncodingDoesNotInspectInvalidTextOutsideTheSelectedRange()
    {
        AssertAllInputs("\ud800é😀\udc00", 1, 3, new UTF8Encoding(true, true),
            new byte[] { 0xc3, 0xa9, 0xf0, 0x9f, 0x98, 0x80 });
    }

    [Fact]
    public void OneShotEncodingDoesNotCreateHiddenThreadLocalCodecState()
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Assert.Null(InternalThreadLocalMap.GetIfSet());
                for (int i = 0; i < 6; i++)
                    _ = new AsciiString("Aé😀", CreateEncoding(i));
                Assert.Null(InternalThreadLocalMap.GetIfSet());
            }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }

    private static Encoding CreateEncoding(int kind) => kind switch
    {
        0 => new UnicodeEncoding(true, true),
        1 => new UnicodeEncoding(true, false),
        2 => new UnicodeEncoding(false, false),
        3 => new UTF8Encoding(true),
        4 => Encoding.GetEncoding("iso-8859-1", new EncoderReplacementFallback("?"), DecoderFallback.ExceptionFallback),
        _ => Encoding.GetEncoding("us-ascii", new EncoderReplacementFallback("?"), DecoderFallback.ExceptionFallback)
    };

    private static void AssertAllInputs(string text, int start, int length, Encoding encoding, byte[] expected)
    {
        Assert.Equal(expected, new AsciiString(text, encoding, start, length).AsSpan().ToArray());
        Assert.Equal(expected, new AsciiString(text.AsSpan(start, length), encoding).AsSpan().ToArray());
        Assert.Equal(expected, new AsciiString(text.ToCharArray(), encoding, start, length).AsSpan().ToArray());
        ICharSequence slice = new StringCharSequence("xx" + text + "yy", 2, text.Length);
        Assert.Equal(expected, new AsciiString(slice, encoding, start, length).AsSpan().ToArray());
    }
}
