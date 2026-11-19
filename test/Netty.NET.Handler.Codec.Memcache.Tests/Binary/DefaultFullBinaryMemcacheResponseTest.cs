/*
 * Copyright 2019 The Netty Project
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

using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Memcache.Binary;

namespace Netty.NET.Handler.Codec.Memcache.Tests.Binary;

// CLR: xUnit creates a new test class instance for every test, so the @BeforeEach setUp() runs from the constructor
// (https://xunit.net/docs/shared-context); CharsetUtil.UTF_8 is Encoding.UTF8 (docs/codec-base-porting.md). The
// package-private setters are internal and visible to this test assembly through InternalsVisibleTo.
public class DefaultFullBinaryMemcacheResponseTest
{

    private DefaultFullBinaryMemcacheResponse response;

    public DefaultFullBinaryMemcacheResponseTest()
    {
        SetUp();
    }

    private void SetUp()
    {
        response = new DefaultFullBinaryMemcacheResponse(
                Unpooled.CopiedBuffer("key", Encoding.UTF8),
                Unpooled.WrappedBuffer(new byte[]{1, 3, 4, 9}),
                Unpooled.CopiedBuffer("some value", Encoding.UTF8));
        response.SetStatus((short) 1);
        response.SetMagic((byte) 0x03);
        response.SetOpcode((byte) 0x02);
        response.SetKeyLength((short) 32);
        response.SetExtrasLength((byte) 34);
        response.SetDataType((byte) 43);
        response.SetTotalBodyLength(345);
        response.SetOpaque(3);
        response.SetCas(345345L);
    }

    [Fact]
    public void FullCopy()
    {
        IFullBinaryMemcacheResponse newInstance = response.Copy();
        try
        {
            AssertResponseEquals(response, response.Content, newInstance);
        }
        finally
        {
            response.Release();
            newInstance.Release();
        }
    }

    [Fact]
    public void FullDuplicate()
    {
        try
        {
            AssertResponseEquals(response, response.Content, response.Duplicate());
        }
        finally
        {
            response.Release();
        }
    }

    [Fact]
    public void FullReplace()
    {
        ByteBuf newContent = Unpooled.CopiedBuffer("new value", Encoding.UTF8);
        IFullBinaryMemcacheResponse newInstance = response.Replace(newContent);
        try
        {
            AssertResponseEquals(response, newContent, newInstance);
        }
        finally
        {
            response.Release();
            newInstance.Release();
        }
    }

    private void AssertResponseEquals(IFullBinaryMemcacheResponse expected, ByteBuf expectedContent,
                            IFullBinaryMemcacheResponse actual)
    {
        Assert.NotSame(expected, actual);

        Assert.Equal(expected.Key(), actual.Key());
        Assert.Equal(expected.Extras(), actual.Extras());
        Assert.Equal(expectedContent, actual.Content);

        Assert.Equal(expected.Status(), actual.Status());
        Assert.Equal(expected.Magic(), actual.Magic());
        Assert.Equal(expected.Opcode(), actual.Opcode());
        Assert.Equal(expected.KeyLength(), actual.KeyLength());
        Assert.Equal(expected.ExtrasLength(), actual.ExtrasLength());
        Assert.Equal(expected.DataType(), actual.DataType());
        Assert.Equal(expected.TotalBodyLength(), actual.TotalBodyLength());
        Assert.Equal(expected.Opaque(), actual.Opaque());
        Assert.Equal(expected.Cas(), actual.Cas());
    }
}
