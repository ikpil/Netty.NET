/*
 * Copyright 2017 The Netty Project
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
using Netty.NET.Common;

namespace Netty.NET.Handler.Codec.Smtp.Tests;

// CLR: CharSequence is ICharSequence (see SmtpUtils), so the Java String arguments are passed as StringCharSequence;
// @ParameterizedTest with @ValueSource(strings) is [Theory] with one [InlineData] per string; IllegalArgumentException is
// ArgumentException (docs/codec-base-porting.md). The package-private isContentExpected() is internal and visible to
// this assembly.
public class SmtpCommandTest
{
    [Fact]
    public void GetCommandFromCache()
    {
        Assert.Same(SmtpCommand.DATA, SmtpCommand.ValueOf(new StringCharSequence("DATA")));
        Assert.Same(SmtpCommand.EHLO, SmtpCommand.ValueOf(new StringCharSequence("EHLO")));
        Assert.NotSame(SmtpCommand.EHLO, SmtpCommand.ValueOf(new StringCharSequence("ehlo")));
    }

    [Fact]
    public void EqualsIgnoreCase()
    {
        Assert.Equal(SmtpCommand.MAIL, SmtpCommand.ValueOf(new StringCharSequence("mail")));
        Assert.Equal(SmtpCommand.ValueOf(new StringCharSequence("test")), SmtpCommand.ValueOf(new StringCharSequence("TEST")));
    }

    [Fact]
    public void IsContentExpected()
    {
        Assert.True(SmtpCommand.ValueOf(new StringCharSequence("DATA")).IsContentExpected());
        Assert.True(SmtpCommand.ValueOf(new StringCharSequence("data")).IsContentExpected());

        Assert.False(SmtpCommand.HELO.IsContentExpected());
        Assert.False(SmtpCommand.HELP.IsContentExpected());
        Assert.False(SmtpCommand.ValueOf(new StringCharSequence("DATA2")).IsContentExpected());
    }

    [Theory]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\n\r")]
    public void MustRejectCRLFInCommandName(string sep)
    {
        Assert.Throws<ArgumentException>(() => SmtpCommand.ValueOf(new StringCharSequence(sep + "EHLO")));
        Assert.Throws<ArgumentException>(() => SmtpCommand.ValueOf(new StringCharSequence("EH" + sep + "LO")));
        Assert.Throws<ArgumentException>(() => SmtpCommand.ValueOf(new StringCharSequence("EHLO" + sep)));
    }
}
