/*
 * Copyright 2012 The Netty Project
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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common;

/**
 * A class that holds a number of network-related constants.
 * <p/>
 * This class borrowed some of its methods from a  modified fork of the
 * <a href="https://svn.apache.org/repos/asf/harmony/enhanced/java/branches/java6/classlib/modules/luni/
 * src/main/java/org/apache/harmony/luni/util/Inet6Util.java">Inet6Util class</a> which was part of Apache Harmony.
 */
/**
     * A constructor to stop this class being constructed.
     */
// Unused
// CLR adaptation: the static class declaration prevents construction.
public static class NetUtil
{
    /**
     * The {@link Inet4Address} that represents the IPv4 loopback address '127.0.0.1'
     */
    // CLR adaptation: use the standard read-only IPAddress.Loopback directly.

    /**
     * The {@link Inet6Address} that represents the IPv6 loopback address '::1'
     */
    // CLR adaptation: use the standard read-only IPAddress.IPv6Loopback directly.

    /**
     * The {@link InetAddress} that represents the loopback address. If IPv6 stack is available, it will refer to
     * {@link #LOCALHOST6}.  Otherwise, {@link #LOCALHOST4}.
     */
    /// <summary>
    /// Gets an independent copy of the loopback address selected during initialization.
    /// Compare address values rather than references; caller mutation does not change later reads.
    /// </summary>
    public static IPAddress LoopbackAddress => CopyAddress(localhost);

    /**
     * The loopback {@link NetworkInterface} of the current machine
     */
    public static readonly NetworkInterface LOOPBACK_IF;

    /**
     * An unmodifiable Collection of all the interfaces on this machine.
     */
    public static readonly IReadOnlyList<NetworkInterface> NETWORK_INTERFACES;

    /**
     * The SOMAXCONN value of the current machine.  If failed to get the value,  {@code 200} is used as a
     * default value for Windows and {@code 128} for others.
     */
    public static readonly int SOMAXCONN;

    /**
     * This defines how many words (represented as ints) are needed to represent an IPv6 address
     */
    private static readonly int IPV6_WORD_COUNT = 8;

    /**
     * The maximum number of characters for an IPV6 string with no scope
     */
    private static readonly int IPV6_MAX_CHAR_COUNT = 39;

    /**
     * Number of bytes needed to represent an IPV6 value
     */
    private static readonly int IPV6_BYTE_COUNT = 16;

    /**
     * Maximum amount of value adding characters in between IPV6 separators
     */
    private static readonly int IPV6_MAX_CHAR_BETWEEN_SEPARATOR = 4;

    /**
     * Minimum number of separators that must be present in an IPv6 string
     */
    private static readonly int IPV6_MIN_SEPARATORS = 2;

    /**
     * Maximum number of separators that must be present in an IPv6 string
     */
    private static readonly int IPV6_MAX_SEPARATORS = 8;

    /**
     * Maximum amount of value adding characters in between IPV4 separators
     */
    private static readonly int IPV4_MAX_CHAR_BETWEEN_SEPARATOR = 3;

    /**
     * Number of separators that must be present in an IPv4 string
     */
    private static readonly int IPV4_SEPARATORS = 3;

    /**
     * {@code true} if IPv4 should be used even if the system supports both IPv4 and IPv6.
     */
    /**
     * Returns {@code true} if IPv4 should be used even if the system supports both IPv4 and IPv6. Setting this
     * property to {@code true} will disable IPv6 support. The default value of this property is {@code false}.
     *
     * @see <a href="https://docs.oracle.com/javase/8/docs/api/java/net/doc-files/net-properties.html">Java SE
     *      networking properties</a>
     */
    // CLR adaptation: these environment keys configure Netty policy, not System.Net itself.
    // Values are captured once by this type's explicit static constructor.
    /// <summary>
    /// Gets the IPv4-only policy captured from the java.net.preferIPv4Stack environment variable.
    /// Consumers must apply this policy when resolving addresses or creating sockets;
    /// it does not disable IPv6 in System.Net.
    /// </summary>
    public static bool PreferIPv4Stack { get; }

    /**
     * {@code true} if an IPv6 address should be preferred when a host has both an IPv4 address and an IPv6 address.
     */
    /**
     * Returns {@code true} if an IPv6 address should be preferred when a host has both an IPv4 address and an IPv6
     * address. The default value of this property is {@code false}.
     *
     * @see <a href="https://docs.oracle.com/javase/8/docs/api/java/net/doc-files/net-properties.html">Java SE
     *      networking properties</a>
     */
    // CLR adaptation: only true forces IPv6 preference; system/yes/1 do not.
    /// <summary>
    /// Gets the IPv6 address preference captured from the java.net.preferIPv6Addresses environment variable.
    /// This is independent of PreferIPv4Stack and does not describe runtime IPv6 capability.
    /// </summary>
    public static bool PreferIPv6Addresses { get; }

