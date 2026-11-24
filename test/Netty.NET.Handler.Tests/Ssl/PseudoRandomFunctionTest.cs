/*
 * Copyright 2019 The Netty Project
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
using System.Text;
using Netty.NET.Handler.Ssl;

namespace Netty.NET.Handler.Tests.Ssl;

/**
 * The test vectors here were provided via:
 * https://www.ietf.org/mail-archive/web/tls/current/msg03416.html
 */
// CLR: Bouncy Castle's Hex.decode, which skips whitespace, is Convert.FromHexString of the text without its spaces
// ("Converts the specified string, which encodes binary data as hex characters, to an equivalent 8-bit unsigned integer
// array", https://learn.microsoft.com/en-us/dotnet/api/system.convert.fromhexstring?view=net-10.0); test-only
// cross-check libraries may be replaced by BCL types (handler brief decision 6). CharsetUtil.US_ASCII is Encoding.ASCII.
public class PseudoRandomFunctionTest
{

    [Fact]
    public void TestPrfSha256()
    {
        byte[] secret = Decode("9b be 43 6b a9 40 f0 17 b1 76 52 84 9a 71 db 35");
        byte[] seed = Decode("a0 ba 9f 93 6c da 31 18 27 a6 f7 96 ff d5 19 8c");
        byte[] label = Encoding.ASCII.GetBytes("test label");
        byte[] expected = Decode(
                 "e3 f2 29 ba 72 7b e1 7b" +
                 "8d 12 26 20 55 7c d4 53" +
                 "c2 aa b2 1d 07 c3 d4 95" +
                 "32 9b 52 d4 e6 1e db 5a" +
                 "6b 30 17 91 e9 0d 35 c9" +
                 "c9 a4 6b 4e 14 ba f9 af" +
                 "0f a0 22 f7 07 7d ef 17" +
                 "ab fd 37 97 c0 56 4b ab" +
                 "4f bc 91 66 6e 9d ef 9b" +
                 "97 fc e3 4f 79 67 89 ba" +
                 "a4 80 82 d1 22 ee 42 c5" +
                 "a7 2e 5a 51 10 ff f7 01" +
                 "87 34 7b 66");
        byte[] actual = PseudoRandomFunction.Hash(secret, label, seed, expected.Length, "HmacSha256");
        Assert.Equal(expected, actual);
    }

    // CLR: Hex.decode(String).
    private static byte[] Decode(string hex)
    {
        return Convert.FromHexString(hex.Replace(" ", ""));
    }
}
