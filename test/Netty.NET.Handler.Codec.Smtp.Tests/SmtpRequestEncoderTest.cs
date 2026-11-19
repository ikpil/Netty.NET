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

using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Smtp.Tests;

// CLR: CharSequence is ICharSequence (see SmtpUtils), so the Java String arguments are passed as StringCharSequence;
// CharsetUtil.US_ASCII is Encoding.ASCII (docs/codec-base-porting.md); readOutbound() states its type argument; the
// anonymous Executable instance is a lambda.
public class SmtpRequestEncoderTest
{

    [Fact]
    public void TestEncodeEhlo()
    {
        TestEncode(SmtpRequests.Ehlo(new StringCharSequence("localhost")), "EHLO localhost\r\n");
    }

    [Fact]
    public void TestEncodeHelo()
    {
        TestEncode(SmtpRequests.Helo(new StringCharSequence("localhost")), "HELO localhost\r\n");
    }

    [Fact]
    public void TestEncodeAuth()
    {
        TestEncode(SmtpRequests.Auth(new StringCharSequence("LOGIN")), "AUTH LOGIN\r\n");
    }

    [Fact]
    public void TestEncodeAuthWithParameter()
    {
        TestEncode(SmtpRequests.Auth(new StringCharSequence("PLAIN"), new StringCharSequence("dGVzdAB0ZXN0ADEyMzQ=")),
                   "AUTH PLAIN dGVzdAB0ZXN0ADEyMzQ=\r\n");
    }

    [Fact]
    public void TestEncodeEmpty()
    {
        TestEncode(SmtpRequests.Empty(new StringCharSequence("dGVzdAB0ZXN0ADEyMzQ=")),  "dGVzdAB0ZXN0ADEyMzQ=\r\n");
    }

    [Fact]
    public void TestEncodeMail()
    {
        TestEncode(SmtpRequests.Mail(new StringCharSequence("me@netty.io")), "MAIL FROM:<me@netty.io>\r\n");
    }

    [Fact]
    public void TestEncodeMailNullSender()
    {
        TestEncode(SmtpRequests.Mail(null), "MAIL FROM:<>\r\n");
    }

    [Fact]
    public void TestEncodeRcpt()
    {
        TestEncode(SmtpRequests.Rcpt(new StringCharSequence("me@netty.io")), "RCPT TO:<me@netty.io>\r\n");
    }

    [Fact]
    public void TestEncodeNoop()
    {
        TestEncode(SmtpRequests.Noop(), "NOOP\r\n");
    }

    [Fact]
    public void TestEncodeRset()
    {
        TestEncode(SmtpRequests.Rset(), "RSET\r\n");
    }

    [Fact]
    public void TestEncodeHelp()
    {
        TestEncode(SmtpRequests.Help(null), "HELP\r\n");
    }

    [Fact]
    public void TestEncodeHelpWithArg()
    {
        TestEncode(SmtpRequests.Help("MAIL"), "HELP MAIL\r\n");
    }

    [Fact]
    public void TestEncodeData()
    {
        TestEncode(SmtpRequests.Data(), "DATA\r\n");
    }

    [Fact]
    public void TestEncodeDataAndContent()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new SmtpRequestEncoder());
        Assert.True(channel.WriteOutbound(SmtpRequests.Data()));
        Assert.True(channel.WriteOutbound(
                new DefaultSmtpContent(Unpooled.CopiedBuffer("Subject: Test\r\n\r\n", Encoding.ASCII))));
        Assert.True(channel.WriteOutbound(
                new DefaultLastSmtpContent(Unpooled.CopiedBuffer("Test\r\n", Encoding.ASCII))));
        Assert.True(channel.Finish());

        Assert.Equal("DATA\r\nSubject: Test\r\n\r\nTest\r\n.\r\n", GetWrittenString(channel));
    }

    [Fact]
    public void TestThrowsIfContentExpected()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new SmtpRequestEncoder());
        try
        {
            Assert.True(channel.WriteOutbound(SmtpRequests.Data()));
            Assert.Throws<EncoderException>(() =>
            {
                channel.WriteOutbound(SmtpRequests.Noop());
            });
        }
        finally
        {
            channel.FinishAndReleaseAll();
        }
    }

    [Fact]
    public void TestRsetClearsContentExpectedFlag()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new SmtpRequestEncoder());

        Assert.True(channel.WriteOutbound(SmtpRequests.Data()));
        Assert.True(channel.WriteOutbound(SmtpRequests.Rset()));
        Assert.True(channel.WriteOutbound(SmtpRequests.Noop()));
        Assert.True(channel.Finish());

        Assert.Equal("DATA\r\nRSET\r\nNOOP\r\n", GetWrittenString(channel));
    }

    private static string GetWrittenString(EmbeddedChannel channel)
    {
        ByteBuf written = Unpooled.Buffer();

        for (;;)
        {
            ByteBuf buffer = channel.ReadOutbound<ByteBuf>();
            if (buffer == null)
            {
                break;
            }
            written.WriteBytes(buffer);
            buffer.Release();
        }

        string writtenString = written.ToString(Encoding.ASCII);
        written.Release();

        return writtenString;
    }

    private static void TestEncode(ISmtpRequest request, string expected)
    {
        EmbeddedChannel channel = new EmbeddedChannel(new SmtpRequestEncoder());
        Assert.True(channel.WriteOutbound(request));
        Assert.True(channel.Finish());
        ByteBuf buffer = channel.ReadOutbound<ByteBuf>();
        Assert.Equal(expected, buffer.ToString(Encoding.ASCII));
        buffer.Release();
        Assert.Null(channel.ReadOutbound());
    }
}
