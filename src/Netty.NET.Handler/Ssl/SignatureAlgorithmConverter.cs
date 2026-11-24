/*
 * Copyright 2018 The Netty Project
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
using System.Text.RegularExpressions;

namespace Netty.NET.Handler.Ssl;

/**
 * Converts OpenSSL signature Algorithm names to
 * <a href="https://docs.oracle.com/javase/8/docs/technotes/guides/security/StandardNames.html#Signature">
 *     Java signature Algorithm names</a>.
 */
// CLR: Pattern/Matcher are Regex/Match as in CipherSuiteConverter: matcher.matches() is a match of the pattern wrapped in
// \A(?:...)\z, group(n) of a group that did not take part is null (Group.Success is false), and "." is written as the
// class of the characters that are not Java line terminators, since the .NET "." only excludes \n
// (https://learn.microsoft.com/en-us/dotnet/standard/base-types/character-classes-in-regular-expressions;
// https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/regex/Pattern.html). toUpperCase(Locale.ROOT) is
// ToUpperInvariant() and equalsIgnoreCase is an OrdinalIgnoreCase comparison; both agree with Java on the ASCII names
// OpenSSL produces. The class is static: the private constructor only prevented instantiation.
internal static class SignatureAlgorithmConverter
{

    // CLR: the Java "." (any character except the line terminators \n, \r, \u0085, \u2028 and \u2029).
    private const string DOT = "[^\\n\\r\\u0085\\u2028\\u2029]";

    // OpenSSL has 3 different formats it uses at the moment we will match against all of these.
    // For example:
    //              ecdsa-with-SHA384
    //              hmacWithSHA384
    //              dsa_with_SHA224
    //
    // For more details see https://github.com/openssl/openssl/blob/OpenSSL_1_0_2p/crypto/objects/obj_dat.h
    //
    // BoringSSL uses a different format:
    // https://github.com/google/boringssl/blob/8525ff3/ssl/ssl_privkey.cc#L436
    //
    private static readonly Regex PATTERN = new Regex("\\A(?:" +
            // group 1 - 2
            "(?:(^[a-zA-Z]" + DOT + "+)With(" + DOT + "+)Encryption$)|" +
            // group 3 - 4
            "(?:(^[a-zA-Z]" + DOT + "+)(?:_with_|-with-|_pkcs1_|_pss_rsae_)(" + DOT + "+$))|" +
            // group 5 - 6
            "(?:(^[a-zA-Z]" + DOT + "+)_(" + DOT + "+$))" + ")\\z");

    /**
     * Converts an OpenSSL algorithm name to a Java algorithm name and return it,
     * or return {@code null} if the conversion failed because the format is not known.
     */
    internal static string ToJavaName(string opensslName)
    {
        if (opensslName == null)
        {
            return null;
        }
        // EdDSA names carry no hash, so the pattern can't match them
        if ("ed25519".Equals(opensslName, StringComparison.OrdinalIgnoreCase))
        {
            return "Ed25519";
        }
        if ("ed448".Equals(opensslName, StringComparison.OrdinalIgnoreCase))
        {
            return "Ed448";
        }
        Match matcher = PATTERN.Match(opensslName);
        if (matcher.Success)
        {
            string group1 = Group(matcher, 1);
            if (group1 != null)
            {
                return group1.ToUpperInvariant() + "with" + Group(matcher, 2).ToUpperInvariant();
            }
            if (Group(matcher, 3) != null)
            {
                return Group(matcher, 4).ToUpperInvariant() + "with" + Group(matcher, 3).ToUpperInvariant();
            }

            if (Group(matcher, 5) != null)
            {
                return Group(matcher, 6).ToUpperInvariant() + "with" + Group(matcher, 5).ToUpperInvariant();
            }
        }
        return null;
    }

    // CLR: Matcher.group(n), which is null for a group that did not take part in the match.
    private static string Group(Match m, int group)
    {
        return m.Groups[group].Success ? m.Groups[group].Value : null;
    }
}
