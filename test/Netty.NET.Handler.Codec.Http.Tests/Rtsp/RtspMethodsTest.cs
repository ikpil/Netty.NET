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
using System.Globalization;
using Netty.NET.Handler.Codec.Rtsp;

namespace Netty.NET.Handler.Codec.Http.Tests.Rtsp;

// CLR: Locale.getDefault()/setDefault(Locale) are CultureInfo.CurrentCulture, the culture of culture-sensitive
// casing, which is "the culture used by the current thread and task-based asynchronous operations"
// (https://learn.microsoft.com/en-us/dotnet/api/system.globalization.cultureinfo.currentculture?view=net-10.0), so
// it is not process-global like the JVM default and JUnit's @Isolated has no counterpart (the mapping of core
// HttpVersionLocaleTest); new Locale("tr", "TR") is new CultureInfo("tr-TR"), and the original value is restored
// in finally as in Java. The package-private class and methods are public, as xUnit requires.
// assertSame/assertNotSame are Assert.Same/NotSame and assertThrows(IllegalArgumentException.class,...), which
// also accepts a subclass, is Assert.ThrowsAny<ArgumentException>. catch (IllegalArgumentException) is catch
// (ArgumentException).
public class RtspMethodsTest
{

    [Fact]
    public void ValueOfReturnsCachedInstanceForUppercaseName()
    {
        Assert.Same(RtspMethods.DESCRIBE, RtspMethods.ValueOf("DESCRIBE"));
        Assert.Same(RtspMethods.SETUP, RtspMethods.ValueOf("SETUP"));
        Assert.Same(RtspMethods.GET_PARAMETER, RtspMethods.ValueOf("GET_PARAMETER"));
    }

    [Fact]
    public void ValueOfNormalizesLowercaseInputUnderUsLocale()
    {
        Assert.Same(RtspMethods.DESCRIBE, RtspMethods.ValueOf("describe"));
        Assert.Same(RtspMethods.PLAY, RtspMethods.ValueOf("play"));
        Assert.Same(RtspMethods.REDIRECT, RtspMethods.ValueOf("redirect"));
    }

    [Fact]
    public void ValueOfNormalizesLowercaseInputUnderTurkishLocale()
    {
        // In Turkish locale (tr_TR), 'i' uppercases to 'İ' (U+0130) under the JVM default.
        // RtspMethods.valueOf must pin Locale.US so RTSP method names that contain 'i' such as
        // "describe" or "redirect" continue to resolve to the cached uppercase entries.
        CultureInfo original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            HttpMethod describe = RtspMethods.ValueOf("describe");
            HttpMethod redirect = RtspMethods.ValueOf("redirect");
            Assert.Same(RtspMethods.DESCRIBE, describe);
            Assert.Same(RtspMethods.REDIRECT, redirect);
            // Sanity-check: with the locale-default toUpperCase the names would have contained
            // U+0130 instead of plain 'I'. Asserting the resolved names round-trip to ASCII
            // pins down that the fix is taking effect.
            Assert.Equal("DESCRIBE", describe.Name());
            Assert.Equal("REDIRECT", redirect.Name());
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void ValueOfRejectsNulPaddedCachedMethodName()
    {
        Assert.ThrowsAny<ArgumentException>(() => RtspMethods.ValueOf("\u0000PLAY"));
    }

    [Fact]
    public void ValueOfRejectsNulPaddedCachedMethodNameTrailing()
    {
        Assert.ThrowsAny<ArgumentException>(() => RtspMethods.ValueOf("SETUP\u0000"));
    }

    [Fact]
    public void ValueOfRejectsCarriageReturnPaddedCachedMethodName()
    {
        Assert.ThrowsAny<ArgumentException>(() => RtspMethods.ValueOf("\rTEARDOWN"));
    }

    [Fact]
    public void ValueOfDoesNotReturnCachedConstantForControlBytePaddedName()
    {
        try
        {
            HttpMethod result = RtspMethods.ValueOf("\u0000PLAY");
            Assert.NotSame(RtspMethods.PLAY, result);
        }
        catch (ArgumentException)
        {
            // Correct outcome.
        }
    }
}
