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
using System.Globalization;
using Netty.NET.Handler.Codec.Http.Multipart;
using static Netty.NET.Handler.Codec.Http.HttpMethod;
using static Netty.NET.Handler.Codec.Http.HttpVersion;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http.Tests.Multipart;

/**
 * Encoder-side test that flips {@link Locale#setDefault(Locale)} to Turkish to exercise the
 * {@code Content-Type} de-duplication path inside
 * {@link HttpPostRequestEncoder#finalizeRequest()}. Marked {@link Isolated} because the JVM
 * default Locale is process-global state and codec-http runs tests with
 * {@code junit.jupiter.execution.parallel.mode.default = concurrent}.
 */
// CLR: Locale.getDefault()/setDefault(Locale) are CultureInfo.CurrentCulture, "the culture used by the current thread
// and task-based asynchronous operations"
// (https://learn.microsoft.com/en-us/dotnet/api/system.globalization.cultureinfo.currentculture?view=net-10.0), so it
// is not process-global like the JVM default and @Isolated has no counterpart (as core's HttpVersionLocaleTest); the
// original value is restored in finally as in Java. The static import CONTENT_TYPE is HttpHeaderNames.CONTENT_TYPE;
// List<String> is IList<string>. assertEquals(1, list.size(), message) is AssertM.Equal on the Count (the message
// overloads of XunitAssertMessages), and startsWith is StartsWith with StringComparison.Ordinal. The package-private
// class and method are public, as xUnit requires.
public class HttpPostMultipartLocaleEncoderTest
{

    [Fact]
    public void TestFinalizeRemovesPreexistingMultipartContentTypeUnderTurkishLocale()
    {
        // Repro: a caller pre-sets `Content-Type: MULTIPART/form-data` (uppercase, RFC-allowed,
        // case-insensitive). When the JVM default Locale is Turkish, the encoder's
        // contentType.toLowerCase() used to map 'I' -> 'ı' (U+0131), the resulting
        // "multıpart/form-data" missed the prefix check, and the original mixed-case header was
        // left alongside the new multipart Content-Type the encoder is about to set - producing
        // two Content-Type headers on the request.
        CultureInfo original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            DefaultFullHttpRequest request = new DefaultFullHttpRequest(HTTP_1_1, POST, "http://localhost");
            request.Headers().Add(HttpHeaderNames.CONTENT_TYPE, "MULTIPART/form-data; boundary=preexisting");
            HttpPostRequestEncoder encoder = new HttpPostRequestEncoder(request, true);
            encoder.FinalizeRequest();
            IList<string> contentTypes = request.Headers().GetAll(HttpHeaderNames.CONTENT_TYPE);
            Assert.Equal(1, contentTypes.Count,
                    "encoder must drop the pre-existing multipart Content-Type regardless of JVM locale");
            Assert.True(contentTypes[0].StartsWith("multipart/form-data", StringComparison.Ordinal),
                    "the surviving Content-Type must be the encoder's freshly-built multipart header");
            request.Release();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