    /**
     * The logger being used by this class
     */
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(NetUtil));

    private static readonly IPAddress localhost;

    static NetUtil()
    {
        PreferIPv4Stack = SystemPropertyUtil.GetBoolean("java.net.preferIPv4Stack", false);
        string prefer = SystemPropertyUtil.Get("java.net.preferIPv6Addresses", "false");
        if (string.Equals("true", prefer.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            PreferIPv6Addresses = true;
        }
        else
        {
            // Let's just use false in this case as only true is "forcing" ipv6.
            PreferIPv6Addresses = false;
        }

        logger.Debug("java.net.preferIPv4Stack: {}", PreferIPv4Stack);
        logger.Debug("java.net.preferIPv6Addresses: {}", prefer);

        NETWORK_INTERFACES = NetUtilInitializations.NetworkInterfaces();

        // Create IPv4 loopback address.
        IPAddress localhost4 = IPAddress.Loopback;

        // Create IPv6 loopback address.
        IPAddress localhost6 = IPAddress.IPv6Loopback;

        var loopback =
            NetUtilInitializations.DetermineLoopback(NETWORK_INTERFACES, localhost4, localhost6);
        LOOPBACK_IF = loopback.Iface;
        localhost = CopyAddress(loopback.Address);

        // As a SecurityManager may prevent reading the somaxconn file we wrap this in a privileged block.
        //
        // See https://github.com/netty/netty/issues/3680
        SOMAXCONN = SoMaxConnAction.Run();
    }

    private static IPAddress CopyAddress(IPAddress address)
    {
        Span<byte> bytes = stackalloc byte[16];
        address.TryWriteBytes(bytes, out int length);
        return address.AddressFamily == AddressFamily.InterNetworkV6
            ? new IPAddress(bytes[..length], address.ScopeId)
            : new IPAddress(bytes[..length]);
    }

    /**
     * This will execute <a href ="https://www.freebsd.org/cgi/man.cgi?sysctl(8)">sysctl</a> with the {@code sysctlKey}
     * which is expected to return the numeric value for for {@code sysctlKey}.
     * @param sysctlKey The key which the return value corresponds to.
     * @return The <a href ="https://www.freebsd.org/cgi/man.cgi?sysctl(8)">sysctl</a> value for {@code sysctlKey}.
     */
    internal static int? SysctlGetInt(string sysctlKey)
    {
        return SysctlGetInt(sysctlKey, Process.Start);
    }

    internal static int? SysctlGetInt(string sysctlKey, Func<ProcessStartInfo, Process> startProcess)
    {
        ArgumentException.ThrowIfNullOrEmpty(sysctlKey);
        ArgumentNullException.ThrowIfNull(startProcess);
        var processInfo = new ProcessStartInfo
        {
            FileName = "sysctl",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        processInfo.ArgumentList.Add(sysctlKey);

        // Own only a successfully started child; startup failures retain their original exception.
        using Process process = startProcess(processInfo);
        try
        {
            // Suppress warnings about resource leaks since the buffered reader is closed below
            using StreamReader reader = new StreamReader(new BoundedStream(process.StandardOutput.BaseStream));
            {
                string line = reader.ReadLine();
                if (line != null && line.StartsWith(sysctlKey, StringComparison.Ordinal))
                {
                    for (int i = line.Length - 1; i > sysctlKey.Length; i--)
                    {
                        if (!char.IsDigit(line[i]))
                        {
                            return int.Parse(line.AsSpan(i + 1), NumberStyles.None, CultureInfo.InvariantCulture);
                        }
                    }
                }

                return null;
            }
        }
        finally
        {
            // No need of 'null' check because we're initializing
            // the Process instance in first line. Any exception
            // raised will directly lead to throwable.
            if (!process.HasExited)
            {
                try
                {
                    process.Kill();
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                    // The child exited between the state check and termination request.
                }
            }
        }
    }

    /**
     * Creates an byte[] based on an ipAddressString. No error handling is performed here.
     */
    public static byte[] CreateByteArrayFromIpAddressString(string ipAddressString)
    {
        ArgumentNullException.ThrowIfNull(ipAddressString);
        return CreateByteArrayFromIpAddressString(ipAddressString.AsSpan());
    }

    /// <summary>
    /// Parses the selected literal into an owned four- or sixteen-byte array.
    /// IPv6 brackets and scope text are omitted from the wire bytes; invalid input returns null.
    /// </summary>
    public static byte[] CreateByteArrayFromIpAddressString(ReadOnlySpan<char> ipAddressString)
    {
        if (IsValidIpV4Address(ipAddressString))
        {
            return ValidIpV4ToBytes(ipAddressString);
        }

        if (IsValidIpV6Address(ipAddressString))
        {
            if (ipAddressString[0] == '[')
            {
                ipAddressString = ipAddressString[1..(ipAddressString.Length - 1)];
            }

            int percentPos = ipAddressString.IndexOf('%');
            if (percentPos >= 0)
            {
                ipAddressString = ipAddressString[..percentPos];
            }

            return GetIPv6ByName(ipAddressString, true);
        }

        return null;
    }

    /**
     * Creates an {@link InetAddress} based on an ipAddressString or might return null if it can't be parsed.
     * No error handling is performed here.
     */
    public static IPAddress CreateInetAddressFromIpAddressString(string ipAddressString)
    {
        ArgumentNullException.ThrowIfNull(ipAddressString);
        return CreateInetAddressFromIpAddressString(ipAddressString.AsSpan());
    }

    /// <summary>
    /// Parses the selected literal without DNS lookup into an owned native address.
    /// IPv6 numeric scopes use invariant unsigned 32-bit syntax; invalid input returns null.
    /// </summary>
    public static IPAddress CreateInetAddressFromIpAddressString(ReadOnlySpan<char> ipAddressString)
    {
        if (IsValidIpV4Address(ipAddressString))
        {
            byte[] bytes = ValidIpV4ToBytes(ipAddressString);
            // Should never happen!
            // CLR IPAddress(byte[]) has no UnknownHostException for a validated four-byte array.
            return new IPAddress(bytes);
        }

        if (IsValidIpV6Address(ipAddressString))
        {
            if (ipAddressString[0] == '[')
            {
                ipAddressString = ipAddressString[1..(ipAddressString.Length - 1)];
            }

            int percentPos = ipAddressString.IndexOf('%');
            if (percentPos >= 0)
            {
                // CLR scope IDs cover the unsigned 32-bit range. Malformed zones are
                // parse failures, not exceptions or interface-name/DNS lookups.
                ReadOnlySpan<char> scope = ipAddressString[(percentPos + 1)..];
                if (scope.IndexOf('\0') >= 0 || !uint.TryParse(scope, NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture, out uint scopeId))
                {
                    return null;
                }
                ipAddressString = ipAddressString[0..percentPos];
                byte[] bytes = GetIPv6ByName(ipAddressString, true);
                if (bytes == null)
                {
                    return null;
                }

                // Should never happen!
                // CLR adaptation: numeric scope IDs are passed to the IPv6 constructor directly.
                return new IPAddress(bytes, scopeId);
            }

            {
                byte[] bytes = GetIPv6ByName(ipAddressString, true);
                if (bytes == null)
                {
                    return null;
                }

                // Should never happen!
                return new IPAddress(bytes);
            }
        }

        return null;
    }

    private static int DecimalDigit(ReadOnlySpan<char> str, int pos)
    {
        return str[pos] - '0';
    }

    private static byte Ipv4WordToByte(ReadOnlySpan<char> ip, int from, int toExclusive)
    {
        int ret = DecimalDigit(ip, from);
        from++;
        if (from == toExclusive)
        {
            return (byte)ret;
        }

        ret = ret * 10 + DecimalDigit(ip, from);
        from++;
        if (from == toExclusive)
        {
            return (byte)ret;
        }

        return (byte)(ret * 10 + DecimalDigit(ip, from));
    }

    // visible for tests
    internal static byte[] ValidIpV4ToBytes(ReadOnlySpan<char> ip)
    {
        static int FindDot(ReadOnlySpan<char> source, int from)
        {
            int relative = source[from..].IndexOf('.');
            return relative < 0 ? -1 : from + relative;
        }

        int i;
        return new byte[]
        {
            Ipv4WordToByte(ip, 0, i = FindDot(ip, 1)),
            Ipv4WordToByte(ip, i + 1, i = FindDot(ip, i + 2)),
            Ipv4WordToByte(ip, i + 1, i = FindDot(ip, i + 2)),
            Ipv4WordToByte(ip, i + 1, ip.Length)
        };
    }

    /**
     * Convert {@link Inet4Address} into {@code int}
     */
    public static int Ipv4AddressToInt(IPAddress ipAddress)
    {
        ArgumentNullException.ThrowIfNull(ipAddress);
        if (ipAddress.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("An IPv4 address is required.", nameof(ipAddress));
        Span<byte> octets = stackalloc byte[4];
        ipAddress.TryWriteBytes(octets, out _);
        return BinaryPrimitives.ReadInt32BigEndian(octets);
    }

    /**
     * Converts a 32-bit integer into an IPv4 address.
     */
    public static string IntToIpAddress(int i)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, i);
        return BytesToIpAddress(bytes);
    }

    /**
     * Converts 4-byte or 16-byte data into an IPv4 or IPv6 string respectively.
     *
     * @throws IllegalArgumentException
     *         if {@code length} is not {@code 4} nor {@code 16}
     */
    public static string BytesToIpAddress(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return BytesToIpAddress(bytes.AsSpan());
    }

    /**
     * Converts 4-byte or 16-byte data into an IPv4 or IPv6 string respectively.
     *
     * @throws IllegalArgumentException
     *         if {@code length} is not {@code 4} nor {@code 16}
     */
    public static string BytesToIpAddress(byte[] bytes, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (length != 4 && length != 16)
            throw new ArgumentException("length: " + length + " (expected: 4 or 16)", nameof(length));
        return BytesToIpAddress(bytes.AsSpan(offset, length));
    }

    /// <summary>
    /// Formats exactly four or sixteen network-order bytes without copying the selected view.
    /// IPv6 uses Netty's canonical compression with hexadecimal mapped-address output.
    /// </summary>
    public static string BytesToIpAddress(ReadOnlySpan<byte> bytes)
    {
        switch (bytes.Length)
        {
            case 4:
            {
                return new StringBuilder(15)
                    .Append(bytes[0])
                    .Append('.')
                    .Append(bytes[1])
                    .Append('.')
                    .Append(bytes[2])
                    .Append('.')
                    .Append(bytes[3]).ToString();
            }
            case 16:
                return ToAddressString(bytes, false);
            default:
                throw new ArgumentException("length: " + bytes.Length + " (expected: 4 or 16)", nameof(bytes));
        }
    }

    public static bool IsValidIpV6Address(string ip)
    {
        ArgumentNullException.ThrowIfNull(ip);
        return IsValidIpV6Address(ip.AsSpan());
    }

    /// <summary>Validates the selected UTF-16 view using Netty's IPv6 literal grammar without copying.</summary>
    public static bool IsValidIpV6Address(ReadOnlySpan<char> ip)
    {
        int end = ip.Length;
        if (end < 2)
        {
            return false;
        }

        // strip "[]"
        int start;
        char c = ip[0];
        if (c == '[')
        {
            end--;
            if (ip[end] != ']')
            {
                // must have a close ]
                return false;
            }

            start = 1;
            c = ip[1];
        }
        else
        {
            start = 0;
        }

        int colons;
        int compressBegin;
        if (c == ':')
        {
            // an IPv6 address can start with "::" or with a number
            if (ip[start + 1] != ':')
            {
                return false;
            }

            colons = 2;
            compressBegin = start;
            start += 2;
        }
        else
        {
            colons = 0;
            compressBegin = -1;
        }

        int wordLen = 0;
        for (int i = start; i < end; i++)
        {
            c = ip[i];
            if (IsValidHexChar(c))
            {
                if (wordLen < 4)
                {
                    wordLen++;
                    continue;
                }

                return false;
            }

            switch (c)
            {
                case ':':
                    if (colons > 7)
                    {
                        return false;
                    }

                    if (ip[i - 1] == ':')
                    {
                        if (compressBegin >= 0)
                        {
                            return false;
                        }

                        compressBegin = i - 1;
                    }
                    else
                    {
                        wordLen = 0;
                    }

                    colons++;
                    break;
                case '.':
                    // case for the last 32-bits represented as IPv4 x:x:x:x:x:x:d.d.d.d

                    // check a normal case (6 single colons)
                    if (compressBegin < 0 && colons != 6 ||
                        // a special case ::1:2:3:4:5:d.d.d.d allows 7 colons with an
                        // IPv4 ending, otherwise 7 :'s is bad
                        (colons == 7 && compressBegin >= start || colons > 7))
                    {
                        return false;
                    }

                    // Verify this address is of the correct structure to contain an IPv4 address.
                    // It must be IPv4-Mapped or IPv4-Compatible
                    // (see https://tools.ietf.org/html/rfc4291#section-2.5.5).
                    int ipv4Start = i - wordLen;
                    int j = ipv4Start - 2; // index of character before the previous ':'.
                    if (IsValidIPv4MappedChar(ip[j]))
                    {
                        if (!IsValidIPv4MappedChar(ip[j - 1]) ||
                            !IsValidIPv4MappedChar(ip[j - 2]) ||
                            !IsValidIPv4MappedChar(ip[j - 3]))
                        {
                            return false;
                        }

                        j -= 5;
                    }

                    for (; j >= start; --j)
                    {
                        char tmpChar = ip[j];
                        if (tmpChar != '0' && tmpChar != ':')
                        {
                            return false;
                        }
                    }

                    // 7 - is minimum IPv4 address length
                    int scopeStart = Math.Min(ipv4Start + 7, ip.Length);
                    int scopeIndex = ip[scopeStart..].IndexOf('%');
                    int ipv4End = scopeIndex < 0 ? -1 : scopeStart + scopeIndex;
                    if (ipv4End < 0)
                    {
                        ipv4End = end;
                    }

                    return IsValidIpV4Address(ip, ipv4Start, ipv4End);
                case '%':
                    // strip the interface name/index after the percent sign
                    end = i;
                    goto endLoop;
                default:
                    return false;
            }
        }

        endLoop:
        // normal case without compression
        if (compressBegin < 0)
        {
            return colons == 7 && wordLen > 0;
        }

        return compressBegin + 2 == end ||
               // 8 colons is valid only if compression in start or end
               wordLen > 0 && (colons < 8 || compressBegin <= start);
    }

    private static bool IsValidIpV4Word(ReadOnlySpan<char> word, int from, int toExclusive)
    {
        int len = toExclusive - from;
        char c0, c1, c2;
        if (len < 1 || len > 3 || (c0 = word[from]) < '0')
        {
            return false;
        }

        if (len == 3)
        {
            return (c1 = word[from + 1]) >= '0' &&
                   (c2 = word[from + 2]) >= '0' &&
                   (c0 <= '1' && c1 <= '9' && c2 <= '9' ||
                    c0 == '2' && c1 <= '5' && (c2 <= '5' || c1 < '5' && c2 <= '9'));
        }

        return c0 <= '9' && (len == 1 || IsValidNumericChar(word[from + 1]));
    }

    private static bool IsValidHexChar(char c)
    {
        return c >= '0' && c <= '9' || c >= 'A' && c <= 'F' || c >= 'a' && c <= 'f';
    }

    private static bool IsValidNumericChar(char c)
    {
        return c >= '0' && c <= '9';
    }

    private static bool IsValidIPv4MappedChar(char c)
    {
        return c == 'f' || c == 'F';
    }

    private static bool IsValidIPv4MappedSeparators(byte b0, byte b1, bool mustBeZero)
    {
        // We allow IPv4 Mapped (https://tools.ietf.org/html/rfc4291#section-2.5.5.1)
        // and IPv4 compatible (https://tools.ietf.org/html/rfc4291#section-2.5.5.1).
        // The IPv4 compatible is deprecated, but it allows parsing of plain IPv4 addressed into IPv6-Mapped addresses.
        // Java byte -1 has the same bit pattern as CLR byte 255.
        return b0 == b1 && (b0 == 0 || !mustBeZero && b1 == 0xff);
    }

    private static bool IsValidIPv4Mapped(byte[] bytes, int currentIndex, int compressBegin, int compressLength)
    {
        bool mustBeZero = compressBegin + compressLength >= 14;
        return currentIndex <= 12 && currentIndex >= 2 && (!mustBeZero || compressBegin < 12) &&
               IsValidIPv4MappedSeparators(bytes[currentIndex - 1], bytes[currentIndex - 2], mustBeZero) &&
               PlatformDependent.IsZero(bytes, 0, currentIndex - 3);
    }

    /**
     * Takes a {@link CharSequence} and parses it to see if it is a valid IPV4 address.
     *
     * @return true, if the string represents an IPV4 address in dotted
     *         notation, false otherwise
     */
    /// <summary>Validates the selected UTF-16 view as four decimal IPv4 octets without copying.</summary>
    public static bool IsValidIpV4Address(ReadOnlySpan<char> ip)
    {
        return IsValidIpV4Address(ip, 0, ip.Length);
    }

    /**
     * Takes a {@link String} and parses it to see if it is a valid IPV4 address.
     *
     * @return true, if the string represents an IPV4 address in dotted
     *         notation, false otherwise
     */
    public static bool IsValidIpV4Address(string ip)
    {
        ArgumentNullException.ThrowIfNull(ip);
        return IsValidIpV4Address(ip.AsSpan());
    }

    //@SuppressWarnings("DuplicateBooleanBranch")
    private static bool IsValidIpV4Address(ReadOnlySpan<char> ip, int from, int toExcluded)
    {
        static int FindDot(ReadOnlySpan<char> source, int start)
        {
            if (start >= source.Length)
            {
                return -1;
            }

            int relative = source[start..].IndexOf('.');
            return relative < 0 ? -1 : start + relative;
        }

        int len = toExcluded - from;
        int i;
        return len <= 15 && len >= 7 &&
               (i = FindDot(ip, from + 1)) > 0 && IsValidIpV4Word(ip, from, i) &&
               (i = FindDot(ip, from = i + 2)) > 0 && IsValidIpV4Word(ip, from - 1, i) &&
               (i = FindDot(ip, from = i + 2)) > 0 && IsValidIpV4Word(ip, from - 1, i) &&
               IsValidIpV4Word(ip, i + 1, toExcluded);
    }

    /**
     * Returns the {@link Inet6Address} representation of a {@link CharSequence} IP address.
     * <p>
     * This method will treat all IPv4 type addresses as "IPv4 mapped" (see {@link #getByName(CharSequence, boolean)})
     * @param ip {@link CharSequence} IP address to be converted to a {@link Inet6Address}
     * @return {@link Inet6Address} representation of the {@code ip} or {@code null} if not a valid IP address.
     */
    /// <summary>Parses an unbracketed, unscoped literal as an owned IPv6 address, mapping IPv4 input.</summary>
    public static IPAddress GetByName(ReadOnlySpan<char> ip) => GetByName(ip, true);

    public static IPAddress GetByName(string ip)
    {
        ArgumentNullException.ThrowIfNull(ip);
        return GetByName(ip.AsSpan(), true);
    }

    /**
     * Returns the {@link Inet6Address} representation of a {@link CharSequence} IP address.
     * <p>
     * The {@code ipv4Mapped} parameter specifies how IPv4 addresses should be treated.
     * "IPv4 mapped" format as
     * defined in <a href="https://tools.ietf.org/html/rfc4291#section-2.5.5">rfc 4291 section 2</a> is supported.
     * @param ip {@link CharSequence} IP address to be converted to a {@link Inet6Address}
     * @param ipv4Mapped
     * <ul>
     * <li>{@code true} To allow IPv4 mapped inputs to be translated into {@link Inet6Address}</li>
     * <li>{@code false} Consider IPv4 mapped addresses as invalid.</li>
     * </ul>
     * @return {@link Inet6Address} representation of the {@code ip} or {@code null} if not a valid IP address.
     */
    public static IPAddress GetByName(string ip, bool ipv4Mapped)
    {
        ArgumentNullException.ThrowIfNull(ip);
        return GetByName(ip.AsSpan(), ipv4Mapped);
    }

    /// <summary>
    /// Parses the selected literal without DNS lookup. When ipv4Mapped is false,
    /// plain IPv4 and dotted IPv4-in-IPv6 forms are rejected. Invalid text returns null.
    /// </summary>
    public static IPAddress GetByName(ReadOnlySpan<char> ip, bool ipv4Mapped)
    {
        byte[] bytes = GetIPv6ByName(ip, ipv4Mapped);
        if (bytes == null)
        {
            return null;
        }

        // Should never happen
        // CLR adaptation: scope 0 means an unspecified zone; CLR rejects Java's -1 sentinel.
        return new IPAddress(bytes, 0);
    }

    /**
     * Returns the byte array representation of a {@link CharSequence} IP address.
     * <p>
     * The {@code ipv4Mapped} parameter specifies how IPv4 addresses should be treated.
     * "IPv4 mapped" format as
     * defined in <a href="https://tools.ietf.org/html/rfc4291#section-2.5.5">rfc 4291 section 2</a> is supported.
     * @param ip {@link CharSequence} IP address to be converted to a {@link Inet6Address}
     * @param ipv4Mapped
     * <ul>
     * <li>{@code true} To allow IPv4 mapped inputs to be translated into {@link Inet6Address}</li>
     * <li>{@code false} Consider IPv4 mapped addresses as invalid.</li>
     * </ul>
     * @return byte array representation of the {@code ip} or {@code null} if not a valid IP address.
     */
    // visible for test
    internal static byte[] GetIPv6ByName(ReadOnlySpan<char> ip, bool ipv4Mapped)
    {
        byte[] bytes = new byte[IPV6_BYTE_COUNT];
        int ipLength = ip.Length;
        int compressBegin = 0;
        int compressLength = 0;
        int currentIndex = 0;
        int value = 0;
        int begin = -1;
        int i = 0;
        int ipv6Separators = 0;
        int ipv4Separators = 0;
        int tmp;
        for (; i < ipLength; ++i)
        {
            char c = ip[i];
            switch (c)
            {
                case ':':
                    ++ipv6Separators;
                    if (i - begin > IPV6_MAX_CHAR_BETWEEN_SEPARATOR ||
                        ipv4Separators > 0 || ipv6Separators > IPV6_MAX_SEPARATORS ||
                        currentIndex + 1 >= bytes.Length)
                    {
                        return null;
                    }

                    value <<= (IPV6_MAX_CHAR_BETWEEN_SEPARATOR - (i - begin)) << 2;

                    if (compressLength > 0)
                    {
                        compressLength -= 2;
                    }

                    // The value integer holds at most 4 bytes from right (most significant) to left (least significant).
                    // The following bit shifting is used to extract and re-order the individual bytes to achieve a
                    // left (most significant) to right (least significant) ordering.
                    bytes[currentIndex++] = (byte)(((value & 0xf) << 4) | ((value >> 4) & 0xf));
                    bytes[currentIndex++] = (byte)((((value >> 8) & 0xf) << 4) | ((value >> 12) & 0xf));
                    tmp = i + 1;
                    if (tmp < ipLength && ip[tmp] == ':')
                    {
                        ++tmp;
                        if (compressBegin != 0 || (tmp < ipLength && ip[tmp] == ':'))
                        {
                            return null;
                        }

                        ++ipv6Separators;
                        compressBegin = currentIndex;
                        compressLength = bytes.Length - compressBegin - 2;
                        ++i;
                    }

                    value = 0;
                    begin = -1;
                    break;
                case '.':
                    ++ipv4Separators;
                    tmp = i - begin; // tmp is the length of the current segment.
                    if (tmp > IPV4_MAX_CHAR_BETWEEN_SEPARATOR
                        || begin < 0
                        || ipv4Separators > IPV4_SEPARATORS
                        || (ipv6Separators > 0 && (currentIndex + compressLength < 12))
                        || i + 1 >= ipLength
                        || currentIndex >= bytes.Length
                        || ipv4Separators == 1 &&
                        // We also parse pure IPv4 addresses as IPv4-Mapped for ease of use.
                        ((!ipv4Mapped || currentIndex != 0 && !IsValidIPv4Mapped(bytes, currentIndex,
                             compressBegin, compressLength)) ||
                         (tmp == 3 && (!IsValidNumericChar(ip[i - 1]) ||
                                       !IsValidNumericChar(ip[i - 2]) ||
                                       !IsValidNumericChar(ip[i - 3])) ||
                          tmp == 2 && (!IsValidNumericChar(ip[i - 1]) ||
                                       !IsValidNumericChar(ip[i - 2])) ||
                          tmp == 1 && !IsValidNumericChar(ip[i - 1]))))
                    {
                        return null;
                    }

                    value <<= (IPV4_MAX_CHAR_BETWEEN_SEPARATOR - tmp) << 2;

                    // The value integer holds at most 3 bytes from right (most significant) to left (least significant).
                    // The following bit shifting is to restructure the bytes to be left (most significant) to
                    // right (least significant) while also accounting for each IPv4 digit is base 10.
                    begin = (value & 0xf) * 100 + ((value >> 4) & 0xf) * 10 + ((value >> 8) & 0xf);
                    if (begin > 255)
                    {
                        return null;
                    }

                    bytes[currentIndex++] = (byte)begin;
                    value = 0;
                    begin = -1;
                    break;
                default:
                    if (!IsValidHexChar(c) || (ipv4Separators > 0 && !IsValidNumericChar(c)))
                    {
                        return null;
                    }

                    if (begin < 0)
                    {
                        begin = i;
                    }
                    else if (i - begin > IPV6_MAX_CHAR_BETWEEN_SEPARATOR)
                    {
                        return null;
                    }

                    // The value is treated as a sort of array of numbers because we are dealing with
                    // at most 4 consecutive bytes we can use bit shifting to accomplish this.
                    // The most significant byte will be encountered first, and reside in the right most
                    // position of the following integer
                    value += StringUtil.DecodeHexNibble(c) << ((i - begin) << 2);
                    break;
            }
        }

        bool isCompressed = compressBegin > 0;
        // Finish up last set of data that was accumulated in the loop (or before the loop)
        if (ipv4Separators > 0)
        {
            if (begin > 0 && i - begin > IPV4_MAX_CHAR_BETWEEN_SEPARATOR ||
                ipv4Separators != IPV4_SEPARATORS ||
                currentIndex >= bytes.Length)
            {
                return null;
            }

            if (!(ipv6Separators == 0 || ipv6Separators >= IPV6_MIN_SEPARATORS &&
                    (!isCompressed && (ipv6Separators == 6 && ip[0] != ':') ||
                     isCompressed && (ipv6Separators < IPV6_MAX_SEPARATORS &&
                                      (ip[0] != ':' || compressBegin <= 2)))))
            {
                return null;
            }

            value <<= (IPV4_MAX_CHAR_BETWEEN_SEPARATOR - (i - begin)) << 2;

            // The value integer holds at most 3 bytes from right (most significant) to left (least significant).
            // The following bit shifting is to restructure the bytes to be left (most significant) to
            // right (least significant) while also accounting for each IPv4 digit is base 10.
            begin = (value & 0xf) * 100 + ((value >> 4) & 0xf) * 10 + ((value >> 8) & 0xf);
            if (begin > 255)
            {
                return null;
            }

            bytes[currentIndex++] = (byte)begin;
        }
        else
        {
            tmp = ipLength - 1;
            if (begin > 0 && i - begin > IPV6_MAX_CHAR_BETWEEN_SEPARATOR ||
                ipv6Separators < IPV6_MIN_SEPARATORS ||
                !isCompressed && (ipv6Separators + 1 != IPV6_MAX_SEPARATORS ||
                                  ip[0] == ':' || ip[tmp] == ':') ||
                isCompressed && (ipv6Separators > IPV6_MAX_SEPARATORS ||
                                 (ipv6Separators == IPV6_MAX_SEPARATORS &&
                                  (compressBegin <= 2 && ip[0] != ':' ||
                                   compressBegin >= 14 && ip[tmp] != ':'))) ||
                currentIndex + 1 >= bytes.Length ||
                begin < 0 && ip[tmp - 1] != ':' ||
                compressBegin > 2 && ip[0] == ':')
            {
                return null;
            }

            if (begin >= 0 && i - begin <= IPV6_MAX_CHAR_BETWEEN_SEPARATOR)
            {
                value <<= (IPV6_MAX_CHAR_BETWEEN_SEPARATOR - (i - begin)) << 2;
            }

            // The value integer holds at most 4 bytes from right (most significant) to left (least significant).
            // The following bit shifting is used to extract and re-order the individual bytes to achieve a
            // left (most significant) to right (least significant) ordering.
            bytes[currentIndex++] = (byte)(((value & 0xf) << 4) | ((value >> 4) & 0xf));
            bytes[currentIndex++] = (byte)((((value >> 8) & 0xf) << 4) | ((value >> 12) & 0xf));
        }

        if (currentIndex < bytes.Length)
        {
            int toBeCopiedLength = currentIndex - compressBegin;
            int targetIndex = bytes.Length - toBeCopiedLength;
            Arrays.Arraycopy(bytes, compressBegin, bytes, targetIndex, toBeCopiedLength);
            // targetIndex is also the `toIndex` to fill 0
            Arrays.Fill(bytes, compressBegin, targetIndex, (byte)0);
        }

        if (ipv4Separators > 0)
        {
            // We only support IPv4-Mapped addresses [1] because IPv4-Compatible addresses are deprecated [2].
            // [1] https://tools.ietf.org/html/rfc4291#section-2.5.5.2
            // [2] https://tools.ietf.org/html/rfc4291#section-2.5.5.1
            bytes[10] = bytes[11] = (byte)0xff;
        }

        return bytes;
    }

    /**
     * Returns the {@link String} representation of an {@link InetSocketAddress}.
     * <p>
     * The output does not include Scope ID.
     * @param addr {@link InetSocketAddress} to be converted to an address string
     * @return {@code String} containing the text-formatted IP address
     */
    /// <summary>
    /// Formats an IPEndPoint or DnsEndPoint without DNS lookup. Resolved IPv6
    /// addresses omit their scope; unresolved host text is preserved.
    /// </summary>
    public static string ToSocketAddressString(EndPoint addr)
    {
        ArgumentNullException.ThrowIfNull(addr);
        if (addr is DnsEndPoint dnsAddress)
            return ToSocketAddressString(dnsAddress.Host, dnsAddress.Port);
        if (addr is not IPEndPoint ipAddress)
            throw new ArgumentException("Unsupported endpoint type.", nameof(addr));

        string port = ipAddress.Port.ToString(CultureInfo.InvariantCulture);
        StringBuilder sb;

        // CLR adaptation: IPEndPoint always contains a resolved address; formatting needs no DNS lookup.
        IPAddress address = ipAddress.Address;
        string hostString = ToAddressString(address);
        sb = NewSocketAddressStringBuilder(hostString, port, address.AddressFamily == AddressFamily.InterNetwork);
        return sb.Append(':').Append(port).ToString();
    }

    /**
     * Returns the {@link String} representation of a host port combo.
     */
    public static string ToSocketAddressString(string host, int port)
    {
        ArgumentNullException.ThrowIfNull(host);
        string portStr = port.ToString(CultureInfo.InvariantCulture);
        return NewSocketAddressStringBuilder(
            host, portStr, !IsValidIpV6Address(host)).Append(':').Append(portStr).ToString();
    }

    private static StringBuilder NewSocketAddressStringBuilder(string host, string port, bool ipv4)
    {
        int hostLen = host.Length;
        if (ipv4)
        {
            // Need to include enough space for hostString:port.
            return new StringBuilder(hostLen + 1 + port.Length).Append(host);
        }

        // Need to include enough space for [hostString]:port.
        StringBuilder stringBuilder = new StringBuilder(hostLen + 3 + port.Length);
        if (hostLen > 1 && host[0] == '[' && host[hostLen - 1] == ']')
        {
            return stringBuilder.Append(host);
        }

        return stringBuilder.Append('[').Append(host).Append(']');
    }

    /**
     * Returns the {@link String} representation of an {@link InetAddress}.
     * <ul>
     * <li>Inet4Address results are identical to {@link InetAddress#getHostAddress()}</li>
     * <li>Inet6Address results adhere to
     * <a href="https://tools.ietf.org/html/rfc5952#section-4">rfc 5952 section 4</a></li>
     * </ul>
     * <p>
     * The output does not include Scope ID.
     * @param ip {@link InetAddress} to be converted to an address string
     * @return {@code String} containing the text-formatted IP address
     */
    public static string ToAddressString(IPAddress ip)
    {
        return ToAddressString(ip, false);
    }

    /**
     * Returns the {@link String} representation of an {@link InetAddress}.
     * <ul>
     * <li>Inet4Address results are identical to {@link InetAddress#getHostAddress()}</li>
     * <li>Inet6Address results adhere to
     * <a href="https://tools.ietf.org/html/rfc5952#section-4">rfc 5952 section 4</a> if
     * {@code ipv4Mapped} is false.  If {@code ipv4Mapped} is true then "IPv4 mapped" format
     * from <a href="https://tools.ietf.org/html/rfc4291#section-2.5.5">rfc 4291 section 2</a> will be supported.
     * The compressed result will always obey the compression rules defined in
     * <a href="https://tools.ietf.org/html/rfc5952#section-4">rfc 5952 section 4</a></li>
     * </ul>
     * <p>
     * The output does not include Scope ID.
     * @param ip {@link InetAddress} to be converted to an address string
     * @param ipv4Mapped
     * <ul>
     * <li>{@code true} to stray from strict rfc 5952 and support the "IPv4 mapped" format
     * defined in <a href="https://tools.ietf.org/html/rfc4291#section-2.5.5">rfc 4291 section 2</a> while still
     * following the updated guidelines in
     * <a href="https://tools.ietf.org/html/rfc5952#section-4">rfc 5952 section 4</a></li>
     * <li>{@code false} to strictly follow rfc 5952</li>
     * </ul>
     * @return {@code String} containing the text-formatted IP address
     */
    public static string ToAddressString(IPAddress ip, bool ipv4Mapped)
    {
        ArgumentNullException.ThrowIfNull(ip);
        if (ip.AddressFamily == AddressFamily.InterNetwork)
            return ip.ToString();

        if (ip.AddressFamily != AddressFamily.InterNetworkV6)
        {
            throw new ArgumentException("Unhandled type: " + ip);
        }

        Span<byte> bytes = stackalloc byte[IPV6_BYTE_COUNT];
        ip.TryWriteBytes(bytes, out _);
        return ToAddressString(bytes, ipv4Mapped);
    }

    private static string ToAddressString(ReadOnlySpan<byte> bytes, bool ipv4Mapped)
    {
        Span<int> words = stackalloc int[IPV6_WORD_COUNT];
        for (int i = 0; i < words.Length; ++i)
        {
            int idx = i << 1;
            words[i] = ((bytes[idx] & 0xff) << 8) | (bytes[idx + 1] & 0xff);
        }

        // Find longest run of 0s, tie goes to first found instance
        int currentStart = -1;
        int currentLength;
        int shortestStart = -1;
        int shortestLength = 0;
        for (int i = 0; i < words.Length; ++i)
        {
            if (words[i] == 0)
            {
                if (currentStart < 0)
                {
                    currentStart = i;
                }
            }
            else if (currentStart >= 0)
            {
                currentLength = i - currentStart;
                if (currentLength > shortestLength)
                {
                    shortestStart = currentStart;
                    shortestLength = currentLength;
                }

                currentStart = -1;
            }
        }

        // If the array ends on a streak of zeros, make sure we account for it
        if (currentStart >= 0)
        {
            currentLength = words.Length - currentStart;
            if (currentLength > shortestLength)
            {
                shortestStart = currentStart;
                shortestLength = currentLength;
            }
        }

        // Ignore the longest streak if it is only 1 long
        if (shortestLength == 1)
        {
            shortestLength = 0;
            shortestStart = -1;
        }

        // Translate to string taking into account longest consecutive 0s
        int shortestEnd = shortestStart + shortestLength;
        StringBuilder b = new StringBuilder(IPV6_MAX_CHAR_COUNT);
        if (shortestEnd < 0)
        {
            // Optimization when there is no compressing needed
            b.Append(words[0].ToString("x"));
            for (int i = 1; i < words.Length; ++i)
            {
                b.Append(':');
                b.Append(words[i].ToString("x"));
                ;
            }
        }
        else
        {
            // General case that can handle compressing (and not compressing)
            // Loop unroll the first index (so we don't constantly check i==0 cases in loop)
            bool isIpv4Mapped;
            if (InRangeEndExclusive(0, shortestStart, shortestEnd))
            {
                b.Append("::");
                isIpv4Mapped = ipv4Mapped && (shortestEnd == 5 && words[5] == 0xffff);
            }
            else
            {
                b.Append(words[0].ToString("x"));
                isIpv4Mapped = false;
            }

            for (int i = 1; i < words.Length; ++i)
            {
                if (!InRangeEndExclusive(i, shortestStart, shortestEnd))
                {
                    if (!InRangeEndExclusive(i - 1, shortestStart, shortestEnd))
                    {
                        // If the last index was not part of the shortened sequence
                        if (!isIpv4Mapped || i == 6)
                        {
                            b.Append(':');
                        }
                        else
                        {
                            b.Append('.');
                        }
                    }

                    if (isIpv4Mapped && i > 5)
                    {
                        b.Append(words[i] >> 8);
                        b.Append('.');
                        b.Append(words[i] & 0xff);
                    }
                    else
                    {
                        b.Append(words[i].ToString("x"));
                    }
                }
                else if (!InRangeEndExclusive(i - 1, shortestStart, shortestEnd))
                {
                    // If we are in the shortened sequence and the last index was not
                    b.Append("::");
                }
            }
        }

        return b.ToString();
    }

    /**
     * Returns {@link InetSocketAddress#getHostString()}.
     * @param addr The address
     * @return the host string
     */
    /// <summary>
    /// Gets DnsEndPoint.Host or the numeric IPEndPoint address without DNS lookup.
    /// Preserve the DnsEndPoint separately when the original host name is needed
    /// after resolving it to an IPEndPoint.
    /// </summary>
    public static string GetHostname(EndPoint addr)
    {
        ArgumentNullException.ThrowIfNull(addr);
        return addr switch
        {
            DnsEndPoint dnsAddress => dnsAddress.Host,
            IPEndPoint ipAddress => ipAddress.Address.ToString(),
            _ => throw new ArgumentException("Unsupported endpoint type.", nameof(addr))
        };
    }

    /**
     * Does a range check on {@code value} if is within {@code start} (inclusive) and {@code end} (exclusive).
     * @param value The value to checked if is within {@code start} (inclusive) and {@code end} (exclusive)
     * @param start The start of the range (inclusive)
     * @param end The end of the range (exclusive)
     * @return
     * <ul>
     * <li>{@code true} if {@code value} if is within {@code start} (inclusive) and {@code end} (exclusive)</li>
     * <li>{@code false} otherwise</li>
     * </ul>
     */
    private static bool InRangeEndExclusive(int value, int start, int end)
    {
        return value >= start && value < end;
    }
}

