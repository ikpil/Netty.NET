/*
 * Copyright 2026 The Netty Project
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
using System.Text;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Tests;

// CLR: JUnit @Nested classes are public nested xUnit test classes; @BeforeEach is the constructor (xUnit creates one
// instance per test). CharsetUtil.US_ASCII is Encoding.ASCII. Mockito mocks of ByteBufAllocator are Moq mocks of
// IByteBufAllocator; like Mockito, unconfigured members return default values.
public class AdaptiveCumulatorTest
{
    public class CumulateTests
    {
        private const string DATA_INITIAL = "0123";
        private const string DATA_INCOMING = "456789";
        private const string DATA_CUMULATED = "0123456789";

        private readonly IByteBufAllocator alloc = new UnpooledByteBufAllocator(false);
        private AdaptiveCumulator cumulator;
        // CLR: UnsupportedOperationException is NotSupportedException.
        private readonly NotSupportedException throwingCumulatorError = new NotSupportedException();

        private ByteBuf contiguous;
        private ByteBuf @in;

        public CumulateTests()
        {
            SetUp();
        }

        private void SetUp()
        {
            contiguous = ByteBufUtil.WriteAscii(alloc, DATA_INITIAL);
            @in = ByteBufUtil.WriteAscii(alloc, DATA_INCOMING);

            cumulator = new AdaptiveCumulator(0);
        }

        [Fact]
        public void Cumulate_notReadableCumulation_replacedWithInputAndReleased()
        {
            contiguous.ReaderIndex = contiguous.WriterIndex;
            Assert.False(contiguous.IsReadable());
            ByteBuf cumulation = cumulator.Cumulate(alloc, contiguous, @in);
            Assert.Equal(DATA_INCOMING, cumulation.ToString(Encoding.ASCII));
            Assert.Equal(0, contiguous.RefCnt);
            Assert.Equal(1, @in.RefCnt);
            Assert.Equal(1, cumulation.RefCnt);
            cumulation.Release();
        }

        [Fact]
        public void Cumulate_sameBuffer_identityHandled()
        {
            @in.Retain(); // simulate double retention when the same buffer is passed twice
            ByteBuf result = cumulator.Cumulate(alloc, @in, @in);
            Assert.Same(@in, result);
            Assert.Equal(1, @in.RefCnt, "The cumulator should release the doubly-retained buffer once");
            @in.Release();
        }

        [Fact]
        public void Cumulate_contiguousCumulation_newCompositeFromContiguousAndInput()
        {
            CompositeByteBuf cumulation = (CompositeByteBuf) cumulator.Cumulate(alloc, contiguous, @in);
            Assert.Equal(DATA_INITIAL, cumulation.Component(0).ToString(Encoding.ASCII));
            Assert.Equal(DATA_INCOMING, cumulation.Component(1).ToString(Encoding.ASCII));
            Assert.Equal(DATA_CUMULATED, cumulation.ToString(Encoding.ASCII));
            Assert.Equal(1, contiguous.RefCnt);
            Assert.Equal(1, @in.RefCnt);
            Assert.Equal(1, cumulation.RefCnt);
            cumulation.Release();
        }

        [Fact]
        public void Cumulate_compositeCumulation_inputAppendedAsANewComponent()
        {
            CompositeByteBuf composite = alloc.CompositeBuffer().AddComponent(contiguous, true);
            Assert.Same(composite, cumulator.Cumulate(alloc, composite, @in));
            Assert.Equal(DATA_INITIAL, composite.Component(0).ToString(Encoding.ASCII));
            Assert.Equal(DATA_INCOMING, composite.Component(1).ToString(Encoding.ASCII));
            Assert.Equal(DATA_CUMULATED, composite.ToString(Encoding.ASCII));
            Assert.Equal(1, contiguous.RefCnt);
            Assert.Equal(1, @in.RefCnt);
            Assert.Equal(1, composite.RefCnt);
            composite.Release();
        }

        [Fact]
        public void Cumulate_compositeCumulation_inputReleasedOnError()
        {
            CompositeByteBuf composite = new ReleasingThrowingCompositeByteBuf(
                    alloc, int.MaxValue, throwingCumulatorError, contiguous);
            try
            {
                NotSupportedException actualError = Assert.Throws<NotSupportedException>(() =>
                        cumulator.Cumulate(alloc, composite, @in));
                Assert.Same(throwingCumulatorError, actualError);
                Assert.Equal(0, @in.RefCnt);
                Assert.Equal(1, composite.RefCnt);
                Assert.Equal(1, contiguous.RefCnt);
            }
            finally
            {
                composite.Release();
            }
        }

        [Fact]
        public void Cumulate_contiguousCumulation_inputAndNewCompositeReleasedOnError()
        {
            CompositeByteBuf newComposite = new SecondAddThrowingCompositeByteBuf(alloc, throwingCumulatorError);
            Mock<IByteBufAllocator> mock = new Mock<IByteBufAllocator>();
            mock.Setup(a => a.CompositeBuffer(It.IsAny<int>())).Returns(newComposite);
            IByteBufAllocator mockAlloc = mock.Object;

            NotSupportedException actualError = Assert.Throws<NotSupportedException>(() =>
                    cumulator.Cumulate(mockAlloc, contiguous, @in));
            Assert.Same(throwingCumulatorError, actualError);
            Assert.Equal(0, @in.RefCnt);
            Assert.Equal(0, newComposite.RefCnt);
            Assert.Equal(1, contiguous.RefCnt);
        }

        // CLR: the anonymous CompositeByteBuf subclass is a named nested class.
        private sealed class SecondAddThrowingCompositeByteBuf(IByteBufAllocator alloc, Exception error)
            : CompositeByteBuf(alloc, false, int.MaxValue)
        {
            private int count;

            public override CompositeByteBuf AddFlattenedComponents(ByteBuf buffer, bool increaseWriterIndex = false)
            {
                if (++count == 2)
                {
                    buffer.Release();
                    throw error;
                }
                return base.AddFlattenedComponents(buffer, increaseWriterIndex);
            }
        }

        [Fact]
        public void MustNotReleaseNonOwnedCumulationIfAddInputFails()
        {
            NotSupportedException expectedError = new NotSupportedException();
            IByteBufAllocator throwingAlloc = new ThrowingAllocator(expectedError);

            AdaptiveCumulator cumulator = new AdaptiveCumulator(1024);
            IByteBufAllocator bufAlloc = new UnpooledByteBufAllocator(false);
            ByteBuf cumulation = bufAlloc.Buffer(4, 4).WriteBytes(Encoding.ASCII.GetBytes("0123"));
            ByteBuf @in = bufAlloc.Buffer().WriteBytes(Encoding.ASCII.GetBytes("456"));

            try
            {
                Assert.Same(expectedError, Record.Exception(() => cumulator.Cumulate(throwingAlloc, cumulation, @in)));
                Assert.Equal(0, @in.RefCnt, "Incoming buffer must be released on failure");
                Assert.Equal(1, cumulation.RefCnt,
                        "Caller-owned cumulation must NOT be released when the merge fails");
            }
            finally
            {
                if (cumulation.RefCnt > 0)
                {
                    cumulation.Release();
                }
            }
        }

        // CLR: the anonymous allocator is a named nested class.
        private sealed class ThrowingAllocator(Exception expectedError) : AbstractByteBufAllocator(false)
        {
            protected override ByteBuf NewHeapBuffer(int initialCapacity, int maxCapacity)
            {
                throw expectedError;
            }

            protected override ByteBuf NewDirectBuffer(int initialCapacity, int maxCapacity)
            {
                throw expectedError;
            }

            public override bool IsDirectBufferPooled => false;
        }
    }

    // CLR: @TestInstance(PER_CLASS) only lets the source use instance methods as @MethodSource; xUnit member data is
    // static, so the data constants are const.
    public class ShouldComposeTests
    {
        private const string DATA_INITIAL = "0123";
        private const string DATA_INCOMING = "456789";

        public static IEnumerable<object[]> ShouldComposeParams()
        {
            List<int> composeMinSizes = new List<int> { 0, 9, 10, 11, int.MaxValue };
            List<string> tailDatas = new List<string> { "", DATA_INITIAL };
            List<string> inDatas = new List<string> { "", DATA_INCOMING };

            foreach (int cms in composeMinSizes)
            {
                foreach (string td in tailDatas)
                {
                    foreach (string id in inDatas)
                    {
                        yield return new object[] { cms, td, id };
                    }
                }
            }
        }

        private CompositeByteBuf composite;
        private ByteBuf tail;
        private ByteBuf @in;
        private readonly IByteBufAllocator alloc = new UnpooledByteBufAllocator(false);

        private void SetUp(int composeMinSize, string tailData, string inData)
        {
            @in = ByteBufUtil.WriteAscii(alloc, inData);
            tail = ByteBufUtil.WriteAscii(alloc, tailData);
            composite = alloc.CompositeBuffer(int.MaxValue);
            composite.AddFlattenedComponents(tail, true);
        }

        private void TearDown()
        {
            if (composite != null && composite.RefCnt > 0)
            {
                composite.Release();
            }
        }

        [Theory]
        [MemberData(nameof(ShouldComposeParams))]
        public void ShouldCompose(int composeMinSize, string tailData, string inData)
        {
            SetUp(composeMinSize, tailData, inData);
            try
            {
                int initialComponents = composite.NumComponents;
                AdaptiveCumulator cumulator = new AdaptiveCumulator(composeMinSize);
                ByteBuf result = cumulator.Cumulate(alloc, composite, @in);

                if (!composite.IsReadable())
                {
                    Assert.Same(@in, result);
                    composite = null;
                    @in.Release();
                }
                else if (!@in.IsReadable())
                {
                    Assert.Same(composite, result);
                    Assert.Equal(initialComponents, composite.NumComponents);
                    Assert.Equal(tailData + inData, composite.ToString(Encoding.ASCII));
                }
                else if (tail.ReadableBytes + @in.ReadableBytes >= composeMinSize)
                {
                    composite = (CompositeByteBuf) result;
                    Assert.Equal(initialComponents + 1, composite.NumComponents);
                    Assert.Equal(tailData + inData, composite.ToString(Encoding.ASCII));
                }
                else
                {
                    composite = (CompositeByteBuf) result;
                    Assert.Equal(initialComponents, composite.NumComponents);
                    Assert.Equal(tailData + inData, composite.ToString(Encoding.ASCII));
                }
            }
            finally
            {
                TearDown();
            }
        }
    }

    public class MergeWithCompositeTailTests
    {
        private const string INCOMING_DATA_READABLE = "+incoming";
        private const string INCOMING_DATA_DISCARDABLE = "discard";

        private const string TAIL_DATA_DISCARDABLE = "---";
        private const string TAIL_DATA_READABLE = "tail";
        private const string TAIL_DATA = TAIL_DATA_DISCARDABLE + TAIL_DATA_READABLE;
        private readonly int TAIL_READER_INDEX = TAIL_DATA_DISCARDABLE.Length;
        private readonly int TAIL_MAX_CAPACITY = 128;

        private const string EXPECTED_TAIL_DATA = "tail+incoming";

        public static IEnumerable<object[]> MergeParams()
        {
            List<string> headDatas = new List<string> { "", "head" };
            List<int> readerIndexes = new List<int> { 0, 2, 6 }; // 0, head.length()-2, head.length()+2

            foreach (string hd in headDatas)
            {
                foreach (int ri in readerIndexes)
                {
                    yield return new object[] { hd, ri };
                }
            }
        }

        private CompositeByteBuf composite;
        private ByteBuf tail;
        private ByteBuf @in;
        private readonly IByteBufAllocator alloc = new PooledByteBufAllocator();

        private void SetUp(string compositeHeadData, int compositeReaderIndex)
        {
            composite = alloc.CompositeBuffer();

            ByteBuf head = alloc.Buffer().WriteBytes(Encoding.ASCII.GetBytes(compositeHeadData));
            composite.AddFlattenedComponents(head, true);

            tail = alloc.Buffer(TAIL_DATA.Length, TAIL_MAX_CAPACITY)
                    .WriteBytes(Encoding.ASCII.GetBytes(TAIL_DATA));
            tail.ReaderIndex = TAIL_READER_INDEX;

            @in = alloc.Buffer()
                    .WriteBytes(Encoding.ASCII.GetBytes(INCOMING_DATA_DISCARDABLE))
                    .WriteBytes(Encoding.ASCII.GetBytes(INCOMING_DATA_READABLE));
            @in.ReaderIndex = INCOMING_DATA_DISCARDABLE.Length;
        }

        private void TearDown()
        {
            if (composite != null && composite.RefCnt > 0)
            {
                composite.Release();
            }
        }

        private string Repeat(string s, int count)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                sb.Append(s);
            }
            return sb.ToString();
        }

        private void AssertTailExpanded(string expectedTailReadableData, int expectedNewTailCapacity,
                int compositeReaderIndex, string compositeHeadData)
        {
            int originalNumComponents = composite.NumComponents;
            int compositeReaderIndexBounded = Math.Min(compositeReaderIndex, composite.WriterIndex);
            composite.ReaderIndex = compositeReaderIndexBounded;
            bool wasReadable = composite.IsReadable();

            AdaptiveCumulator cumulator = new AdaptiveCumulator(int.MaxValue);
            ByteBuf result = cumulator.Cumulate(alloc, composite, @in);

            if (!wasReadable)
            {
                Assert.Same(@in, result, "When composite is fully read, cumulate should return in");
                Assert.Equal(0, composite.RefCnt, "Old composite should be released");
                @in.Release(); // Free the returned buffer manually
                return;
            }

            composite = (CompositeByteBuf) result;

            Assert.Equal(originalNumComponents, composite.NumComponents,
                    "When tail is expanded, the number of components in the cumulation must not change");

            ByteBuf newTail = composite.Component(composite.NumComponents - 1);

            Assert.Equal(expectedTailReadableData, newTail.ToString(Encoding.ASCII));
            Assert.Equal(expectedNewTailCapacity, newTail.Capacity);

            string newTailDataDiscardable = newTail.ToString(0, newTail.ReaderIndex, Encoding.ASCII);
            Assert.Equal("", newTailDataDiscardable,
                    "After tail expansion, its discardable bytes should be discarded");

            Assert.Equal(0, newTail.ReaderIndex);
            Assert.Equal(expectedTailReadableData.Length, newTail.WriterIndex);

            AssertExpectedCumulation(newTail, expectedTailReadableData, compositeReaderIndexBounded, compositeHeadData);

            Assert.False(@in.IsReadable(), "Incoming buffer is fully read");
            Assert.Equal(0, @in.RefCnt, "Incoming buffer is released");
        }

        private void AssertExpectedCumulation(ByteBuf newTail, string expectedTailReadable,
                int expectedReaderIndex, string compositeHeadData)
        {
            string expectedCumulationData = string.Concat(compositeHeadData, expectedTailReadable)
                    .Substring(expectedReaderIndex);
            Assert.Equal(expectedCumulationData, composite.ToString(Encoding.ASCII));

            int expectedCumulationCapacity = compositeHeadData.Length + expectedTailReadable.Length;
            Assert.Equal(expectedCumulationCapacity, composite.Capacity);
            Assert.Equal(expectedReaderIndex, composite.ReaderIndex);
            Assert.Equal(expectedCumulationCapacity, composite.WriterIndex);

            Assert.Equal(1, composite.RefCnt);
            Assert.Equal(1, newTail.RefCnt);
        }

        private void AssertTailReplaced(int compositeReaderIndex, string compositeHeadData)
        {
            int cumulationOriginalComponentsNum = composite.NumComponents;
            int taiOriginalRefCount = tail.RefCnt;
            string expectedTailReadable = tail.ToString(Encoding.ASCII) + @in.ToString(Encoding.ASCII);
            int expectedReallocatedTailCapacity = alloc.CalculateNewCapacity(
                    expectedTailReadable.Length, int.MaxValue);

            int compositeReaderIndexBounded = Math.Min(compositeReaderIndex, composite.WriterIndex);
            composite.ReaderIndex = compositeReaderIndexBounded;
            bool wasReadable = composite.IsReadable();

            AdaptiveCumulator cumulator = new AdaptiveCumulator(int.MaxValue);
            ByteBuf result = cumulator.Cumulate(alloc, composite, @in);

            if (!wasReadable)
            {
                Assert.Same(@in, result, "When composite is fully read, cumulate should return in");
                Assert.Equal(0, composite.RefCnt, "Old composite should be released");
                @in.Release(); // Free the returned buffer manually
                return;
            }

            composite = (CompositeByteBuf) result;

            Assert.Equal(cumulationOriginalComponentsNum, composite.NumComponents);
            ByteBuf replacedTail = composite.Component(composite.NumComponents - 1);

            Assert.Equal(0, @in.ReadableBytes);
            Assert.Equal(expectedTailReadable, replacedTail.ToString(Encoding.ASCII));

            Assert.Equal(0, replacedTail.ReaderIndex);
            Assert.Equal(expectedTailReadable.Length, replacedTail.WriterIndex);
            Assert.Equal(expectedReallocatedTailCapacity, replacedTail.Capacity);

            AssertExpectedCumulation(replacedTail, expectedTailReadable,
                    compositeReaderIndexBounded, compositeHeadData);

            Assert.False(@in.IsReadable(), "Incoming buffer is fully read");
            Assert.Equal(0, @in.RefCnt, "Incoming buffer is released");
            Assert.Equal(taiOriginalRefCount - 1, tail.RefCnt, "Replaced tail released once.");
        }

        [Theory]
        [MemberData(nameof(MergeParams))]
        public void MergeWithCompositeTail_tailExpandable_write(string compositeHeadData, int compositeReaderIndex)
        {
            SetUp(compositeHeadData, compositeReaderIndex);
            try
            {
                int fitCapacity = tail.Capacity + INCOMING_DATA_READABLE.Length;
                tail.Capacity = fitCapacity;
                int expectedCapacity = alloc.CalculateNewCapacity(fitCapacity, int.MaxValue);
                Assert.True(@in.ReadableBytes <= tail.WritableBytes);

                composite.AddFlattenedComponents(tail, true);
                AssertTailExpanded(EXPECTED_TAIL_DATA, expectedCapacity, compositeReaderIndex, compositeHeadData);
            }
            finally
            {
                TearDown();
            }
        }

        [Theory]
        [MemberData(nameof(MergeParams))]
        public void MergeWithCompositeTail_tailExpandable_fastWrite(
                string compositeHeadData, int compositeReaderIndex)
        {
            SetUp(compositeHeadData, compositeReaderIndex);
            try
            {
                int tailFastCapacity = alloc.CalculateNewCapacity(EXPECTED_TAIL_DATA.Length, int.MaxValue);

                composite.AddFlattenedComponents(tail, true);
                AssertTailExpanded(EXPECTED_TAIL_DATA, tailFastCapacity, compositeReaderIndex, compositeHeadData);
            }
            finally
            {
                TearDown();
            }
        }

        [Theory]
        [MemberData(nameof(MergeParams))]
        public void MergeWithCompositeTail_tailExpandable_reallocateInMemory(
                string compositeHeadData, int compositeReaderIndex)
        {
            SetUp(compositeHeadData, compositeReaderIndex);
            try
            {
                int tailFastCapacity = tail.WriterIndex + tail.MaxFastWritableBytes;
                string inSuffixOverFastBytes = Repeat("a", tailFastCapacity + 1);
                int newTailSize = tail.ReadableBytes + inSuffixOverFastBytes.Length;
                composite.AddFlattenedComponents(tail, true);

                @in.WriteCharSequence(inSuffixOverFastBytes, Encoding.ASCII);
                Assert.True(@in.ReadableBytes > tail.MaxFastWritableBytes);
                Assert.True(@in.ReadableBytes <= tail.MaxWritableBytes);

                int expectedTailCapacity = alloc.CalculateNewCapacity(newTailSize, int.MaxValue);
                AssertTailExpanded(string.Concat(EXPECTED_TAIL_DATA, inSuffixOverFastBytes),
                        expectedTailCapacity, compositeReaderIndex, compositeHeadData);
            }
            finally
            {
                TearDown();
            }
        }

        [Theory]
        [MemberData(nameof(MergeParams))]
        public void MergeWithCompositeTail_tailNotExpandable_maxCapacityReached(
                string compositeHeadData, int compositeReaderIndex)
        {
            SetUp(compositeHeadData, compositeReaderIndex);
            try
            {
                string tailSuffixFullCapacity = Repeat("a", tail.MaxWritableBytes);
                tail.WriteCharSequence(tailSuffixFullCapacity, Encoding.ASCII);
                composite.AddFlattenedComponents(tail, true);
                AssertTailReplaced(compositeReaderIndex, compositeHeadData);
            }
            finally
            {
                TearDown();
            }
        }

        [Theory]
        [MemberData(nameof(MergeParams))]
        public void MergeWithCompositeTail_tailNotExpandable_shared(
                string compositeHeadData, int compositeReaderIndex)
        {
            SetUp(compositeHeadData, compositeReaderIndex);
            try
            {
                tail.Retain();
                composite.AddFlattenedComponents(tail, true);
                AssertTailReplaced(compositeReaderIndex, compositeHeadData);
                tail.Release();
            }
            finally
            {
                TearDown();
            }
        }

        [Theory]
        [MemberData(nameof(MergeParams))]
        public void MergeWithCompositeTail_tailNotExpandable_readOnly(
                string compositeHeadData, int compositeReaderIndex)
        {
            SetUp(compositeHeadData, compositeReaderIndex);
            try
            {
                composite.AddFlattenedComponents(tail.AsReadOnly(), true);
                AssertTailReplaced(compositeReaderIndex, compositeHeadData);
            }
            finally
            {
                TearDown();
            }
        }

        [Theory]
        [MemberData(nameof(MergeParams))]
        public void MergeWithCompositeTail_tailExpandable_mergedReleaseOnThrow(
                string compositeHeadData, int compositeReaderIndex)
        {
            SetUp(compositeHeadData, compositeReaderIndex);
            NotSupportedException expectedError = new NotSupportedException();
            CompositeByteBuf compositeThrows = new ReleasingThrowingCompositeByteBuf(
                    alloc, int.MaxValue, expectedError, tail);

            try
            {
                NotSupportedException actualError = Assert.Throws<NotSupportedException>(() =>
                {
                    AdaptiveCumulator cumulator = new AdaptiveCumulator(int.MaxValue);
                    cumulator.Cumulate(alloc, compositeThrows, @in);
                });
                Assert.Same(expectedError, actualError);
                Assert.Equal(0, tail.RefCnt);
                Assert.Equal(1, compositeThrows.RefCnt);
                Assert.Equal(0, compositeThrows.NumComponents);
                Assert.Equal(0, @in.RefCnt);
            }
            finally
            {
                compositeThrows.Release();
                TearDown();
            }
        }

        [Theory]
        [MemberData(nameof(MergeParams))]
        public void MergeWithCompositeTail_tailNotExpandable_mergedReleaseOnThrow(
                string compositeHeadData, int compositeReaderIndex)
        {
            SetUp(compositeHeadData, compositeReaderIndex);
            NotSupportedException expectedError = new NotSupportedException();
            CompositeByteBuf compositeRo = new ReleasingThrowingCompositeByteBuf(
                    alloc, int.MaxValue, expectedError, tail.AsReadOnly());

            int newTailSize = tail.ReadableBytes + @in.ReadableBytes;
            ByteBuf newTail = alloc.Buffer(alloc.CalculateNewCapacity(newTailSize, int.MaxValue));
            Mock<IByteBufAllocator> mock = new Mock<IByteBufAllocator>();
            // CLR: buffer(int) is Buffer(initialCapacity, maxCapacity = int.MaxValue), so both arguments are matched.
            mock.Setup(a => a.Buffer(It.IsAny<int>(), It.IsAny<int>())).Returns(newTail);
            IByteBufAllocator mockAlloc = mock.Object;

            try
            {
                NotSupportedException actualError = Assert.Throws<NotSupportedException>(() =>
                {
                    AdaptiveCumulator cumulator = new AdaptiveCumulator(int.MaxValue);
                    cumulator.Cumulate(mockAlloc, compositeRo, @in);
                });
                Assert.Same(expectedError, actualError);
                Assert.Equal(1, compositeRo.RefCnt);
                Assert.Equal(0, compositeRo.NumComponents);
                Assert.Equal(0, newTail.RefCnt);
                Assert.Equal(0, @in.RefCnt);
            }
            finally
            {
                compositeRo.Release();
                if (newTail.RefCnt > 0)
                {
                    newTail.Release();
                }
                TearDown();
            }
        }
    }

    public class MergeWithCompositeTailMiscTests
    {
        private readonly IByteBufAllocator alloc = new PooledByteBufAllocator();

        [Fact]
        public void MergeWithCompositeTail_outOfSyncComposite()
        {
            AdaptiveCumulator cumulator = new AdaptiveCumulator(1024);

            ByteBuf buf = alloc.Buffer(32).WriteBytes(Encoding.ASCII.GetBytes("---01234"));

            CompositeByteBuf composite1 = alloc.CompositeBuffer(8).AddFlattenedComponents(buf.Retain(), true);
            Assert.Equal("---", composite1.ReadCharSequence(3, Encoding.ASCII).ToString());

            CompositeByteBuf composite2 = alloc.CompositeBuffer(8).AddFlattenedComponents(composite1, true);
            Assert.Equal("01234", composite2.ToString(Encoding.ASCII));

            CompositeByteBuf cumulation = (CompositeByteBuf) cumulator.Cumulate(alloc, composite2,
                    ByteBufUtil.WriteAscii(alloc, "56789"));
            Assert.Equal("0123456789", cumulation.ToString(Encoding.ASCII));

            Assert.Equal(1, cumulation.NumComponents);
            buf.SetByte(5, '*').SetByte(11, '$');
            Assert.Equal("0123456789", cumulation.ToString(Encoding.ASCII));
            cumulation.Release();
            buf.Release();
        }

        [Fact]
        public void MergeWithCompositeTail_detectsIndicesInNestedComposite()
        {
            // 1. Setup local allocator and a real cumulator that triggers merging.
            IByteBufAllocator alloc = new UnpooledByteBufAllocator(false);
            AdaptiveCumulator realCumulator = new AdaptiveCumulator(1024);

            // 2. Create a buffer with some "header" data we intend to skip.
            ByteBuf buf = alloc.Buffer(32).WriteBytes(Encoding.ASCII.GetBytes("SKIP01234"));

            // 3. Wrap it in a composite buffer and read the "SKIP" part.
            CompositeByteBuf innerComposite = alloc.CompositeBuffer()
                    .AddFlattenedComponents(buf, true);
            innerComposite.ReadCharSequence(4, Encoding.ASCII); // Discard "SKIP"

            // 4. Wrap that into a second composite (simulating cumulate() with refCnt > 1).
            // The readable content here is "01234".
            CompositeByteBuf outerComposite = alloc.CompositeBuffer()
                    .AddFlattenedComponents(innerComposite, true);

            try
            {
                // 5. Perform a cumulation that triggers mergeWithCompositeTail.
                // Use realCumulator to ensure the merge logic is executed.
                realCumulator.Cumulate(alloc, outerComposite,
                        ByteBufUtil.WriteAscii(alloc, "56789"));

                // Verify the "SKIP" bytes did not reappear.
                Assert.Equal("0123456789", outerComposite.ToString(Encoding.ASCII));

                // Verify the merge occurred (resulting in a single component).
                Assert.Equal(1, outerComposite.NumComponents);
            }
            finally
            {
                // Clean up to prevent leaks.
                outerComposite.Release();
            }
        }
    }

    // CLR: the source's two anonymous CompositeByteBuf subclasses that release the added buffer and throw are one
    // named nested class.
    private sealed class ReleasingThrowingCompositeByteBuf(
            IByteBufAllocator alloc, int maxNumComponents, Exception error, params ByteBuf[] buffers)
        : CompositeByteBuf(alloc, false, maxNumComponents, buffers)
    {
        public override CompositeByteBuf AddFlattenedComponents(ByteBuf buffer, bool increaseWriterIndex = false)
        {
            buffer.Release();
            throw error;
        }
    }
}
