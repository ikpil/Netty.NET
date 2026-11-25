using System;
using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

// CLR: contracts of the two CLR-only helpers that the owner added on 2026-11-24 for the codec and handler modules,
// CharsetUtil.ForName (Charset.forName) and ThrowableUtil.ThrowableToString (Throwable.toString()); no original test
// covers them. The expected values are the Java results of the pinned Charset/Throwable documentation
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/nio/charset/Charset.html,
// https://docs.oracle.com/en/java/javase/17/intl/supported-encodings.html,
// https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Throwable.html#toString()).
public class SharedApiHelperContractTest
{
    // The java.base canonical names and aliases of the six standard charsets, with the literal bytes of "A\u00e9" in
    // that charset (US-ASCII has no e-acute, so "A" only).
    public static readonly TheoryData<string, string, byte[]> JavaCharsetNames = NewJavaCharsetNames();

    private static TheoryData<string, string, byte[]> NewJavaCharsetNames()
    {
        var data = new TheoryData<string, string, byte[]>();
        foreach (string name in new[] { "UTF-8", "UTF8", "unicode-1-1-utf-8" })
        {
            data.Add(name, "A\u00e9", new byte[] { 0x41, 0xc3, 0xa9 });
        }
        foreach (string name in new[] { "US-ASCII", "iso-ir-6", "ANSI_X3.4-1986", "ISO_646.irv:1991", "ASCII",
                                        "ISO646-US", "us", "IBM367", "cp367", "csASCII", "default", "646",
                                        "iso_646.irv:1983", "ANSI_X3.4-1968", "ascii7" })
        {
            data.Add(name, "A", new byte[] { 0x41 });
        }
        foreach (string name in new[] { "ISO-8859-1", "iso-ir-100", "ISO_8859-1", "latin1", "l1", "IBM819", "cp819",
                                        "csISOLatin1", "819", "IBM-819", "ISO8859_1", "ISO_8859-1:1987", "ISO_8859_1",
                                        "8859_1", "ISO8859-1" })
        {
            data.Add(name, "A\u00e9", new byte[] { 0x41, 0xe9 });
        }
        foreach (string name in new[] { "UTF-16BE", "UTF_16BE", "ISO-10646-UCS-2", "X-UTF-16BE", "UnicodeBigUnmarked",
                                        "UTF-16", "UTF_16", "utf16", "unicode", "UnicodeBig" })
        {
            data.Add(name, "A\u00e9", new byte[] { 0x00, 0x41, 0x00, 0xe9 });
        }
        foreach (string name in new[] { "UTF-16LE", "UTF_16LE", "X-UTF-16LE", "UnicodeLittleUnmarked" })
        {
            data.Add(name, "A\u00e9", new byte[] { 0x41, 0x00, 0xe9, 0x00 });
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(JavaCharsetNames))]
    public void ForNameResolvesJavaCharsetNamesIgnoringCase(string name, string text, byte[] expected)
    {
        Assert.Equal(expected, CharsetUtil.ForName(name).GetBytes(text));
        Assert.Equal(expected, CharsetUtil.ForName(name.ToUpperInvariant()).GetBytes(text));
        Assert.Equal(expected, CharsetUtil.ForName(name.ToLowerInvariant()).GetBytes(text));
        Assert.Equal(text, CharsetUtil.ForName(name).GetString(expected));
    }

    [Fact]
    public void ForNamePassesOtherLegalNamesToEncodingGetEncoding()
    {
        Assert.Equal(Encoding.GetEncoding("utf-32").CodePage, CharsetUtil.ForName("utf-32").CodePage);
        // UnsupportedCharsetException is the ArgumentException of Encoding.GetEncoding.
        Assert.Throws<ArgumentException>(() => CharsetUtil.ForName("x-no-such-charset"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" utf-8")]
    [InlineData("utf-8 ")]
    [InlineData("-utf8")]
    [InlineData("_utf8")]
    [InlineData("utf/8")]
    [InlineData("u\u017f-ascii")]
    [InlineData("\u0131so-8859-1")]
    public void ForNameRejectsIllegalNamesAsIllegalCharsetNameException(string name)
    {
        // IllegalCharsetNameException(name) is an IllegalArgumentException whose message is the name; the non-ASCII
        // letters, which the case-insensitive comparer would fold to "s" and "I", are not legal charset characters.
        ArgumentException e = Assert.Throws<ArgumentException>(() => CharsetUtil.ForName(name));
        Assert.Equal(name, e.Message);
    }

    [Fact]
    public void ForNameRejectsNullAsIllegalArgumentException()
    {
        // Charset.forName(null) throws IllegalArgumentException("Null charset name"), not NullPointerException, so the
        // exception is not ArgumentNullException (NullPointerException in this port).
        ArgumentException e = Assert.Throws<ArgumentException>(() => CharsetUtil.ForName(null));
        Assert.Equal("Null charset name", e.Message);
    }

    [Fact]
    public void ForNameUtf16IsBigEndianWithoutByteOrderMarkDetection()
    {
        // Java's UTF-16 decoder would read FE FF as a byte-order mark; the big-endian UnicodeEncoding keeps it as
        // U+FEFF (docs/common-clr-design.md, UTF-16), and GetBytes writes no byte-order mark.
        Encoding utf16 = CharsetUtil.ForName("UTF-16");
        Assert.Equal("\ufeffA", utf16.GetString(new byte[] { 0xfe, 0xff, 0x00, 0x41 }));
        Assert.Equal(new byte[] { 0x00, 0x41 }, utf16.GetBytes("A"));
    }

    [Fact]
    public void ThrowableToStringIsTheJavaThrowableText()
    {
        Assert.Null(ThrowableUtil.ThrowableToString(null));
        Assert.Equal("System.InvalidOperationException: boom",
                ThrowableUtil.ThrowableToString(new InvalidOperationException("boom")));
        // A CLR exception without a message has a default message, so ": " is always present.
        var noMessage = new InvalidOperationException();
        Assert.Equal("System.InvalidOperationException: " + noMessage.Message, ThrowableUtil.ThrowableToString(noMessage));
        // Unlike Exception.ToString(), the text has no inner exception and no stack trace.
        Exception thrown;
        try
        {
            throw new FormatException("outer", new ArgumentException("inner"));
        }
        catch (FormatException e)
        {
            thrown = e;
        }
        Assert.Equal("System.FormatException: outer", ThrowableUtil.ThrowableToString(thrown));
        Assert.Equal("Netty.NET.Common.Tests.Porting.SharedApiHelperContractTest+NestedException: nested",
                ThrowableUtil.ThrowableToString(new NestedException("nested")));
    }

    private sealed class NestedException : Exception
    {
        public NestedException(string message) : base(message)
        {
        }
    }
}
