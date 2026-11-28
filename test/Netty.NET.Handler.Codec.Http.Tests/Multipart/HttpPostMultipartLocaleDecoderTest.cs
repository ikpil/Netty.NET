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
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Http.Multipart;

namespace Netty.NET.Handler.Codec.Http.Tests.Multipart;

/**
 * Decoder-side test that flips {@link Locale#setDefault(Locale)} to Turkish to exercise the
 * {@code Content-Transfer-Encoding} normalization path. Marked {@link Isolated} because the JVM
 * default Locale is process-global state and codec-http runs tests with
 * {@code junit.jupiter.execution.parallel.mode.default = concurrent}.
 */
// CLR: Locale.getDefault()/setDefault(Locale) are CultureInfo.CurrentCulture, "the culture used by the current thread
// and task-based asynchronous operations"
// (https://learn.microsoft.com/en-us/dotnet/api/system.globalization.cultureinfo.currentculture?view=net-10.0), so it
// is not process-global like the JVM default and @Isolated has no counterpart (as core's HttpVersionLocaleTest); the
// original value is restored in finally as in Java. FullHttpRequest is IFullHttpRequest, CharsetUtil.US_ASCII
// Encoding.ASCII, and content.length() the string Length. The package-private class and method are public, as xUnit
// requires.
public class HttpPostMultipartLocaleDecoderTest
{

    [Fact]
    public void TestUppercaseBinaryTransferEncodingUnderTurkishLocale()
    {
        // Repro: a part declares an uppercase Content-Transfer-Encoding (RFC 2045 mechanism
        // tokens are case-insensitive). On a Turkish-locale JVM the decoder used to call
        // toLowerCase() without a Locale and produced "bınary" (U+0131) which then failed the
        // compare against the lowercase ASCII constants and threw
        // "TransferEncoding Unknown: bınary".
        CultureInfo original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            string content = "\n--861fbeab-cd20-470c-9609-d40a0f704466\r\n" +
                    "content-disposition: form-data; " +
                    "name=\"file\"; filename=\"myfile.ogg\"\r\n" +
                    "content-type: audio/ogg; codecs=opus; charset=UTF8\r\n" +
                    "Content-Transfer-Encoding: BINARY\r\n" +
                    "\r\n\r\n--861fbeab-cd20-470c-9609-d40a0f704466--\r\n";

            IFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/upload",
                    Unpooled.CopiedBuffer(content, Encoding.ASCII));
            req.Headers().Set("content-type", "multipart/form-data; boundary=861fbeab-cd20-470c-9609-d40a0f704466");
            req.Headers().Set("content-length", content.Length);

            HttpPostMultipartRequestDecoder decoder = new HttpPostMultipartRequestDecoder(req);
            IFileUpload httpData = (IFileUpload) decoder.GetBodyHttpDatas("file")[0];
            Assert.NotNull(httpData);
            Assert.Equal("audio/ogg", httpData.GetContentType());
            decoder.Destroy();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