internal static class SoMaxConnAction
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(SoMaxConnAction));

    public static int Run()
    {
        // Determine the default somaxconn (server socket backlog) value of the platform.
        // The known defaults:
        // - Windows NT Server 4.0+: 200
        // - Mac OS X: 128
        // - Linux kernel > 5.4 : 4096
        int somaxconn;
        if (OperatingSystem.IsWindows())
        {
            somaxconn = 200;
        }
        else if (OperatingSystem.IsMacOS())
        {
            somaxconn = 128;
        }
        else
        {
            somaxconn = 4096;
        }

        return Run(somaxconn, File.Exists,
            path => File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read),
            () => SystemPropertyUtil.GetBoolean("io.netty.net.somaxconn.trySysctl", false), NetUtil.SysctlGetInt);
    }

    internal static int Run(int somaxconn, Func<string, bool> fileExists, Func<string, Stream> openFile,
        Func<bool> trySysctl, Func<string, int?> sysctlGetInt)
    {
        ArgumentNullException.ThrowIfNull(fileExists);
        ArgumentNullException.ThrowIfNull(openFile);
        ArgumentNullException.ThrowIfNull(trySysctl);
        ArgumentNullException.ThrowIfNull(sysctlGetInt);
        string file = "/proc/sys/net/core/somaxconn";
        try
        {
            // file.exists() may throw a SecurityException if a SecurityManager is used, so execute it in the
            // try / catch block.
            // See https://github.com/netty/netty/issues/4936
            if (fileExists(file))
            {
                using var reader = new StreamReader(new BoundedStream(openFile(file)));
                var line = reader.ReadLine();
                somaxconn = int.Parse(line, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                if (logger.IsDebugEnabled())
                {
                    logger.Debug("{}: {}", file, somaxconn);
                }
            }
            else
            {
                // Try to get from sysctl
                int? tmp = null;
                if (trySysctl())
                {
                    tmp = sysctlGetInt("kern.ipc.somaxconn");
                    if (tmp == null)
                    {
                        tmp = sysctlGetInt("kern.ipc.soacceptqueue");
                        if (tmp != null)
                        {
                            somaxconn = tmp.Value;
                        }
                    }
                    else
                    {
                        somaxconn = tmp.Value;
                    }
                }

                if (tmp == null)
                {
                    logger.Debug($"Failed to get SOMAXCONN from sysctl and file {file}. Default: {somaxconn}");
                }
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (logger.IsDebugEnabled())
            {
                logger.Debug($"Failed to get SOMAXCONN from sysctl and file {file}. Default: {somaxconn}", e);
            }
        }

        return somaxconn;
    }
}
