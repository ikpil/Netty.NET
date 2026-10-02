using System;

namespace Netty.NET.Common.Tests.Porting;

// Native allocation permits unspecified initial contents. Observable AsciiString
// bytes must be completely written before publication, including large arrays.
public class AsciiStringAllocationContractTest
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(1023)]
    [InlineData(1024)]
    [InlineData(1025)]
    [InlineData(65537)]
    public void CharacterAndSequenceSlicesPublishEveryMappedByte(int length)
    {
        var characters = new char[length + 4];
        Array.Fill(characters, '\u2007');
        var expected = new byte[length];
        for (int i = 0; i < length; i++)
        {
            byte value = (byte)((i * 73 + 41) & 255);
            characters[i + 2] = (char)value;
            expected[i] = value;
        }

        Assert.Equal(expected, new AsciiString(characters, 2, length).AsSpan().ToArray());
        Assert.Equal(expected, new AsciiString(new StringCharSequence(new string(characters)), 2, length)
            .AsSpan().ToArray());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(1025)]
    [InlineData(65537)]
    public void CaseConversionConcatenationAndReplacementInitializeTheirWholeResult(int length)
    {
        const string pattern = "aB-09xY";
        var characters = new char[length];
        for (int i = 0; i < length; i++) characters[i] = pattern[i % pattern.Length];
        string text = new string(characters);
        AsciiString source = new AsciiString("!" + text + "!").subSequence(1, length + 1, false);

        Assert.Equal(text.ToUpperInvariant(), source.toUpperCase().ToString());
        Assert.Equal(text.ToLowerInvariant(), source.toLowerCase().ToString());
        Assert.Equal(text.Replace('a', 'Q'), source.replace('a', 'Q').ToString());
        Assert.Equal(text + "tail", source.concat(new AsciiString("tail")).ToString());
        Assert.Equal(text + "tail", source.concat(new StringCharSequence("tail")).ToString());
    }
}
