/*
 * Copyright 2021 The Netty Project
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

namespace Netty.NET.Handler.Ssl;

/**
 * Convert java naming to OpenSSL naming if possible and if not return the original name.
 */
// CLR: the class is static: the private constructor only prevented instantiation. A switch on a string compares with
// string equality in both languages; a null key throws NullPointerException in Java and selects the default section in
// C#, so a null key is returned unchanged.
internal static class GroupsConverter
{

    // See https://tools.ietf.org/search/rfc4492#appendix-A and https://www.java.com/en/configure_crypto.html
    internal static string ToOpenSsl(string key)
    {
        switch (key)
        {
            case "secp224r1":
                return "P-224";
            case "prime256v1":
            case "secp256r1":
                return "P-256";
            case "secp384r1":
                return "P-384";
            case "secp521r1":
                return "P-521";
            case "x25519":
                return "X25519";
            case "x448":
                return "X448";
            case "x25519mlkem786":
                return "X25519MLKEM768";
            default:
                return key;
        }
    }
}
