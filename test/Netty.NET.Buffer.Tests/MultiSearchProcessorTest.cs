/*
 * Copyright 2020 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Netty.NET.Buffer.Search;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class MultiSearchProcessorTest
{
    public static IEnumerable<object[]> Backings() => Enumerable.Range(0, 8).Select(i => new object[] { i });
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
    private static AhoCorasicSearchProcessorFactory Factory(params string[] patterns)
        => AbstractMultiSearchProcessorFactory.NewAhoCorasicSearchProcessorFactory(patterns.Select(Bytes).ToArray());

    private static (ByteBuf Buffer, ByteBuf Owner) Buffer(byte[] data, int kind)
    {
        ByteBuf owner = kind switch
        {
            1 => Unpooled.DirectBuffer(data.Length, data.Length).WriteBytes(data),
            2 => Unpooled.CompositeBuffer().AddComponent(Unpooled.WrappedBuffer(data[..(data.Length / 2)]), true)
                .AddComponent(Unpooled.WrappedBuffer(data[(data.Length / 2)..]), true),
            4 => Unpooled.Buffer(data.Length + 2).WriteByte(99).WriteBytes(data).WriteByte(98),
            _ => Unpooled.CopiedBuffer(data)
        };
        ByteBuf buffer = kind switch
        {
            3 => owner.AsReadOnly(), 4 => owner.Slice(1, data.Length), 5 => owner.Duplicate(),
            6 => Unpooled.UnreleasableBuffer(owner), 7 => new SimpleLeakAwareByteBuf(owner, new Tracker()), _ => owner
        };
        return (buffer, owner);
    }

    [Theory]
    [MemberData(nameof(Backings))]
    public void OriginalSearchForMultiple(int kind)
    {
        var (haystack, owner) = Buffer(Bytes("one two three one"), kind);
        try
        {
            int length = haystack.ReadableBytes;
            IMultiSearchProcessor processor = Factory("one", "two", "three").NewSearchProcessor();
            Assert.Equal(-1, processor.FoundNeedleId);
            Assert.Equal(2, haystack.ForEachByte(processor.Process));
            Assert.Equal(0, processor.FoundNeedleId); // index of "one" in needles[]
            Assert.Equal(6, haystack.ForEachByte(3, length - 3, processor.Process));
            Assert.Equal(1, processor.FoundNeedleId); // index of "two" in needles[]
            Assert.Equal(12, haystack.ForEachByte(7, length - 7, processor.Process));
            Assert.Equal(2, processor.FoundNeedleId); // index of "three" in needles[]
            Assert.Equal(16, haystack.ForEachByte(13, length - 13, processor.Process));
            Assert.Equal(0, processor.FoundNeedleId); // index of "one" in needles[]
            Assert.Equal(-1, haystack.ForEachByte(17, length - 17, processor.Process));
            Assert.Equal(0, processor.FoundNeedleId);
            Assert.Equal(0, haystack.ReaderIndex); Assert.Equal(length, haystack.WriterIndex); Assert.Equal(1, owner.ReferenceCount);
        }
        finally { owner.Release(); }
    }

    [Theory]
    [MemberData(nameof(Backings))]
    public void OriginalSearchForMultipleOverlapping(int kind)
    {
        var (haystack, owner) = Buffer(Bytes("abcd"), kind);
        try
        {
            int length = haystack.ReadableBytes;
            IMultiSearchProcessor processor = Factory("ab", "bc", "cd").NewSearchProcessor();
            Assert.Equal(1, haystack.ForEachByte(processor.Process));
            Assert.Equal(0, processor.FoundNeedleId); // index of "ab" in needles[]
            Assert.Equal(2, haystack.ForEachByte(2, length - 2, processor.Process));
            Assert.Equal(1, processor.FoundNeedleId); // index of "bc" in needles[]
            Assert.Equal(3, haystack.ForEachByte(3, length - 3, processor.Process));
            Assert.Equal(2, processor.FoundNeedleId); // index of "cd" in needles[]
            Assert.Equal(0, haystack.ReaderIndex); Assert.Equal(length, haystack.WriterIndex);
        }
        finally { owner.Release(); }
    }

    [Theory]
    [MemberData(nameof(Backings))]
    public void OriginalLongerNeedleWinsSuffixMatch(int kind)
    {
        var (haystack, owner) = Buffer(Bytes("xabcx"), kind);
        try
        {
            IMultiSearchProcessor processor1 = Factory("abc", "bc").NewSearchProcessor();
            Assert.Equal(3, haystack.ForEachByte(processor1.Process)); // end of "abc" in haystack
            Assert.Equal(0, processor1.FoundNeedleId); // index of "abc" in needles[]
            IMultiSearchProcessor processor2 = Factory("bc", "abc").NewSearchProcessor();
            Assert.Equal(3, haystack.ForEachByte(processor2.Process)); // end of "abc" in haystack
            Assert.Equal(1, processor2.FoundNeedleId); // index of "abc" in needles[]
        }
        finally { owner.Release(); }
    }

    private static int ExpectedId(byte[][] patterns, ReadOnlySpan<byte> session)
    {
        int found = -1, length = 0;
        for (int id = 0; id < patterns.Length; id++)
        {
            byte[] pattern = patterns[id];
            if (pattern.Length >= length && session.EndsWith(pattern)) { found = id; length = pattern.Length; }
        }
        return found;
    }

    [Theory]
    [MemberData(nameof(Backings))]
    public void EverySplitRetainsLongestDuplicateAndSuffixStateWithoutMovingCursors(int kind)
    {
        byte[][] patterns = new[] { "he", "she", "his", "hers", "rs", "she" }.Select(Bytes).ToArray();
        byte[] data = Bytes("ushershishershe");
        var expected = new List<(int End, int Id)>();
        for (int i = 0; i < data.Length; i++)
        {
            int id = ExpectedId(patterns, data.AsSpan(0, i + 1));
            if (id >= 0) expected.Add((i, id));
        }
        for (int split = 0; split <= data.Length; split++)
        {
            IMultiSearchProcessor processor = AbstractMultiSearchProcessorFactory.NewAhoCorasicSearchProcessorFactory(patterns).NewSearchProcessor();
            var actual = new List<(int End, int Id)>(); int consumed = 0;
            foreach (byte[] chunk in new[] { data[..split], Array.Empty<byte>(), data[split..] })
            {
                var (buffer, owner) = Buffer(chunk, kind);
                try
                {
                    buffer.MarkReaderIndex().MarkWriterIndex(); int start = 0, found;
                    while ((found = buffer.ForEachByte(start, chunk.Length - start, processor.Process)) >= 0)
                    { actual.Add((consumed + found, processor.FoundNeedleId)); start = found + 1; }
                    buffer.ResetReaderIndex().ResetWriterIndex();
                    Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(chunk.Length, buffer.WriterIndex); Assert.Equal(1, owner.ReferenceCount);
                }
                finally { owner.Release(); }
                consumed += chunk.Length;
            }
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void SuffixFallbackAndDuplicateSelectionAgreeWithSlidingWindowAtEveryByte()
    {
        foreach (string[] needles in new[]
        {
            new[] { "a", "aa", "aaa", "aa" }, new[] { "abc", "bc", "c", "abc" },
            new[] { "he", "she", "hers", "his" }, new[] { "abx", "bc", "c" }, new[] { "ab", "bcd", "bc" }
        })
        {
            byte[][] patterns = needles.Select(Bytes).ToArray();
            IMultiSearchProcessor processor = AbstractMultiSearchProcessorFactory.NewAhoCorasicSearchProcessorFactory(patterns).NewSearchProcessor();
            byte[] data = Bytes("aaaaaxabcabxushershishersabcd");
            for (int i = 0; i < data.Length; i++)
            {
                int expected = ExpectedId(patterns, data.AsSpan(0, i + 1));
                Assert.Equal(expected < 0, processor.Process(data[i])); Assert.Equal(expected, processor.FoundNeedleId);
            }
        }
    }

    [Fact]
    public void FoundIdFollowsCurrentStateAndResetDiscardsPartialOrCompletedMatch()
    {
        IMultiSearchProcessor processor = Factory("ab", "b").NewSearchProcessor();
        Assert.Equal(-1, processor.FoundNeedleId); Assert.True(processor.Process((byte)'a'));
        processor.Reset(); processor.Reset(); Assert.Equal(-1, processor.FoundNeedleId);
        Assert.False(processor.Process((byte)'b')); Assert.Equal(1, processor.FoundNeedleId);
        Assert.True(processor.Process((byte)'x')); Assert.Equal(-1, processor.FoundNeedleId);
        Assert.True(processor.Process((byte)'a')); Assert.False(processor.Process((byte)'b')); Assert.Equal(0, processor.FoundNeedleId);
        processor.Reset(); Assert.Equal(-1, processor.FoundNeedleId);
    }

    [Fact]
    public void FactoryRetainsNoCallerPatternAndBaseInterfacesCreateIndependentProcessors()
    {
        byte[][] patterns = { Bytes("ab"), Bytes("b") };
        AbstractMultiSearchProcessorFactory factory = AbstractMultiSearchProcessorFactory.NewAhoCorasicSearchProcessorFactory(patterns);
        Array.Fill(patterns[0], (byte)'x'); patterns[1] = Bytes("z");
        IMultiSearchProcessor a = ((IMultiSearchProcessorFactory)factory).NewSearchProcessor();
        ISearchProcessor b = ((ISearchProcessorFactory)factory).NewSearchProcessor();
        Assert.IsAssignableFrom<IMultiSearchProcessor>(b); Assert.NotSame(a, b);
        Assert.True(a.Process((byte)'a')); Assert.False(b.Process((byte)'b'));
        Assert.Equal(1, ((IMultiSearchProcessor)b).FoundNeedleId);
        Assert.False(a.Process((byte)'b')); Assert.Equal(0, a.FoundNeedleId);
    }

    [Fact]
    public void EmptyPatternListMatchesNothingAndIndividualNullOrEmptyPatternsAreRejected()
    {
        IMultiSearchProcessor processor = AbstractMultiSearchProcessorFactory.NewAhoCorasicSearchProcessorFactory().NewSearchProcessor();
        for (int i = 0; i < 256; i++) { Assert.True(processor.Process((byte)i)); Assert.Equal(-1, processor.FoundNeedleId); }
        processor.Reset(); Assert.Equal(-1, processor.FoundNeedleId);
        Assert.Throws<ArgumentNullException>(() => AbstractMultiSearchProcessorFactory.NewAhoCorasicSearchProcessorFactory((byte[][])null));
        Assert.Throws<ArgumentNullException>(() => AbstractMultiSearchProcessorFactory.NewAhoCorasicSearchProcessorFactory(Bytes("a"), null));
        Assert.Throws<ArgumentException>(() => AbstractMultiSearchProcessorFactory.NewAhoCorasicSearchProcessorFactory(Bytes("a"), Array.Empty<byte>()));
    }

    [Fact]
    public void FullBinaryAlphabetAndLongestHighBitSuffixArePreserved()
    {
        byte[][] patterns = Enumerable.Range(0, 256).Select(i => new[] { (byte)i }).Concat(new[]
        { new byte[] { 255, 128 }, new byte[] { 0, 255, 128 }, new byte[] { 255 } }).ToArray();
        IMultiSearchProcessor processor = AbstractMultiSearchProcessorFactory.NewAhoCorasicSearchProcessorFactory(patterns).NewSearchProcessor();
        for (int i = 0; i < 256; i++)
        { Assert.False(processor.Process((byte)i)); Assert.Equal(i == 255 ? 258 : i, processor.FoundNeedleId); }
        processor.Reset(); Assert.False(processor.Process(0)); Assert.False(processor.Process(255)); Assert.False(processor.Process(128));
        Assert.Equal(257, processor.FoundNeedleId);
    }

    [Fact]
    public void LongSharedPrefixesAndFallbackBeyondBitapLimitAreSupported()
    {
        byte[] prefix = Enumerable.Repeat((byte)255, 1025).ToArray();
        byte[][] patterns = { prefix.Concat(new byte[] { 0 }).ToArray(), prefix.Concat(new byte[] { 128 }).ToArray(), new byte[] { 255, 128 } };
        IMultiSearchProcessor processor = AbstractMultiSearchProcessorFactory.NewAhoCorasicSearchProcessorFactory(patterns).NewSearchProcessor();
        foreach (byte value in prefix) Assert.True(processor.Process(value));
        Assert.False(processor.Process(128)); Assert.Equal(1, processor.FoundNeedleId);
        Assert.True(processor.Process(255)); Assert.False(processor.Process(128)); Assert.Equal(2, processor.FoundNeedleId);
    }

    [Fact]
    public void RandomPatternSetsAndResetsAgreeWithIndependentLongestSuffixModel()
    {
        var random = new Random(314159);
        for (int seed = 0; seed < 384; seed++)
        {
            byte[][] patterns = Enumerable.Range(0, 1 + seed % 12).Select(_ => new byte[random.Next(1, 24)]).ToArray();
            foreach (byte[] pattern in patterns) for (int j = 0; j < pattern.Length; j++) pattern[j] = (byte)(random.Next(8) * 33);
            if (patterns.Length > 2) { patterns[^1] = patterns[0].ToArray(); patterns[^2] = patterns[0][^1..]; }
            byte[] data = new byte[384]; for (int j = 0; j < data.Length; j++) data[j] = (byte)(random.Next(8) * 33);
            patterns[0].CopyTo(data, 60); patterns[^1].CopyTo(data, 200);
            IMultiSearchProcessor processor = AbstractMultiSearchProcessorFactory.NewAhoCorasicSearchProcessorFactory(patterns).NewSearchProcessor();
            int resetStart = 0;
            for (int i = 0; i < data.Length; i++)
            {
                if (i == 61 || i == 257) { processor.Reset(); resetStart = i; Assert.Equal(-1, processor.FoundNeedleId); }
                int expected = ExpectedId(patterns, data.AsSpan(resetStart, i - resetStart + 1));
                Assert.Equal(expected < 0, processor.Process(data[i])); Assert.Equal(expected, processor.FoundNeedleId);
            }
        }
    }

    [Fact]
    public void FactorySupportsConcurrentIndependentSessions()
    {
        var factory = Factory("a", "ba", "aba");
        Parallel.For(0, 64, _ =>
        {
            IMultiSearchProcessor processor = factory.NewSearchProcessor();
            for (int i = 0; i < 256; i++)
            {
                processor.Reset(); Assert.False(processor.Process((byte)'a')); Assert.Equal(0, processor.FoundNeedleId);
                Assert.True(processor.Process((byte)'b')); Assert.Equal(-1, processor.FoundNeedleId);
                Assert.False(processor.Process((byte)'a')); Assert.Equal(2, processor.FoundNeedleId);
            }
        });
    }

    private sealed class Tracker : IResourceLeakTracker<ByteBuf>
    {
        public void Record() { }
        public void Record(object hint) { }
        public bool Close(ByteBuf buffer) => true;
    }
}
