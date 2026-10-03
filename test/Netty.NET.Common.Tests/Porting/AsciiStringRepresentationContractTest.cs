using System;
using System.Linq;
using System.Text;

namespace Netty.NET.Common.Tests.Porting;

// Pinned AsciiString.java: byte-to-character widening in toString(int,int),
// c2b(char), byte[] copy/view constructors and arrayChanged().
public class AsciiStringRepresentationContractTest
{
    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void AllByteValuesRoundTripWithoutAsciiReplacement(int offset)
    {
        byte[] bytes = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        byte[] backing = new byte[offset + bytes.Length + 7];
        bytes.CopyTo(backing, offset);
        AsciiString value = new AsciiString(backing, offset, bytes.Length, false);

        string expected = new string(bytes.Select(item => (char)item).ToArray());
        Assert.Equal(expected, value.ToString());
        Assert.Equal(expected.Substring(127, 128), value.ToString(127, 255));
        Assert.Equal(bytes, Encoding.Latin1.GetBytes(value.ToString()));
    }

    [Fact]
    public void CharacterConstructionPreservesLatin1AndReplacesLargerCharacters()
    {
        char[] input = { '\0', 'A', '\u0080', '\u00e9', '\u00ff', '\u0100', '\ud83d', '\ude00' };
        AsciiString value = new AsciiString(input);
        Assert.Equal(new byte[] { 0, 65, 128, 233, 255, 63, 63, 63 }, value.ToByteArray());
        Assert.Equal("\0A\u0080\u00e9\u00ff???", value.ToString());
    }

    [Fact]
    public void SharedViewCacheIsInvalidatedExplicitlyAndCopyRemainsIndependent()
    {
        byte[] backing = { 0, 233, 255, 0 };
        AsciiString view = new AsciiString(backing, 1, 2, false);
        AsciiString copy = new AsciiString(backing, 1, 2, true);
        Assert.Equal("éÿ", view.ToString());
        int hash = view.GetHashCode();

        backing[1] = 128;
        Assert.Equal("éÿ", view.ToString());
        view.ArrayChanged();
        Assert.Equal("\u0080ÿ", view.ToString());
        Assert.Equal(new AsciiString(new byte[] { 128, 255 }).GetHashCode(), view.GetHashCode());
        Assert.Equal("éÿ", copy.ToString());
        Assert.Equal(hash, copy.GetHashCode());
    }
}
