/*
 * Copyright 2016 The Netty Project
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

using System.Collections.Generic;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels.Embedded;

#pragma warning disable xUnit2013 // assertEquals(n, size()) keeps the source assertions.

namespace Netty.NET.Handler.Codec.Smtp.Tests;

// CLR: List<CharSequence> is IList<ICharSequence> (size() is Count, get(i) the indexer); readInbound() states its type
// argument; Throwable.getCause() is Exception.InnerException; Integer.MAX_VALUE is int.MaxValue and
// CharsetUtil.US_ASCII is Encoding.ASCII (docs/codec-base-porting.md); the anonymous Executable instances are lambdas.
// The helper newBuffer(CharSequence) takes the string its callers pass, since Unpooled.CopiedBuffer has a string
// overload and no ICharSequence overload.
public class SmtpResponseDecoderTest
{

    [Fact]
    public void TestDecodeOneLineResponse()
    {
        EmbeddedChannel channel = NewChannel();
        Assert.True(channel.WriteInbound(NewBuffer("200 Ok\r\n")));
        Assert.True(channel.Finish());

        ISmtpResponse response = channel.ReadInbound<ISmtpResponse>();
        Assert.Equal(200, response.Code());
        IList<ICharSequence> sequences = response.Details();
        Assert.Equal(1, sequences.Count);

        Assert.Equal("Ok", sequences[0].ToString());
        Assert.Null(channel.ReadInbound());
    }

    [Fact]
    public void TestDecodeOneLineResponseNoDetails()
    {
        EmbeddedChannel channel = NewChannel();
        Assert.True(channel.WriteInbound(NewBuffer("250 \r\n")));
        Assert.True(channel.Finish());

        ISmtpResponse response = channel.ReadInbound<ISmtpResponse>();
        Assert.Equal(250, response.Code());
        IList<ICharSequence> sequences = response.Details();
        Assert.Equal(0, sequences.Count);
    }

    [Fact]
    public void TestDecodeOneLineResponseChunked()
    {
        EmbeddedChannel channel = NewChannel();
        Assert.False(channel.WriteInbound(NewBuffer("200 Ok")));
        Assert.True(channel.WriteInbound(NewBuffer("\r\n")));
        Assert.True(channel.Finish());

        ISmtpResponse response = channel.ReadInbound<ISmtpResponse>();
        Assert.Equal(200, response.Code());
        IList<ICharSequence> sequences = response.Details();
        Assert.Equal(1, sequences.Count);

        Assert.Equal("Ok", sequences[0].ToString());
        Assert.Null(channel.ReadInbound());
    }

    [Fact]
    public void TestDecodeTwoLineResponse()
    {
        EmbeddedChannel channel = NewChannel();
        Assert.True(channel.WriteInbound(NewBuffer("200-Hello\r\n200 Ok\r\n")));
        Assert.True(channel.Finish());

        ISmtpResponse response = channel.ReadInbound<ISmtpResponse>();
        Assert.Equal(200, response.Code());
        IList<ICharSequence> sequences = response.Details();
        Assert.Equal(2, sequences.Count);

        Assert.Equal("Hello", sequences[0].ToString());
        Assert.Equal("Ok", sequences[1].ToString());
        Assert.Null(channel.ReadInbound());
    }

    [Fact]
    public void TestDecodeTwoLineResponseChunked()
    {
        EmbeddedChannel channel = NewChannel();
        Assert.False(channel.WriteInbound(NewBuffer("200-")));
        Assert.False(channel.WriteInbound(NewBuffer("Hello\r\n2")));
        Assert.False(channel.WriteInbound(NewBuffer("00 Ok")));
        Assert.True(channel.WriteInbound(NewBuffer("\r\n")));
        Assert.True(channel.Finish());

        ISmtpResponse response = channel.ReadInbound<ISmtpResponse>();
        Assert.Equal(200, response.Code());
        IList<ICharSequence> sequences = response.Details();
        Assert.Equal(2, sequences.Count);

        Assert.Equal("Hello", sequences[0].ToString());
        Assert.Equal("Ok", sequences[1].ToString());
        Assert.Null(channel.ReadInbound());
    }

    [Fact]
    public void TestDecodeInvalidSeparator()
    {
        EmbeddedChannel channel = NewChannel();
        Assert.Throws<DecoderException>(() =>
        {
            channel.WriteInbound(NewBuffer("200:Ok\r\n"));
        });
    }

    [Fact]
    public void TestDecodeInvalidCode()
    {
        EmbeddedChannel channel = NewChannel();
        Assert.Throws<DecoderException>(() =>
        {
            channel.WriteInbound(NewBuffer("xyz Ok\r\n"));
        });
    }

    [Fact]
    public void TestDecodeLineWithOnlyCodeAndNoSeparator()
    {
        EmbeddedChannel channel = NewChannel();
        // A line consisting of just the 3-digit code (no separator, no detail) must be rejected as an invalid
        // line rather than crash the decoder: ByteToMessageDecoder wraps any *unexpected* exception thrown out
        // of decode() (e.g. an IndexOutOfBoundsException from reading past the line) into a DecoderException
        // with that exception as its cause, whereas the intentional "invalid line" rejection path throws a
        // DecoderException with no cause. Asserting there is no cause distinguishes the two.
        DecoderException exception = Assert.Throws<DecoderException>(() =>
        {
            channel.WriteInbound(NewBuffer("250\r\n"));
        });
        Assert.Null(exception.InnerException);
        channel.FinishAndReleaseAll();
    }

    [Fact]
    public void TestDecodeInvalidLine()
    {
        EmbeddedChannel channel = NewChannel();
        Assert.Throws<DecoderException>(() =>
        {
            channel.WriteInbound(NewBuffer("Ok\r\n"));
        });
    }

    [Fact]
    public void TestDecodeMultiLineResponseExceedingMaxResponseSize()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new SmtpResponseDecoder(int.MaxValue, 256));
        Assert.Throws<TooLongFrameException>(() =>
        {
            for (int i = 0; i < 1000; i++)
            {
                channel.WriteInbound(NewBuffer("250-A\r\n"));
            }
        });
    }

    [Fact]
    public void TestDecodeMultiLineResponseWithinMaxResponseSize()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new SmtpResponseDecoder(int.MaxValue, 256));
        Assert.True(channel.WriteInbound(NewBuffer("250-Hello\r\n250-World\r\n250 Ok\r\n")));
        Assert.True(channel.Finish());

        ISmtpResponse response = channel.ReadInbound<ISmtpResponse>();
        Assert.Equal(250, response.Code());
        Assert.Equal(3, response.Details().Count);
        Assert.Null(channel.ReadInbound());
    }

    [Fact]
    public void TestMaxResponseSizeAppliesPerResponse()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new SmtpResponseDecoder(int.MaxValue, 256));

        for (int i = 0; i < 100; i++)
        {
            Assert.True(channel.WriteInbound(NewBuffer("250-Hello\r\n250 Ok\r\n")));
            ISmtpResponse response = channel.ReadInbound<ISmtpResponse>();
            Assert.Equal(2, response.Details().Count);
        }

        Assert.False(channel.Finish());
    }

    private static EmbeddedChannel NewChannel()
    {
        return new EmbeddedChannel(new SmtpResponseDecoder(int.MaxValue));
    }

    private static ByteBuf NewBuffer(string seq)
    {
        return Unpooled.CopiedBuffer(seq, Encoding.ASCII);
    }
}
