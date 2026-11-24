/*
 * Copyright 2014 The Netty Project
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

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Handler.Ssl;

/**
 * Converts a Java cipher suite string to an OpenSSL cipher suite string and vice versa.
 *
 * @see <a href="https://en.wikipedia.org/wiki/Cipher_suite">Wikipedia page about cipher suite</a>
 */
// CLR: java.util.regex.Pattern is System.Text.RegularExpressions.Regex. Matcher.matches() "Attempts to match the entire
// region against the pattern" (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/regex/Matcher.html),
// so each pattern is wrapped in \A(?:...)\z and Match().Success is matches(); replaceFirst is Replace(input, replacement,
// 1); group(n) of a group that did not take part is null in Java and is read through Group(). Java's "." "matches any
// character except a line terminator"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/regex/Pattern.html) while the .NET "."
// "matches any character except \n"
// (https://learn.microsoft.com/en-us/dotnet/standard/base-types/character-classes-in-regular-expressions), so "." is
// written as DOT, the class of the Java line terminators excluded. ConcurrentMap/ConcurrentHashMap is
// ConcurrentDictionary (putIfAbsent is TryAdd), HashMap is Dictionary and Collections.unmodifiableMap/singletonMap are
// ReadOnlyDictionary wrappers; Map.get of an absent or null key returns null through Get(). convertToCipherStrings is
// not ported: it validates every cipher with OpenSsl.isCipherSuiteAvailable, and OpenSsl (netty-tcnative) is
// library-not-ported (ssl design decision 4, docs/handler-ssl-design.md).
[UnstableApi]
public static class CipherSuiteConverter
{

    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(CipherSuiteConverter));

    // CLR: the Java "." (any character except the line terminators \n, \r, \u0085, \u2028 and \u2029).
    private const string DOT = "[^\\n\\r\\u0085\\u2028\\u2029]";

    /**
     * A_B_WITH_C_D, where:
     *
     * A - TLS or SSL (protocol)
     * B - handshake algorithm (key exchange and authentication algorithms to be precise)
     * C - bulk cipher
     * D - HMAC algorithm
     *
     * This regular expression assumes that:
     *
     * 1) A is always TLS or SSL, and
     * 2) D is always a single word.
     */
    private static readonly Regex JAVA_CIPHERSUITE_PATTERN =
            Compile("^(?:TLS|SSL)_((?:(?!_WITH_)" + DOT + ")+)_WITH_(" + DOT + "*)_(" + DOT + "*)$");

    /**
     * A-B-C, where:
     *
     * A - handshake algorithm (key exchange and authentication algorithms to be precise)
     * B - bulk cipher
     * C - HMAC algorithm
     *
     * This regular expression assumes that:
     *
     * 1) A has some deterministic pattern as shown below, and
     * 2) C is always a single word
     */
    private static readonly Regex OPENSSL_CIPHERSUITE_PATTERN =
            // Be very careful not to break the indentation while editing.
            Compile(
                    "^(?:(" + // BEGIN handshake algorithm
                        "(?:(?:EXP-)?" +
                            "(?:" +
                                "(?:DHE|EDH|ECDH|ECDHE|SRP|RSA)-(?:DSS|RSA|ECDSA|PSK)|" +
                                "(?:ADH|AECDH|KRB5|PSK|SRP)" +
                            ')' +
                        ")|" +
                        "EXP" +
                    ")-)?" +  // END handshake algorithm
                    "(" + DOT + "*)-(" + DOT + "*)$");

    private static readonly Regex JAVA_AES_CBC_PATTERN = Compile("^(AES)_([0-9]+)_CBC$");
    private static readonly Regex JAVA_AES_PATTERN = Compile("^(AES)_([0-9]+)_(" + DOT + "*)$");
    private static readonly Regex OPENSSL_AES_CBC_PATTERN = Compile("^(AES)([0-9]+)$");
    private static readonly Regex OPENSSL_AES_PATTERN = Compile("^(AES)([0-9]+)-(" + DOT + "*)$");

    // CLR: Pattern.compile for use with Matcher.matches() (see the class comment).
    private static Regex Compile(string pattern)
    {
        return new Regex("\\A(?:" + pattern + ")\\z");
    }

    // CLR: Matcher.group(n), which is null for a group that did not take part in the match.
    private static string Group(Match m, int group)
    {
        return m.Groups[group].Success ? m.Groups[group].Value : null;
    }

    // CLR: Map.get(key), which is null for an absent key and, for HashMap and singletonMap, for a null key.
    private static V Get<V>(IDictionary<string, V> map, string key) where V : class
    {
        return key != null && map.TryGetValue(key, out V value) ? value : null;
    }

    /**
     * Used to store nullable values in a CHM
     */
    private sealed class CachedValue
    {

        internal static readonly CachedValue NULL = new CachedValue(null);

        internal static CachedValue Of(string value)
        {
            return value != null ? new CachedValue(value) : NULL;
        }

        internal readonly string value;
        private CachedValue(string value)
        {
            this.value = value;
        }
    }

    /**
     * Java-to-OpenSSL cipher suite conversion map
     * Note that the Java cipher suite has the protocol prefix (TLS_, SSL_)
     */
    private static readonly ConcurrentDictionary<string, CachedValue> j2o = new ConcurrentDictionary<string, CachedValue>();

    /**
     * OpenSSL-to-Java cipher suite conversion map.
     * Note that one OpenSSL cipher suite can be converted to more than one Java cipher suites because
     * a Java cipher suite has the protocol name prefix (TLS_, SSL_)
     */
    private static readonly ConcurrentDictionary<string, IDictionary<string, string>> o2j =
            new ConcurrentDictionary<string, IDictionary<string, string>>();

    private static readonly IDictionary<string, string> j2oTls13;
    private static readonly IDictionary<string, IDictionary<string, string>> o2jTls13;

    static CipherSuiteConverter()
    {
        Dictionary<string, string> j2oTls13Map = new Dictionary<string, string>();
        j2oTls13Map["TLS_AES_128_GCM_SHA256"] = "AEAD-AES128-GCM-SHA256";
        j2oTls13Map["TLS_AES_256_GCM_SHA384"] = "AEAD-AES256-GCM-SHA384";
        j2oTls13Map["TLS_CHACHA20_POLY1305_SHA256"] = "AEAD-CHACHA20-POLY1305-SHA256";
        j2oTls13 = new ReadOnlyDictionary<string, string>(j2oTls13Map);

        Dictionary<string, IDictionary<string, string>> o2jTls13Map =
                new Dictionary<string, IDictionary<string, string>>();
        o2jTls13Map["TLS_AES_128_GCM_SHA256"] = SingletonMap("TLS", "TLS_AES_128_GCM_SHA256");
        o2jTls13Map["TLS_AES_256_GCM_SHA384"] = SingletonMap("TLS", "TLS_AES_256_GCM_SHA384");
        o2jTls13Map["TLS_CHACHA20_POLY1305_SHA256"] = SingletonMap("TLS", "TLS_CHACHA20_POLY1305_SHA256");
        o2jTls13Map["AEAD-AES128-GCM-SHA256"] = SingletonMap("TLS", "TLS_AES_128_GCM_SHA256");
        o2jTls13Map["AEAD-AES256-GCM-SHA384"] = SingletonMap("TLS", "TLS_AES_256_GCM_SHA384");
        o2jTls13Map["AEAD-CHACHA20-POLY1305-SHA256"] = SingletonMap("TLS", "TLS_CHACHA20_POLY1305_SHA256");
        o2jTls13 = new ReadOnlyDictionary<string, IDictionary<string, string>>(o2jTls13Map);
    }

    // CLR: Collections.singletonMap(key, value), an immutable one-entry map.
    private static IDictionary<string, string> SingletonMap(string key, string value)
    {
        return new ReadOnlyDictionary<string, string>(new Dictionary<string, string> { [key] = value });
    }

    /**
     * Clears the cache for testing purpose.
     */
    internal static void ClearCache()
    {
        j2o.Clear();
        o2j.Clear();
    }

    /**
     * Tests if the specified key-value pair has been cached in Java-to-OpenSSL cache.
     */
    internal static bool IsJ2OCached(string key, string value)
    {
        j2o.TryGetValue(key, out CachedValue cached);
        return cached != null && value.Equals(cached.value);
    }

    /**
     * Tests if the specified key-value pair has been cached in OpenSSL-to-Java cache.
     */
    internal static bool IsO2JCached(string key, string protocol, string value)
    {
        o2j.TryGetValue(key, out IDictionary<string, string> p2j);
        if (p2j == null)
        {
            return false;
        }
        else
        {
            return value.Equals(Get(p2j, protocol));
        }
    }

    /**
     * Converts the specified Java cipher suite to its corresponding OpenSSL cipher suite name.
     *
     * @return {@code null} if the conversion has failed
     */
    public static string ToOpenSsl(string javaCipherSuite, bool boringSSL)
    {
        j2o.TryGetValue(javaCipherSuite, out CachedValue converted);
        if (converted != null)
        {
            return converted.value;
        }
        return CacheFromJava(javaCipherSuite, boringSSL);
    }

    private static string CacheFromJava(string javaCipherSuite, bool boringSSL)
    {
        string converted = Get(j2oTls13, javaCipherSuite);
        if (converted != null)
        {
            return boringSSL ? converted : javaCipherSuite;
        }

        string openSslCipherSuite = ToOpenSslUncached(javaCipherSuite, boringSSL);

        // Cache the mapping.
        j2o.TryAdd(javaCipherSuite, CachedValue.Of(openSslCipherSuite));

        if (openSslCipherSuite == null)
        {
            return null;
        }

        // Cache the reverse mapping after stripping the protocol prefix (TLS_ or SSL_)
        string javaCipherSuiteSuffix = javaCipherSuite.Substring(4);
        Dictionary<string, string> p2j = new Dictionary<string, string>(4);
        p2j[""] = javaCipherSuiteSuffix;
        p2j["SSL"] = "SSL_" + javaCipherSuiteSuffix;
        p2j["TLS"] = "TLS_" + javaCipherSuiteSuffix;
        o2j[openSslCipherSuite] = p2j;

        logger.Debug("Cipher suite mapping: {} => {}", javaCipherSuite, openSslCipherSuite);

        return openSslCipherSuite;
    }

    internal static string ToOpenSslUncached(string javaCipherSuite, bool boringSSL)
    {
        string converted = Get(j2oTls13, javaCipherSuite);
        if (converted != null)
        {
            return boringSSL ? converted : javaCipherSuite;
        }

        Match m = JAVA_CIPHERSUITE_PATTERN.Match(javaCipherSuite);
        if (!m.Success)
        {
            return null;
        }

        string handshakeAlgo = ToOpenSslHandshakeAlgo(Group(m, 1));
        string bulkCipher = ToOpenSslBulkCipher(Group(m, 2));
        string hmacAlgo = ToOpenSslHmacAlgo(Group(m, 3));
        if (handshakeAlgo.Length == 0)
        {
            return bulkCipher + '-' + hmacAlgo;
        }
        else if (bulkCipher.Contains("CHACHA20"))
        {
            return handshakeAlgo + '-' + bulkCipher;
        }
        else
        {
            return handshakeAlgo + '-' + bulkCipher + '-' + hmacAlgo;
        }
    }

    private static string ToOpenSslHandshakeAlgo(string handshakeAlgo)
    {
        bool export = handshakeAlgo.EndsWith("_EXPORT", System.StringComparison.Ordinal);
        if (export)
        {
            handshakeAlgo = handshakeAlgo.Substring(0, handshakeAlgo.Length - 7);
        }

        if ("RSA".Equals(handshakeAlgo))
        {
            handshakeAlgo = "";
        }
        else if (handshakeAlgo.EndsWith("_anon", System.StringComparison.Ordinal))
        {
            handshakeAlgo = 'A' + handshakeAlgo.Substring(0, handshakeAlgo.Length - 5);
        }

        if (export)
        {
            if (handshakeAlgo.Length == 0)
            {
                handshakeAlgo = "EXP";
            }
            else
            {
                handshakeAlgo = "EXP-" + handshakeAlgo;
            }
        }

        return handshakeAlgo.Replace('_', '-');
    }

    private static string ToOpenSslBulkCipher(string bulkCipher)
    {
        if (bulkCipher.StartsWith("AES_", System.StringComparison.Ordinal))
        {
            Match m = JAVA_AES_CBC_PATTERN.Match(bulkCipher);
            if (m.Success)
            {
                return JAVA_AES_CBC_PATTERN.Replace(bulkCipher, "$1$2", 1);
            }

            m = JAVA_AES_PATTERN.Match(bulkCipher);
            if (m.Success)
            {
                return JAVA_AES_PATTERN.Replace(bulkCipher, "$1$2-$3", 1);
            }
        }

        if ("3DES_EDE_CBC".Equals(bulkCipher))
        {
            return "DES-CBC3";
        }

        if ("RC4_128".Equals(bulkCipher) || "RC4_40".Equals(bulkCipher))
        {
            return "RC4";
        }

        if ("DES40_CBC".Equals(bulkCipher) || "DES_CBC_40".Equals(bulkCipher))
        {
            return "DES-CBC";
        }

        if ("RC2_CBC_40".Equals(bulkCipher))
        {
            return "RC2-CBC";
        }

        return bulkCipher.Replace('_', '-');
    }

    private static string ToOpenSslHmacAlgo(string hmacAlgo)
    {
        // Java and OpenSSL use the same algorithm names for:
        //
        //   * SHA
        //   * SHA256
        //   * MD5
        //
        return hmacAlgo;
    }

    /**
     * Convert from OpenSSL cipher suite name convention to java cipher suite name convention.
     * @param openSslCipherSuite An OpenSSL cipher suite name.
     * @param protocol The cryptographic protocol (i.e. SSL, TLS, ...).
     * @return The translated cipher suite name according to java conventions (or null if translation was not possible).
     */
    public static string ToJava(string openSslCipherSuite, string protocol)
    {
        o2j.TryGetValue(openSslCipherSuite, out IDictionary<string, string> p2j);
        if (p2j == null)
        {
            p2j = CacheFromOpenSsl(openSslCipherSuite);
            // This may happen if this method is queried when OpenSSL doesn't yet have a cipher setup. It will return
            // "(NONE)" in this case.
            if (p2j == null)
            {
                return null;
            }
        }

        string javaCipherSuite = Get(p2j, protocol);
        if (javaCipherSuite == null)
        {
            string cipher = Get(p2j, "");
            if (cipher == null)
            {
                return null;
            }
            // CLR: string concatenation of a null protocol gives "" where Java gives "null".
            javaCipherSuite = (protocol ?? "null") + '_' + cipher;
        }

        return javaCipherSuite;
    }

    private static IDictionary<string, string> CacheFromOpenSsl(string openSslCipherSuite)
    {
        IDictionary<string, string> converted = Get(o2jTls13, openSslCipherSuite);
        if (converted != null)
        {
            return converted;
        }

        string javaCipherSuiteSuffix = ToJavaUncached0(openSslCipherSuite, false);
        if (javaCipherSuiteSuffix == null)
        {
            return null;
        }

        string javaCipherSuiteSsl = "SSL_" + javaCipherSuiteSuffix;
        string javaCipherSuiteTls = "TLS_" + javaCipherSuiteSuffix;

        // Cache the mapping.
        Dictionary<string, string> p2j = new Dictionary<string, string>(4);
        p2j[""] = javaCipherSuiteSuffix;
        p2j["SSL"] = javaCipherSuiteSsl;
        p2j["TLS"] = javaCipherSuiteTls;
        o2j.TryAdd(openSslCipherSuite, p2j);

        // Cache the reverse mapping after adding the protocol prefix (TLS_ or SSL_)
        CachedValue cachedValue = CachedValue.Of(openSslCipherSuite);
        j2o.TryAdd(javaCipherSuiteTls, cachedValue);
        j2o.TryAdd(javaCipherSuiteSsl, cachedValue);

        logger.Debug("Cipher suite mapping: {} => {}", javaCipherSuiteTls, openSslCipherSuite);
        logger.Debug("Cipher suite mapping: {} => {}", javaCipherSuiteSsl, openSslCipherSuite);

        return p2j;
    }

    internal static string ToJavaUncached(string openSslCipherSuite)
    {
        return ToJavaUncached0(openSslCipherSuite, true);
    }

    private static string ToJavaUncached0(string openSslCipherSuite, bool checkTls13)
    {
        if (checkTls13)
        {
            IDictionary<string, string> converted = Get(o2jTls13, openSslCipherSuite);
            if (converted != null)
            {
                return Get(converted, "TLS");
            }
        }

        Match m = OPENSSL_CIPHERSUITE_PATTERN.Match(openSslCipherSuite);
        if (!m.Success)
        {
            return null;
        }

        string handshakeAlgo = Group(m, 1);
        bool export;
        if (handshakeAlgo == null)
        {
            handshakeAlgo = "";
            export = false;
        }
        else if (handshakeAlgo.StartsWith("EXP-", System.StringComparison.Ordinal))
        {
            handshakeAlgo = handshakeAlgo.Substring(4);
            export = true;
        }
        else if ("EXP".Equals(handshakeAlgo))
        {
            handshakeAlgo = "";
            export = true;
        }
        else
        {
            export = false;
        }

        handshakeAlgo = ToJavaHandshakeAlgo(handshakeAlgo, export);
        string bulkCipher = ToJavaBulkCipher(Group(m, 2), export);
        string hmacAlgo = ToJavaHmacAlgo(Group(m, 3));

        string javaCipherSuite = handshakeAlgo + "_WITH_" + bulkCipher + '_' + hmacAlgo;
        // For historical reasons the CHACHA20 ciphers do not follow OpenSSL's custom naming convention and omits the
        // HMAC algorithm portion of the name. There is currently no way to derive this information because it is
        // omitted from the OpenSSL cipher name, but they currently all use SHA256 for HMAC [1].
        // [1] https://www.openssl.org/docs/man1.1.0/apps/ciphers.html
        return bulkCipher.Contains("CHACHA20") ? javaCipherSuite + "_SHA256" : javaCipherSuite;
    }

    private static string ToJavaHandshakeAlgo(string handshakeAlgo, bool export)
    {
        if (handshakeAlgo.Length == 0)
        {
            handshakeAlgo = "RSA";
        }
        else if ("ADH".Equals(handshakeAlgo))
        {
            handshakeAlgo = "DH_anon";
        }
        else if ("AECDH".Equals(handshakeAlgo))
        {
            handshakeAlgo = "ECDH_anon";
        }

        handshakeAlgo = handshakeAlgo.Replace('-', '_');
        if (export)
        {
            return handshakeAlgo + "_EXPORT";
        }
        else
        {
            return handshakeAlgo;
        }
    }

    private static string ToJavaBulkCipher(string bulkCipher, bool export)
    {
        if (bulkCipher.StartsWith("AES", System.StringComparison.Ordinal))
        {
            Match m = OPENSSL_AES_CBC_PATTERN.Match(bulkCipher);
            if (m.Success)
            {
                return OPENSSL_AES_CBC_PATTERN.Replace(bulkCipher, "$1_$2_CBC", 1);
            }

            m = OPENSSL_AES_PATTERN.Match(bulkCipher);
            if (m.Success)
            {
                return OPENSSL_AES_PATTERN.Replace(bulkCipher, "$1_$2_$3", 1);
            }
        }

        if ("DES-CBC3".Equals(bulkCipher))
        {
            return "3DES_EDE_CBC";
        }

        if ("RC4".Equals(bulkCipher))
        {
            if (export)
            {
                return "RC4_40";
            }
            else
            {
                return "RC4_128";
            }
        }

        if ("DES-CBC".Equals(bulkCipher))
        {
            if (export)
            {
                return "DES_CBC_40";
            }
            else
            {
                return "DES_CBC";
            }
        }

        if ("RC2-CBC".Equals(bulkCipher))
        {
            if (export)
            {
                return "RC2_CBC_40";
            }
            else
            {
                return "RC2_CBC";
            }
        }

        return bulkCipher.Replace('-', '_');
    }

    private static string ToJavaHmacAlgo(string hmacAlgo)
    {
        // Java and OpenSSL use the same algorithm names for:
        //
        //   * SHA
        //   * SHA256
        //   * MD5
        //
        return hmacAlgo;
    }
}
