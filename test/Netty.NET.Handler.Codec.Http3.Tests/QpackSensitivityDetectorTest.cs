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

using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http3.Tests;

// CLR: the package-private JUnit class and methods are public, because xUnit runs public test classes and methods. A
// Java String passed as a CharSequence is a StringCharSequence (Cs), because a C# string is not an ICharSequence
// (docs/codec-http-porting.md, Shared CLR adaptations). The lambda detector of customDetectorFlagsCredentialHeaders is
// the private nested class CredentialHeaderDetector (IQpackSensitivityDetector is an interface, not a delegate), and
// String.toLowerCase() (default locale) is ToLower() (current culture); "authorization".equals(n) is string equality.
public class QpackSensitivityDetectorTest
{

    [Fact]
    public void NeverSensitiveAlwaysReturnsFalse()
    {
        Assert.NotNull(IQpackSensitivityDetector.NEVER_SENSITIVE);
        Assert.False(IQpackSensitivityDetector.NEVER_SENSITIVE.IsSensitive(Cs("authorization"), Cs("Bearer x")));
        Assert.False(IQpackSensitivityDetector.NEVER_SENSITIVE.IsSensitive(Cs("cookie"), Cs("sid=abc")));
        Assert.False(IQpackSensitivityDetector.NEVER_SENSITIVE.IsSensitive(Cs(""), Cs("")));
    }

    [Fact]
    public void AlwaysSensitiveAlwaysReturnsTrue()
    {
        Assert.NotNull(IQpackSensitivityDetector.ALWAYS_SENSITIVE);
        Assert.True(IQpackSensitivityDetector.ALWAYS_SENSITIVE.IsSensitive(Cs("authorization"), Cs("Bearer x")));
        Assert.True(IQpackSensitivityDetector.ALWAYS_SENSITIVE.IsSensitive(Cs(":path"), Cs("/")));
        Assert.True(IQpackSensitivityDetector.ALWAYS_SENSITIVE.IsSensitive(Cs(""), Cs("")));
    }

    [Fact]
    public void CustomDetectorFlagsCredentialHeaders()
    {
        IQpackSensitivityDetector detector = new CredentialHeaderDetector();

        Assert.True(detector.IsSensitive(Cs("Authorization"), Cs("Bearer secret")));
        Assert.True(detector.IsSensitive(Cs("cookie"), Cs("sid=abc")));
        Assert.True(detector.IsSensitive(Cs("Set-Cookie"), Cs("sid=abc; HttpOnly")));
        Assert.True(detector.IsSensitive(Cs("Proxy-Authorization"), Cs("Basic xxx")));

        Assert.False(detector.IsSensitive(Cs("content-type"), Cs("text/plain")));
        Assert.False(detector.IsSensitive(Cs(":method"), Cs("GET")));
    }

    // CLR: the lambda (name, value) -> { ... } of customDetectorFlagsCredentialHeaders.
    private sealed class CredentialHeaderDetector : IQpackSensitivityDetector
    {
        public bool IsSensitive(ICharSequence name, ICharSequence value)
        {
            string n = name.ToString().ToLower();
            return "authorization".Equals(n)
                    || "cookie".Equals(n)
                    || "set-cookie".Equals(n)
                    || "proxy-authorization".Equals(n);
        }
    }

    // CLR: a Java String passed as a CharSequence (see the class comment).
    private static ICharSequence Cs(string s)
    {
        return new StringCharSequence(s);
    }
}
