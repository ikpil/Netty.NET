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

public class SearchProcessorTest
{
    public static IEnumerable<object[]> Layouts()
    {
        for (int algorithm = 0; algorithm < 2; algorithm++)
            for (int layout = 0; layout < 8; layout++) yield return new object[] { algorithm, layout };
    }

    private static AbstractSearchProcessorFactory Factory(int algorithm, byte[] needle) => algorithm == 0
        ? AbstractSearchProcessorFactory.NewKmpSearchProcessorFactory(needle)
        : AbstractSearchProcessorFactory.NewBitapSearchProcessorFactory(needle);

    private static (ByteBuf Buffer, ByteBuf Owner) Buffer(byte[] data, int layout)
    {
        ByteBuf owner = layout switch
        {
            1 => Unpooled.DirectBuffer(data.Length, data.Length).WriteBytes(data),
            2 => Unpooled.CompositeBuffer().AddComponent(Unpooled.WrappedBuffer(data[..(data.Length / 2)]), true)
                .AddComponent(Unpooled.WrappedBuffer(data[(data.Length / 2)..]), true),
            4 => Unpooled.Buffer(data.Length + 2).WriteByte(99).WriteBytes(data).WriteByte(98),
            _ => Unpooled.CopiedBuffer(data)
        };
        ByteBuf buffer = layout switch
        {
            3 => owner.AsReadOnly(), 4 => owner.Slice(1, data.Length), 5 => owner.Duplicate(),
            6 => Unpooled.UnreleasableBuffer(owner), 7 => new SimpleLeakAwareByteBuf(owner, new Tracker()), _ => owner
        };
        return (buffer, owner);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void OriginalSearchReturnsLastByteIndex(int algorithm, int layout)
    {
        var (buffer, owner) = Buffer(Encoding.UTF8.GetBytes("abc☺"), layout);
        try
        {
            string[] patterns = { "a", "ab", "abc", "abc☺", "abc☺☺", "abc☺x", "b", "bc", "bc☺", "bc☺☺", "bc☺x",
                "c", "c☺", "c☺☺", "c☺x", "☺", "☺☺", "☺x", "z", "aa", "ba", "abcd", "abcde" };
            int[] expected = { 0, 1, 2, 5, -1, -1, 1, 2, 5, -1, -1, 2, 5, -1, -1, 5, -1, -1, -1, -1, -1, -1, -1 };
            for (int i = 0; i < patterns.Length; i++)
            {
                ISearchProcessor processor = Factory(algorithm, Encoding.UTF8.GetBytes(patterns[i])).NewSearchProcessor();
                Assert.Equal(expected[i], buffer.ForEachByte(processor.Process));
            }
            Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(6, buffer.WriterIndex); Assert.Equal(1, owner.ReferenceCount);
        }
        finally { owner.Release(); }
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void OriginalRepeatingAndOverlappingMatchesContinueAfterTheLastByte(int algorithm, int layout)
    {
        foreach (var (haystack, needle, expected) in new[]
        {
            ("abcababc", "ab", new[] { 1, 4, 6 }), ("ababab", "bab", new[] { 3, 5 })
        })
        {
            var (buffer, owner) = Buffer(Encoding.UTF8.GetBytes(haystack), layout);
            try
            {
                ISearchProcessor processor = Factory(algorithm, Encoding.UTF8.GetBytes(needle)).NewSearchProcessor();
                int start = 0;
                foreach (int end in expected)
                {
                    Assert.Equal(end, buffer.ForEachByte(start, buffer.ReadableBytes - start, processor.Process));
                    start = end + 1;
                }
                Assert.Equal(-1, buffer.ForEachByte(start, buffer.ReadableBytes - start, processor.Process));
                Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(haystack.Length, buffer.WriterIndex);
            }
            finally { owner.Release(); }
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void OriginalLongInputs(int algorithm)
    {
        byte[] haystackBytes = new byte[1024]; haystackBytes[^1] = 1;
        ByteBuf haystack = Unpooled.CopiedBuffer(haystackBytes); // 00000...00001
        try
        {
            byte[] needleBytes = new byte[64]; // 000...000
            Assert.Equal(63, haystack.ForEachByte(Factory(algorithm, needleBytes).NewSearchProcessor().Process));
            needleBytes[^1] = 1; // 000...001
            Assert.Equal(1023, haystack.ForEachByte(Factory(algorithm, needleBytes).NewSearchProcessor().Process));
            needleBytes[^1] = 2; // 000...002
            Assert.Equal(-1, haystack.ForEachByte(Factory(algorithm, needleBytes).NewSearchProcessor().Process));
            needleBytes[^1] = 0; needleBytes[0] = 1; // 100...000
            Assert.Equal(-1, haystack.ForEachByte(Factory(algorithm, needleBytes).NewSearchProcessor().Process));
        }
        finally { haystack.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void OriginalUniqueLength64Substrings(int algorithm)
    {
        byte[] haystackBytes = new byte[32 * 65]; // 1, 2, 2, 3, 3, 3, 4, 4, 4, 4, ...
        int pos = 0;
        for (int i = 1; i <= 64; i++)
            for (int j = 0; j < i; j++) haystackBytes[pos++] = (byte)i;
        ByteBuf haystack = Unpooled.CopiedBuffer(haystackBytes);
        try
        {
            for (int start = 0; start < haystackBytes.Length - 64; start++)
                Assert.Equal(start + 63, haystack.ForEachByte(Factory(algorithm, haystackBytes.AsSpan(start, 64).ToArray()).NewSearchProcessor().Process));
        }
        finally { haystack.Release(); }
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void SessionSpansEveryBufferBoundaryAndPreservesIndicesAndMarks(int algorithm, int layout)
    {
        byte[] needle = { 0xff, 0, 0x80, 0xff, 0 }; byte[] wire = needle.Concat(needle).ToArray();
        for (int split = 0; split <= wire.Length; split++)
        {
            ISearchProcessor processor = Factory(algorithm, needle).NewSearchProcessor();
            var matches = new List<int>(); int consumed = 0;
            foreach (byte[] chunk in new[] { wire[..split], Array.Empty<byte>(), wire[split..] })
            {
                var (buffer, owner) = Buffer(chunk, layout);
                try
                {
                    buffer.MarkReaderIndex().MarkWriterIndex();
                    int start = 0, found;
                    while ((found = buffer.ForEachByte(start, chunk.Length - start, processor.Process)) >= 0)
                    { matches.Add(consumed + found); start = found + 1; }
                    buffer.ResetReaderIndex().ResetWriterIndex();
                    Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(chunk.Length, buffer.WriterIndex);
                    Assert.Equal(1, owner.ReferenceCount);
                }
                finally { owner.Release(); }
                consumed += chunk.Length;
            }
            Assert.Equal(new[] { 4, 9 }, matches);
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void FactoryCopiesPatternAndProcessorsHaveIndependentResettableState(int algorithm)
    {
        byte[] needle = { 1, 2, 1 }; AbstractSearchProcessorFactory factory = Factory(algorithm, needle);
        Array.Fill(needle, (byte)99);
        ISearchProcessor first = factory.NewSearchProcessor(), second = factory.NewSearchProcessor();
        Assert.NotSame(first, second);
        Assert.True(first.Process(1)); Assert.True(first.Process(2));
        Assert.True(second.Process(1)); Assert.False(first.Process(1));
        Assert.True(second.Process(2)); Assert.False(second.Process(1));
        first.Reset(); first.Reset(); Assert.True(first.Process(2)); Assert.True(first.Process(1));
        Assert.True(first.Process(2)); Assert.False(first.Process(1));
        Assert.True(first.Process(2)); first.Reset(); Assert.True(first.Process(1));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void RandomSessionsAgreeWithIndependentSlidingWindowIncludingResets(int algorithm)
    {
        var random = new Random(271828);
        for (int seed = 0; seed < 256; seed++)
        {
            byte[] pattern = new byte[1 + seed % 64], data = new byte[512];
            random.NextBytes(pattern); random.NextBytes(data);
            pattern.CopyTo(data, 80); pattern.CopyTo(data, 240);
            ISearchProcessor processor = Factory(algorithm, pattern).NewSearchProcessor(); int resetStart = 0;
            for (int index = 0; index < data.Length; index++)
            {
                if (index == 257 || index == 81 && seed % 2 == 0) { processor.Reset(); resetStart = index; }
                bool match = index - resetStart + 1 >= pattern.Length && data.AsSpan(index - pattern.Length + 1, pattern.Length).SequenceEqual(pattern);
                Assert.Equal(!match, processor.Process(data[index]));
            }
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void ConcurrentSessionsShareFactoryButNotState(int algorithm)
    {
        AbstractSearchProcessorFactory factory = Factory(algorithm, new byte[] { 255, 128, 0 });
        Parallel.For(0, 64, _ =>
        {
            ISearchProcessor processor = factory.NewSearchProcessor();
            for (int i = 0; i < 256; i++)
            { Assert.True(processor.Process(255)); Assert.True(processor.Process(128)); Assert.False(processor.Process(0)); }
        });
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void SpanFactoriesOwnStackPatternsAndArrayFactoriesRejectNullAndEmpty(int algorithm)
    {
        Span<byte> needle = stackalloc byte[] { 255, 128, 0 };
        ISearchProcessorFactory factory = algorithm == 0
            ? AbstractSearchProcessorFactory.NewKmpSearchProcessorFactory(needle)
            : AbstractSearchProcessorFactory.NewBitapSearchProcessorFactory(needle);
        needle.Clear();
        ISearchProcessor processor = factory.NewSearchProcessor();
        Assert.True(processor.Process(255)); Assert.True(processor.Process(128)); Assert.False(processor.Process(0));
        Assert.Throws<ArgumentNullException>(() => Factory(algorithm, null));
        Assert.Throws<ArgumentException>(() => Factory(algorithm, Array.Empty<byte>()));
        Assert.Throws<ArgumentException>(() => EmptySpanFactory(algorithm));
    }

    private static void EmptySpanFactory(int algorithm)
    {
        if (algorithm == 0) AbstractSearchProcessorFactory.NewKmpSearchProcessorFactory(ReadOnlySpan<byte>.Empty);
        else AbstractSearchProcessorFactory.NewBitapSearchProcessorFactory(ReadOnlySpan<byte>.Empty);
    }

    [Fact]
    public void KmpSupportsPatternsLongerThanBitapLimit()
    {
        byte[] needle = Enumerable.Range(0, 1025).Select(i => (byte)(i % 251)).ToArray();
        ISearchProcessor processor = AbstractSearchProcessorFactory.NewKmpSearchProcessorFactory(needle).NewSearchProcessor();
        for (int i = 0; i < needle.Length; i++) Assert.Equal(i != needle.Length - 1, processor.Process(needle[i]));
    }

    private sealed class Tracker : IResourceLeakTracker<ByteBuf>
    {
        public void Record() { }
        public void Record(object hint) { }
        public bool Close(ByteBuf buffer) => true;
    }
}
