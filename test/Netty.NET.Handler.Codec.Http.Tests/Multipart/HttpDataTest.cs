/*
 * Copyright 2021 The Netty Project
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
using System.IO;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Http.Multipart;

namespace Netty.NET.Handler.Codec.Http.Tests.Multipart;

// CLR: the meta-annotation @ParameterizedHttpDataTest (@ParameterizedTest with @MethodSource("data")) has no xUnit
// counterpart, so each of its methods is a [Theory] with [MemberData(nameof(Data))]; xUnit enumerates the
// non-serializable IHttpData rows when the theory runs, so every theory gets new instances, as data() is called
// for each JUnit method; xUnit names each row with its argument, as JUnit's name = "{displayName}({0})" does. The
// static @BeforeAll setUp() runs from the static constructor; ThreadLocalRandom.nextBytes is
// Random.Shared.NextBytes (only the size matters). PooledByteBufAllocator.DEFAULT is
// PooledByteBufAllocator.Default, CharsetUtil.UTF_8 Encoding.UTF8, and refCnt() RefCnt. retainedDuplicate()
// returns IByteBufHolder through the IHttpData interface (narrowing re-declarations are inherited, owner decision
// 2026-11-19, docs/transport-porting.md, Decisions), so the result is cast to IHttpData. AssertJ
// assertThatExceptionOfType(IOException.class).isThrownBy(...).withMessage(text) is Assert.ThrowsAny<IOException>
// (a subclass also matches) with Assert.Equal on the message. The test methods with package access (no modifier in
// JUnit 5) are public, as xUnit requires.
public class HttpDataTest
{
    private static readonly byte[] BYTES = new byte[64];

    public static TheoryData<IHttpData> Data()
    {
        return new TheoryData<IHttpData>
        {
                new MemoryAttribute("test", 10),
                new MemoryFileUpload("test", "", "text/plain", null, Encoding.UTF8, 10),
                new MixedAttribute("test", 10, -1),
                new MixedFileUpload("test", "", "text/plain", null, Encoding.UTF8, 10, -1),
                new DiskAttribute("test", 10),
                new DiskFileUpload("test", "", "text/plain", null, Encoding.UTF8, 10)
        };
    }

    static HttpDataTest()
    {
        SetUp();
    }

    private static void SetUp()
    {
        Random.Shared.NextBytes(BYTES);
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestAddContentEmptyBuffer(IHttpData httpData)
    {
        ByteBuf content = PooledByteBufAllocator.Default.Buffer();
        httpData.AddContent(content, false);
        Assert.Equal(0, content.RefCnt);
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestCompletedFlagPreservedAfterRetainDuplicate(IHttpData httpData)
    {
        httpData.AddContent(Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes("foo")), false);
        Assert.False(httpData.IsCompleted());
        IHttpData duplicate = (IHttpData) httpData.RetainedDuplicate();
        Assert.False(duplicate.IsCompleted());
        Assert.True(duplicate.Release());
        httpData.AddContent(Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes("bar")), true);
        Assert.True(httpData.IsCompleted());
        duplicate = (IHttpData) httpData.RetainedDuplicate();
        Assert.True(duplicate.IsCompleted());
        Assert.True(duplicate.Release());
    }

    [Fact]
    public void TestAddContentExceedsDefinedSizeDiskFileUpload()
    {
        DoTestAddContentExceedsSize(
                new DiskFileUpload("test", "", "application/json", null, Encoding.UTF8, 10),
                "Out of size: 64 > 10");
    }

    [Fact]
    public void TestAddContentExceedsDefinedSizeMemoryFileUpload()
    {
        DoTestAddContentExceedsSize(
                new MemoryFileUpload("test", "", "application/json", null, Encoding.UTF8, 10),
                "Out of size: 64 > 10");
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestAddContentExceedsMaxSize(IHttpData httpData)
    {
        httpData.SetMaxSize(10);
        DoTestAddContentExceedsSize(httpData, "Size exceed allowed maximum capacity");
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestSetContentExceedsDefinedSize(IHttpData httpData)
    {
        DoTestSetContentExceedsSize(httpData, "Out of size: 64 > 10");
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestSetContentExceedsMaxSize(IHttpData httpData)
    {
        httpData.SetMaxSize(10);
        DoTestSetContentExceedsSize(httpData, "Size exceed allowed maximum capacity");
    }

    private static void DoTestAddContentExceedsSize(IHttpData httpData, string expectedMessage)
    {
        ByteBuf content = PooledByteBufAllocator.Default.Buffer();
        content.WriteBytes(BYTES);

        IOException e = Assert.ThrowsAny<IOException>(() => httpData.AddContent(content, false));
        Assert.Equal(expectedMessage, e.Message);

        Assert.Equal(0, content.RefCnt);
    }

    private static void DoTestSetContentExceedsSize(IHttpData httpData, string expectedMessage)
    {
        ByteBuf content = PooledByteBufAllocator.Default.Buffer();
        content.WriteBytes(BYTES);

        IOException e = Assert.ThrowsAny<IOException>(() => httpData.SetContent(content));
        Assert.Equal(expectedMessage, e.Message);

        Assert.Equal(0, content.RefCnt);
    }
}
