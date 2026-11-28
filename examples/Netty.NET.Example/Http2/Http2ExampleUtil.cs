/*
 * Copyright 2014 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Http;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Example.Http2;

/**
 * Utility methods used by the example client and server.
 */
// CLR: the class only holds statics, so it is a static class (the private constructor of the final class has no
// counterpart). InputStream is System.IO.Stream and IOException is System.IO.IOException; ByteBuf.writeBytes(InputStream,
// int) is WriteBytes(Stream, int), which returns -1 at the end of the stream as in Java. Integer.parseInt is int.Parse with
// an optional sign and the invariant culture (codec-http's HttpResponseDecoder). QueryStringDecoder.parameters() is
// Parameters(), an IDictionary whose missing key is not found by TryGetValue (Map.get returns null).
public static class Http2ExampleUtil
{

    /**
     * Response header sent in response to the http-&gt;http2 cleartext upgrade request.
     */
    public const string UPGRADE_RESPONSE_HEADER = "http-to-http2-upgrade";

    /**
     * Size of the block to be read from the input stream.
     */
    private const int BLOCK_SIZE = 1024;

    /**
     * @param string the string to be converted to an integer.
     * @param defaultValue the default value
     * @return the integer value of a string or the default value, if the string is either null or empty.
     */
    public static int ToInt(string @string, int defaultValue)
    {
        if (@string != null && @string.Length != 0)
        {
            return int.Parse(@string, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        }

        return defaultValue;
    }

    /**
     * Reads an InputStream into a byte array.
     * @param input the InputStream.
     * @return a byte array representation of the InputStream.
     * @throws IOException if an I/O exception of some sort happens while reading the InputStream.
     */
    public static ByteBuf ToByteBuf(Stream input)
    {
        ByteBuf buf = Unpooled.Buffer();
        int n = 0;

        do
        {
            n = buf.WriteBytes(input, BLOCK_SIZE);
        } while (n > 0);

        return buf;
    }

    /**
     * @param query the decoder of query string
     * @param key the key to lookup
     * @return the first occurrence of that key in the string parameters
     */
    public static string FirstValue(QueryStringDecoder query, string key)
    {
        CheckNotNull(query, "Query can't be null!");
        if (!query.Parameters().TryGetValue(key, out IList<string> values) || values == null || values.Count == 0)
        {
            return null;
        }

        return values[0];
    }

    // CLR-only (no Java counterpart): the HTTP/2 examples pass Http2SecurityUtil.CIPHERS to SslContextBuilder.ciphers(...).
    // SslStream restricts cipher suites with a CipherSuitesPolicy, which cannot be created on Windows and Android: there
    // a context built with non-null ciphers fails in newHandler with PlatformNotSupportedException (SslContextBuilder.Ciphers,
    // docs/handler-porting.md, "Cipher suites cannot be restricted on Windows and Android"). The examples therefore pass
    // the ciphers through this method, which returns them where a CipherSuitesPolicy exists and null (the default cipher
    // suites of the operating system, as ciphers(null, filter) means in Java) elsewhere.
    public static IEnumerable<string> PlatformCiphers(IEnumerable<string> ciphers)
    {
        return OperatingSystem.IsWindows() || OperatingSystem.IsAndroid() ? null : ciphers;
    }
}
