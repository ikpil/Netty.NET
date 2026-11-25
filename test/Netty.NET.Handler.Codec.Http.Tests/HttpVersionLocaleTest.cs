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

using System.Globalization;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http.Tests;

/**
 * Tests that exercise {@link HttpVersion} parsing with the JVM-default Locale flipped to a value
 * that exposes the Turkish dotted-I problem (U+0130). {@link Locale#setDefault} is process-global
 * mutable state, so this class is marked {@link Isolated} to keep it from leaking into the rest
 * of the codec-http suite, which runs in concurrent mode
 * ({@code junit.jupiter.execution.parallel.mode.default = concurrent}).
 */
// CLR: Locale.getDefault()/setDefault(Locale) are CultureInfo.CurrentCulture, the culture that culture-sensitive
// casing such as string.ToUpper() uses. It is "the culture used by the current thread and task-based asynchronous
// operations" and "a property of the executing thread"
// (https://learn.microsoft.com/en-us/dotnet/api/system.globalization.cultureinfo.currentculture?view=net-10.0), so it is
// not process-global like the JVM default and JUnit's @Isolated has no counterpart; the original value is restored in
// finally as in Java. The package-private class and methods are public, as xUnit requires.
public class HttpVersionLocaleTest
{

    [Fact]
    public void TestLowercaseIotaProtocolNameUnderTurkishLocale()
    {
        // Turkish locale maps 'i' -> 'İ' (U+0130) under the JVM-default toUpperCase().
        // The constructor must use Locale.US so an HTTP-derived protocol name like "icap"
        // round-trips to the ASCII "ICAP" instead of being corrupted to "İCAP".
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            HttpVersion version = HttpVersion.ValueOf("icap/1.0");
            Assert.Equal("ICAP", version.ProtocolName());
            for (int i = 0; i < version.ProtocolName().Length; i++)
            {
                Assert.True(version.ProtocolName()[i] < 0x80,
                        "protocolName must remain ASCII regardless of JVM default locale");
            }
            Assert.Equal("ICAP/1.0", version.Text());
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void TestProtocolNameConstructorUnderTurkishLocale()
    {
        // Same Locale.US guarantee for the (protocolName, major, minor, ...) constructor.
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            HttpVersion version = new HttpVersion("icap", 1, 0, true);
            Assert.Equal("ICAP", version.ProtocolName());
            Assert.Equal("ICAP/1.0", version.Text());
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
