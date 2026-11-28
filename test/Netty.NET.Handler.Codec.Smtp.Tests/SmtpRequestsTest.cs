/*
 * Copyright 2025 The Netty Project
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

// CLR: CharSequence is ICharSequence (see SmtpUtils), so the Java String arguments are passed as
// StringCharSequence.
public class SmtpRequestsTest
{
    [Fact]
    public void TestSmtpInjectionWithCarriageReturn()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            SmtpRequests.Mail(new StringCharSequence("test@example.com\rQUIT"));
        });
    }

    [Fact]
    public void TestSmtpInjectionWithLineFeed()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            SmtpRequests.Mail(new StringCharSequence("test@example.com\nQUIT"));
        });
    }

    [Fact]
    public void TestSmtpInjectionWithCRLF()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            SmtpRequests.Rcpt(new StringCharSequence("test@example.com\r\nQUIT"));
        });
    }

    [Fact]
    public void TestSmtpInjectionInAuthParameter()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            SmtpRequests.Auth(new StringCharSequence("PLAIN"), new StringCharSequence("dGVzdA\rQUIT"));
        });
    }

    [Fact]
    public void TestSmtpInjectionInHelo()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            SmtpRequests.Helo(new StringCharSequence("localhost\r\nQUIT"));
        });
    }
}
