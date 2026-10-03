using System;
using System.Collections.Generic;
using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class CharacterSequenceComparerContractTest
{
    [Fact]
    public void DictionaryAndHashSetShareHeaderKeysAcrossRepresentationsAndSlices()
    {
        ICharSequence ascii = new AsciiString(Encoding.ASCII.GetBytes("xxContent-Typeyy"), 2, 12, false);
        ICharSequence text = new StringCharSequence("zzcontent-typeww", 2, 12);
        ICharSequence appendable = new AppendableCharSequence(12).Append("CONTENT-TYPE");
        IEqualityComparer<ICharSequence> comparer = AsciiString.CASE_INSENSITIVE_HASHER;
        var dictionary = new Dictionary<ICharSequence, int>(comparer) { [ascii] = 1 };
        Assert.Equal(1, dictionary[text]);
        dictionary[appendable] = 2;
        Assert.Equal(1, dictionary.Count);
        Assert.Equal(2, dictionary[ascii]);
        Assert.Equal(comparer.GetHashCode(ascii), comparer.GetHashCode(text));
        Assert.Equal(comparer.GetHashCode(ascii), comparer.GetHashCode(appendable));
        Assert.True(dictionary.Remove(text));
        Assert.Empty(dictionary);

        var set = new HashSet<ICharSequence>(comparer) { ascii };
        Assert.False(set.Add(text));
        Assert.False(set.Add(appendable));
        Assert.True(set.Remove(appendable));
        Assert.Empty(set);
    }

    [Fact]
    public void CaseSensitiveDictionaryKeepsCaseVariantsDespiteTheirSharedAsciiHash()
    {
        ICharSequence upper = new AsciiString("X-Name");
        ICharSequence lower = new StringCharSequence("x-name");
        ICharSequence upperSlice = new StringCharSequence("xxX-Nameyy", 2, 6);
        IEqualityComparer<ICharSequence> comparer = AsciiString.CASE_SENSITIVE_HASHER;
        // The pinned sensitive comparer deliberately uses the same hash as the insensitive comparer.
        Assert.Equal(comparer.GetHashCode(upper), comparer.GetHashCode(lower));
        var dictionary = new Dictionary<ICharSequence, int>(comparer) { [upper] = 1, [lower] = 2 };
        Assert.Equal(2, dictionary.Count);
        Assert.Equal(1, dictionary[upperSlice]);
        Assert.Equal(2, dictionary[new AppendableCharSequence(6).Append("x-name")]);
        Assert.True(dictionary.Remove(upperSlice));
        Assert.Equal(2, dictionary[lower]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullComparisonContractAndNativeCollectionNullPolicyAreExplicit(bool ignoreCase)
    {
        IEqualityComparer<ICharSequence> comparer = ignoreCase
            ? AsciiString.CASE_INSENSITIVE_HASHER : AsciiString.CASE_SENSITIVE_HASHER;
        ICharSequence empty = new StringCharSequence("");
        Assert.True(comparer.Equals(null, null));
        Assert.False(comparer.Equals(null, empty));
        Assert.False(comparer.Equals(empty, null));
        Assert.Equal(0, comparer.GetHashCode(null));
        var set = new HashSet<ICharSequence>(comparer) { null, empty };
        Assert.Equal(2, set.Count);
        Assert.True(set.Contains(AsciiString.EMPTY_STRING));
        Assert.True(set.Remove(null));
        Assert.False(set.Contains(null));
        var dictionary = new Dictionary<ICharSequence, int>(comparer);
        Assert.Throws<ArgumentNullException>(() => dictionary.Add(null, 1));
    }

    [Fact]
    public void EveryLatin1PairUsesOnlyAsciiCaseFoldingAndEqualContentHasEqualHashes()
    {
        ICharSequence[] bytes = new ICharSequence[256];
        ICharSequence[] characters = new ICharSequence[256];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = new AsciiString(new byte[] { 17, (byte)i, 19 }, 1, 1, false);
            characters[i] = new StringCharSequence("!" + (char)i + "?", 1, 1);
        }
        IEqualityComparer<ICharSequence> insensitive = AsciiString.CASE_INSENSITIVE_HASHER;
        IEqualityComparer<ICharSequence> sensitive = AsciiString.CASE_SENSITIVE_HASHER;
        for (int a = 0; a < 256; a++)
        {
            for (int b = 0; b < 256; b++)
            {
                int foldedA = a >= 'A' && a <= 'Z' ? a + 32 : a;
                int foldedB = b >= 'A' && b <= 'Z' ? b + 32 : b;
                bool equal = foldedA == foldedB;
                Assert.Equal(equal, insensitive.Equals(bytes[a], characters[b]));
                Assert.Equal(equal, insensitive.Equals(characters[b], bytes[a]));
                Assert.Equal(a == b, sensitive.Equals(bytes[a], characters[b]));
                Assert.Equal(a == b, sensitive.Equals(characters[b], bytes[a]));
                if (equal)
                    Assert.Equal(insensitive.GetHashCode(bytes[a]), insensitive.GetHashCode(characters[b]));
            }
        }
    }

    [Theory]
    [InlineData("\u00c4", "\u00e4")]
    [InlineData("\u212a", "K")]
    [InlineData("\u0130", "i")]
    [InlineData("\u0131", "I")]
    [InlineData("\u017f", "s")]
    [InlineData("\u03a3", "\u03c3")]
    public void HeaderComparerDoesNotIntroduceUnicodeCaseEquivalence(string left, string right)
    {
        ICharSequence a = new StringCharSequence(left);
        ICharSequence b = new AppendableCharSequence(1).Append(right);
        IEqualityComparer<ICharSequence> comparer = AsciiString.CASE_INSENSITIVE_HASHER;
        Assert.False(comparer.Equals(a, b));
        Assert.False(comparer.Equals(b, a));
        var set = new HashSet<ICharSequence>(comparer) { a, b };
        Assert.Equal(2, set.Count);
        Assert.True(set.Contains(new StringCharSequence(left)));
        Assert.True(set.Contains(new StringCharSequence(right)));
    }

    [Fact]
    public void SharedByteKeysMustBeRemovedBeforeMutationAndTheirCachedHashReset()
    {
        byte[] backing = Encoding.ASCII.GetBytes("xxalphaYY");
        var key = new AsciiString(backing, 2, 5, false);
        var dictionary = new Dictionary<ICharSequence, int>(AsciiString.CASE_INSENSITIVE_HASHER) { [key] = 1 };
        Assert.Equal(1, dictionary[new StringCharSequence("ALPHA")]);
        // Follow AsciiString.arrayChanged's ownership contract; no hash collection repairs a resident mutated key.
        Assert.True(dictionary.Remove(key));
        Encoding.ASCII.GetBytes("bravo").CopyTo(backing, 2);
        key.ArrayChanged();
        dictionary.Add(key, 2);
        Assert.False(dictionary.ContainsKey(new StringCharSequence("alpha")));
        Assert.Equal(2, dictionary[new StringCharSequence("BRAVO")]);
    }
}
