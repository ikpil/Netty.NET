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
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Netty.NET.Common;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Collections.JCTools;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using static Netty.NET.Common.Internal.PlatformDependent0;

namespace Netty.NET.Common.Internal;

/**
 * Utility that detects various properties specific to the current runtime
 * environment, such as Java version and the availability of the
 * {@code sun.misc.Unsafe} object.
 * <p>
 * You can disable the use of {@code sun.misc.Unsafe} if you specify
 * the system property <strong>io.netty.noUnsafe</strong>.
 */
public static class PlatformDependent
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.getInstance(typeof(PlatformDependent));

    private static readonly bool MAYBE_SUPER_USER;

    private static readonly bool CAN_ENABLE_TCP_NODELAY_BY_DEFAULT = !isAndroid();

    private static readonly Exception UNSAFE_UNAVAILABILITY_CAUSE = unsafeUnavailabilityCause0();

    public static readonly int MPSC_CHUNK_SIZE = 1024;
    public static readonly int MIN_MAX_MPSC_CAPACITY = MPSC_CHUNK_SIZE * 2;
    public static readonly int MAX_ALLOWED_MPSC_CAPACITY = Pow2.MAX_POW2;
    private static readonly long BYTE_ARRAY_BASE_OFFSET = byteArrayBaseOffset0();
    private static readonly DirectoryInfo TMPDIR = tmpdir0();
    private static readonly int BIT_MODE = bitMode0();
    private static readonly string NORMALIZED_ARCH = normalizeArch(SystemPropertyUtil.get("os.arch", RuntimeInformation.ProcessArchitecture.ToString()));
    private static readonly string NORMALIZED_OS = normalizeOs(SystemPropertyUtil.get("os.name",
        OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "Mac OS X" :
        OperatingSystem.IsLinux() ? "Linux" : RuntimeInformation.OSDescription));
    private static readonly ISet<string> LINUX_OS_CLASSIFIERS;
    private static readonly bool IS_WINDOWS = isWindows0();
    private static readonly bool IS_OSX = isOsx0();
    private static readonly bool IS_J9_JVM = isJ9Jvm0();
    private static readonly bool IS_IVKVM_DOT_NET = isIkvmDotNet0();
    private static readonly int ADDRESS_SIZE = addressSize0();
    private static readonly string LINUX_ID_PREFIX = "ID=";
    private static readonly string LINUX_ID_LIKE_PREFIX = "ID_LIKE=";
    public static readonly bool BIG_ENDIAN_NATIVE_ORDER = ByteOrder.nativeOrder() == ByteOrder.BIG_ENDIAN;
    private static readonly bool JFR;

    // For specifications, see https://www.freedesktop.org/software/systemd/man/os-release.html
    public static void addFilesystemOsClassifiers(ISet<string> availableClassifiers) {
        if (processOsReleaseFile("/etc/os-release", availableClassifiers)) {
            return;
        }
        processOsReleaseFile("/usr/lib/os-release", availableClassifiers);
    }

    private static bool processOsReleaseFile(string osReleaseFileName, ISet<string> availableClassifiers)
    {
        if (string.IsNullOrEmpty(osReleaseFileName) || availableClassifiers == null)
            return false;

        string file = osReleaseFileName;
        try {
            if (File.Exists(file))
            {
                try
                {
                    using var stream = File.OpenRead(osReleaseFileName);
                    using var reader = new StreamReader(stream, Encoding.UTF8);

                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.StartsWith(LINUX_ID_PREFIX))
                        {
                            string id = normalizeOsReleaseVariableValue(line[LINUX_ID_PREFIX.Length..]);
                            addClassifier(availableClassifiers, id);
                        }
                        else if (line.StartsWith(LINUX_ID_LIKE_PREFIX))
                        {
                            line = normalizeOsReleaseVariableValue(line[LINUX_ID_LIKE_PREFIX.Length..]);
                            addClassifier(availableClassifiers, line.Split(" "));
                        }
                    }
                } catch (SecurityException e) {
                    logger.debug("Unable to read {}", osReleaseFileName, e);
                } catch (IOException e) {
                    logger.debug("Error while reading content of {}", osReleaseFileName, e);
                }
                // specification states we should only fall back if /etc/os-release does not exist
                return true;
            }

        } catch (SecurityException e) {
            logger.debug("Unable to check if {} exists", osReleaseFileName, e);
        }
        return false;
    }

    public static bool addPropertyOsClassifiers(ISet<string> availableClassifiers) {
        // empty: -Dio.netty.osClassifiers (no distro specific classifiers for native libs)
        // single ID: -Dio.netty.osClassifiers=ubuntu
        // pair ID, ID_LIKE: -Dio.netty.osClassifiers=ubuntu,debian
        // illegal otherwise
        string osClassifiersPropertyName = "io.netty.osClassifiers";
        string osClassifiers = SystemPropertyUtil.get(osClassifiersPropertyName);
        if (osClassifiers == null) {
            return false;
        }
        if (osClassifiers.isEmpty()) {
            // let users omit classifiers with just -Dio.netty.osClassifiers
            return true;
        }
        string[] classifiers = osClassifiers.Split(',');
        // String.split(regex) discards trailing empty fields on the JVM.
        int classifierCount = classifiers.Length;
        while (classifierCount > 0 && classifiers[classifierCount - 1].Length == 0) classifierCount--;
        Array.Resize(ref classifiers, classifierCount);
        if (classifiers.Length == 0) {
            throw new ArgumentException(
                    osClassifiersPropertyName + " property is not empty, but contains no classifiers: "
                            + osClassifiers);
        }
        // at most ID, ID_LIKE classifiers
        if (classifiers.Length > 2) {
            throw new ArgumentException(
                    osClassifiersPropertyName + " property contains more than 2 classifiers: " + osClassifiers);
        }
        foreach (string classifier in classifiers) {
            addClassifier(availableClassifiers, classifier);
        }
        return true;
    }

    public static long byteArrayBaseOffset() {
        return BYTE_ARRAY_BASE_OFFSET;
    }

    /**
     * Returns {@code true} if and only if the current platform is Android
     */
    public static bool isAndroid() {
        return PlatformDependent0.isAndroid();
    }

    /**
     * Return {@code true} if the JVM is running on Windows
     */
    public static bool isWindows() {
        return IS_WINDOWS;
    }

    /**
     * Return {@code true} if the JVM is running on OSX / MacOS
     */
    public static bool isOsx() {
        return IS_OSX;
    }

    /**
     * Return {@code true} if the current user may be a super-user. Be aware that this is just an hint and so it may
     * return false-positives.
     */
    public static bool maybeSuperUser() {
        return MAYBE_SUPER_USER;
    }

    /**
     * @param thread The thread to be checked.
     * @return {@code true} if this {@link Thread} is a virtual thread, {@code false} otherwise.
     */
    public static bool isVirtualThread(Thread thread) {
        return PlatformDependent0.isVirtualThread(thread);
    }

    /**
     * Returns {@code true} if and only if it is fine to enable TCP_NODELAY socket option by default.
     */
    public static bool canEnableTcpNoDelayByDefault() {
        return CAN_ENABLE_TCP_NODELAY_BY_DEFAULT;
    }

    /**
     * Return {@code true} if {@code sun.misc.Unsafe} was found on the classpath and can be used for accelerated
     * direct memory access.
     */
    public static bool hasUnsafe() {
        return UNSAFE_UNAVAILABILITY_CAUSE == null;
    }

    /**
     * Return the reason (if any) why {@code sun.misc.Unsafe} was not available.
     */
    public static Exception getUnsafeUnavailabilityCause() {
        return UNSAFE_UNAVAILABILITY_CAUSE;
    }

    /**
     * {@code true} if and only if the platform supports unaligned access.
     *
     * @see <a href="https://en.wikipedia.org/wiki/Segmentation_fault#Bus_error">Wikipedia on segfault</a>
     */
    public static bool isUnaligned() {
        return PlatformDependent0.isUnaligned();
    }











    /**
     * Returns the temporary directory.
     */
    public static DirectoryInfo tmpdir() {
        return TMPDIR;
    }

    /**
     * Returns the bit mode of the current VM (usually 32 or 64.)
     */
    public static int bitMode() {
        return BIT_MODE;
    }

    /**
     * Return the address size of the OS.
     * 4 (for 32 bits systems ) and 8 (for 64 bits systems).
     */
    public static int addressSize() {
        return ADDRESS_SIZE;
    }

    public static long allocateMemory(long size) {
        return PlatformDependent0.allocateMemory(size);
    }

    public static void freeMemory(long address) {
        PlatformDependent0.freeMemory(address);
    }

    public static long reallocateMemory(long address, long newSize) {
        return PlatformDependent0.reallocateMemory(address, newSize);
    }

    /**
     * Raises an exception bypassing compiler checks for checked exceptions.
     */
    public static void throwException(Exception t) {
        PlatformDependent0.throwException(t);
    }

    /**
     * Creates a new fastest {@link ConcurrentDictionary} implementation for the current platform.
     * @deprecated please use new ConcurrentDictionary<K, V>() directly.
     */
    [Obsolete]
    public static ConcurrentDictionary<K, V> newConcurrentHashMap<K, V>() {
        return new ConcurrentDictionary<K, V>();
    }

    /**
     * Creates a new fastest {@link ConcurrentDictionary} implementation for the current platform.
     * @deprecated please use new ConcurrentDictionary<K, V>() directly.
     */
    [Obsolete]
    public static ConcurrentDictionary<K, V> newConcurrentHashMap<K, V>(int initialCapacity) {
        return new ConcurrentDictionary<K, V>();
    }

    /**
     * Creates a new fastest {@link ConcurrentDictionary} implementation for the current platform.
     * @deprecated please use new ConcurrentDictionary<K, V>() directly.
     */
    [Obsolete]
    public static ConcurrentDictionary<K, V> newConcurrentHashMap<K, V>(int initialCapacity, float loadFactor) {
        return new ConcurrentDictionary<K, V>();
    }

    /**
     * Creates a new fastest {@link ConcurrentDictionary} implementation for the current platform.
     * @deprecated please use new ConcurrentDictionary<K, V>() directly.
     */
    [Obsolete]
    public static ConcurrentDictionary<K, V> newConcurrentHashMap<K, V>(
            int initialCapacity, float loadFactor, int concurrencyLevel)
    {
        return new ConcurrentDictionary<K, V>();
    }

    /**
     * Creates a new fastest {@link ConcurrentDictionary} implementation for the current platform.
     * @deprecated please use new ConcurrentDictionary<K, V>() directly.
     */
    [Obsolete]
    public static ConcurrentDictionary<K, V> newConcurrentHashMap<K, V>(IDictionary<K, V> map) {
        return new ConcurrentDictionary<K, V>(map);
    }



    public static void putShortOrdered(long adddress, short newValue) {
        PlatformDependent0.putShortOrdered(adddress, newValue);
    }

    public static int getIntVolatile(long address) {
        return PlatformDependent0.getIntVolatile(address);
    }

    public static void putIntOrdered(long adddress, int newValue) {
        PlatformDependent0.putIntOrdered(adddress, newValue);
    }

    public static byte getByte(long address) {
        return PlatformDependent0.getByte(address);
    }

    public static short getShort(long address) {
        return PlatformDependent0.getShort(address);
    }

    public static int getInt(long address) {
        return PlatformDependent0.getInt(address);
    }

    public static long getLong(long address) {
        return PlatformDependent0.getLong(address);
    }

    public static byte getByte(byte[] data, int index) {
        return data[index];
    }

    public static byte getByte(byte[] data, long index) {
        return data[toIntExact(index)];
    }

    public static short getShort(byte[] data, int index) {
        return MemoryMarshal.Read<short>(data.AsSpan(index, sizeof(short)));
    }

    public static int getInt(byte[] data, int index) {
        return MemoryMarshal.Read<int>(data.AsSpan(index, sizeof(int)));
    }

    public static int getInt(int[] data, long index) {
        return data[toIntExact(index)];
    }

    public static long getLong(byte[] data, int index) {
        return MemoryMarshal.Read<long>(data.AsSpan(index, sizeof(long)));
    }

    public static long getLong(long[] data, long index) {
        return data[toIntExact(index)];
    }

    private static int toIntExact(long value)
    {
        if (value > int.MaxValue || value < int.MinValue)
        {
            throw new OverflowException("Value out of range for Int32.");
        }
        return (int)value;
    }

    private static long getLongSafe(byte[] bytes, int offset) {
        if (BIG_ENDIAN_NATIVE_ORDER) {
            return (long) bytes[offset] << 56 |
                    ((long) bytes[offset + 1] & 0xff) << 48 |
                    ((long) bytes[offset + 2] & 0xff) << 40 |
                    ((long) bytes[offset + 3] & 0xff) << 32 |
                    ((long) bytes[offset + 4] & 0xff) << 24 |
                    ((long) bytes[offset + 5] & 0xff) << 16 |
                    ((long) bytes[offset + 6] & 0xff) <<  8 |
                    (long) bytes[offset + 7] & 0xff;
        }
        return (long) bytes[offset] & 0xff |
                ((long) bytes[offset + 1] & 0xff) << 8 |
                ((long) bytes[offset + 2] & 0xff) << 16 |
                ((long) bytes[offset + 3] & 0xff) << 24 |
                ((long) bytes[offset + 4] & 0xff) << 32 |
                ((long) bytes[offset + 5] & 0xff) << 40 |
                ((long) bytes[offset + 6] & 0xff) << 48 |
                (long) bytes[offset + 7] << 56;
    }

    private static int getIntSafe(byte[] bytes, int offset) {
        if (BIG_ENDIAN_NATIVE_ORDER) {
            return bytes[offset] << 24 |
                    (bytes[offset + 1] & 0xff) << 16 |
                    (bytes[offset + 2] & 0xff) << 8 |
                    bytes[offset + 3] & 0xff;
        }
        return bytes[offset] & 0xff |
                (bytes[offset + 1] & 0xff) << 8 |
                (bytes[offset + 2] & 0xff) << 16 |
                bytes[offset + 3] << 24;
    }

    private static short getShortSafe(byte[] bytes, int offset) {
        if (BIG_ENDIAN_NATIVE_ORDER) {
            return unchecked((short) (bytes[offset] << 8 | (bytes[offset + 1] & 0xff)));
        }
        return unchecked((short) (bytes[offset] & 0xff | (bytes[offset + 1] << 8)));
    }

    /**
     * Identical to {@link PlatformDependent0#hashCodeAsciiCompute(long, int)} but for {@link CharSequence}.
     */
    private static int hashCodeAsciiCompute(ICharSequence value, int offset, int hash) {
        if (BIG_ENDIAN_NATIVE_ORDER) {
            return unchecked(hash * HASH_CODE_C1 +
                    // Low order int
                    hashCodeAsciiSanitizeInt(value, offset + 4) * HASH_CODE_C2 +
                    // High order int
                    hashCodeAsciiSanitizeInt(value, offset));
        }
        return unchecked(hash * HASH_CODE_C1 +
                // Low order int
                hashCodeAsciiSanitizeInt(value, offset) * HASH_CODE_C2 +
                // High order int
                hashCodeAsciiSanitizeInt(value, offset + 4));
    }

    /**
     * Identical to {@link PlatformDependent0#hashCodeAsciiSanitize(int)} but for {@link CharSequence}.
     */
    private static int hashCodeAsciiSanitizeInt(ICharSequence value, int offset) {
        if (BIG_ENDIAN_NATIVE_ORDER) {
            // mimic a unsafe.getInt call on a big endian machine
            return (value.charAt(offset + 3) & 0x1f) |
                   (value.charAt(offset + 2) & 0x1f) << 8 |
                   (value.charAt(offset + 1) & 0x1f) << 16 |
                   (value.charAt(offset) & 0x1f) << 24;
        }
        return (value.charAt(offset + 3) & 0x1f) << 24 |
               (value.charAt(offset + 2) & 0x1f) << 16 |
               (value.charAt(offset + 1) & 0x1f) << 8 |
               (value.charAt(offset) & 0x1f);
    }

    /**
     * Identical to {@link PlatformDependent0#hashCodeAsciiSanitize(short)} but for {@link CharSequence}.
     */
    private static int hashCodeAsciiSanitizeShort(ICharSequence value, int offset) {
        if (BIG_ENDIAN_NATIVE_ORDER) {
            // mimic a unsafe.getShort call on a big endian machine
            return (value.charAt(offset + 1) & 0x1f) |
                    (value.charAt(offset) & 0x1f) << 8;
        }
        return (value.charAt(offset + 1) & 0x1f) << 8 |
                (value.charAt(offset) & 0x1f);
    }

    /**
     * Identical to {@link PlatformDependent0#hashCodeAsciiSanitize(byte)} but for {@link CharSequence}.
     */
    private static int hashCodeAsciiSanitizeByte(char value) {
        return value & 0x1f;
    }

    public static void putByte(long address, byte value) {
        PlatformDependent0.putByte(address, value);
    }

    public static void putShort(long address, short value) {
        PlatformDependent0.putShort(address, value);
    }

    public static void putInt(long address, int value) {
        PlatformDependent0.putInt(address, value);
    }

    public static void putLong(long address, long value) {
        PlatformDependent0.putLong(address, value);
    }

    public static void putByte(byte[] data, int index, byte value) {
        data[index] = value;
    }

    public static void putShort(byte[] data, int index, short value) {
        MemoryMarshal.Write(data.AsSpan(index, sizeof(short)), in value);
    }

    public static void putInt(byte[] data, int index, int value) {
        MemoryMarshal.Write(data.AsSpan(index, sizeof(int)), in value);
    }

    public static void putLong(byte[] data, int index, long value) {
        MemoryMarshal.Write(data.AsSpan(index, sizeof(long)), in value);
    }

    public static void copyMemory(long srcAddr, long dstAddr, long length) {
        PlatformDependent0.copyMemory(srcAddr, dstAddr, length);
    }

    public static void copyMemory(byte[] src, int srcIndex, long dstAddr, long length) {
        PlatformDependent0.copyMemory(src, BYTE_ARRAY_BASE_OFFSET + srcIndex, null, dstAddr, length);
    }

    public static void copyMemory(byte[] src, int srcIndex, byte[] dst, int dstIndex, long length) {
        ArgumentNullException.ThrowIfNull(src);
        ArgumentNullException.ThrowIfNull(dst);
        int count = checked((int)length);
        // CLR adaptation: bounded CopyTo also supports overlapping ranges. No
        // JVM object-header offset, native pin or manual safe-point loop is needed.
        src.AsSpan(srcIndex, count).CopyTo(dst.AsSpan(dstIndex, count));
    }

    public static void copyMemory(long srcAddr, byte[] dst, int dstIndex, long length) {
        PlatformDependent0.copyMemory(null, srcAddr, dst, BYTE_ARRAY_BASE_OFFSET + dstIndex, length);
    }

    public static void setMemory(byte[] dst, int dstIndex, long bytes, byte value) {
        ArgumentNullException.ThrowIfNull(dst);
        dst.AsSpan(dstIndex, checked((int)bytes)).Fill(value);
    }

    public static void setMemory(long address, long bytes, byte value) {
        PlatformDependent0.setMemory(address, bytes, value);
    }











    public static long align(long value, int alignment) {
        return Pow2.align(value, alignment);
    }

    /**
     * Compare two {@code byte} arrays for equality. For performance reasons no bounds checking on the
     * parameters is performed.
     *
     * @param bytes1 the first byte array.
     * @param startPos1 the position (inclusive) to start comparing in {@code bytes1}.
     * @param bytes2 the second byte array.
     * @param startPos2 the position (inclusive) to start comparing in {@code bytes2}.
     * @param length the amount of bytes to compare. This is assumed to be validated as not going out of bounds
     * by the caller.
     */
    public static bool equals(byte[] bytes1, int startPos1, byte[] bytes2, int startPos2, int length) {
        // CLR adaptation: Span equality is available on the declared target.
        // A JDK-version threshold cannot be applied to Environment.Version.
        if ((startPos2 | startPos1 | (bytes1.Length - length) | bytes2.Length - length) == 0) {
            return bytes1.AsSpan().SequenceEqual(bytes2);
        }
        return !hasUnsafe() || !unalignedAccess() ?
                  equalsSafe(bytes1, startPos1, bytes2, startPos2, length) :
                  PlatformDependent0.equals(bytes1, startPos1, bytes2, startPos2, length);
    }

    /**
     * Determine if a subsection of an array is zero.
     * @param bytes The byte array.
     * @param startPos The starting index (inclusive) in {@code bytes}.
     * @param length The amount of bytes to check for zero.
     * @return {@code false} if {@code bytes[startPos:startsPos+length)} contains a value other than zero.
     */
    public static bool isZero(byte[] bytes, int startPos, int length) {
        return !hasUnsafe() || !unalignedAccess() ?
                isZeroSafe(bytes, startPos, length) :
                PlatformDependent0.isZero(bytes, startPos, length);
    }

    /**
     * Compare two {@code byte} arrays for equality without leaking timing information.
     * For performance reasons no bounds checking on the parameters is performed.
     * <p>
     * The {@code int} return type is intentional and is designed to allow cascading of constant time operations:
     * <pre>
     *     byte[] s1 = new {1, 2, 3};
     *     byte[] s2 = new {1, 2, 3};
     *     byte[] s3 = new {1, 2, 3};
     *     byte[] s4 = new {4, 5, 6};
     *     bool equals = (equalsConstantTime(s1, 0, s2, 0, s1.length) &
     *                       equalsConstantTime(s3, 0, s4, 0, s3.length)) != 0;
     * </pre>
     * @param bytes1 the first byte array.
     * @param startPos1 the position (inclusive) to start comparing in {@code bytes1}.
     * @param bytes2 the second byte array.
     * @param startPos2 the position (inclusive) to start comparing in {@code bytes2}.
     * @param length the amount of bytes to compare. This is assumed to be validated as not going out of bounds
     * by the caller.
     * @return {@code 0} if not equal. {@code 1} if equal.
     */
    public static int equalsConstantTime(byte[] bytes1, int startPos1, byte[] bytes2, int startPos2, int length) {
        return !hasUnsafe() || !unalignedAccess() ?
                  ConstantTimeUtils.equalsConstantTime(bytes1, startPos1, bytes2, startPos2, length) :
                  PlatformDependent0.equalsConstantTime(bytes1, startPos1, bytes2, startPos2, length);
    }

    /**
     * Calculate a hash code of a byte array assuming ASCII character encoding.
     * The resulting hash code will be case insensitive.
     * @param bytes The array which contains the data to hash.
     * @param startPos What index to start generating a hash code in {@code bytes}
     * @param length The amount of bytes that should be accounted for in the computation.
     * @return The hash code of {@code bytes} assuming ASCII character encoding.
     * The resulting hash code will be case insensitive.
     */
    public static int hashCodeAscii(byte[] bytes, int startPos, int length) {
        return !hasUnsafe() || !unalignedAccess() ?
                hashCodeAsciiSafe(bytes, startPos, length) :
                PlatformDependent0.hashCodeAscii(bytes, startPos, length);
    }

    public static int hashCodeAscii(string bytes)
    {
        var scs = new StringCharSequence(bytes);
        return hashCodeAscii(scs);
    }

    /**
     * Calculate a hash code of a byte array assuming ASCII character encoding.
     * The resulting hash code will be case insensitive.
     * <p>
     * This method assumes that {@code bytes} is equivalent to a {@code byte[]} but just using {@link CharSequence}
     * for storage. The upper most byte of each {@code char} from {@code bytes} is ignored.
     * @param bytes The array which contains the data to hash (assumed to be equivalent to a {@code byte[]}).
     * @return The hash code of {@code bytes} assuming ASCII character encoding.
     * The resulting hash code will be case insensitive.
     */
    public static int hashCodeAscii(ICharSequence bytes)
    {
        int length = bytes.length();
        int remainingBytes = length & 7;
        int hash = HASH_CODE_ASCII_SEED;
        // Benchmarking shows that by just naively looping for inputs 8~31 bytes long we incur a relatively large
        // performance penalty (only achieve about 60% performance of loop which iterates over each char). So because
        // of this we take special provisions to unroll the looping for these conditions.
        if (length >= 32) {
            for (int i = length - 8; i >= remainingBytes; i -= 8) {
                hash = hashCodeAsciiCompute(bytes, i, hash);
            }
        } else if (length >= 8) {
            hash = hashCodeAsciiCompute(bytes, length - 8, hash);
            if (length >= 16) {
                hash = hashCodeAsciiCompute(bytes, length - 16, hash);
                if (length >= 24) {
                    hash = hashCodeAsciiCompute(bytes, length - 24, hash);
                }
            }
        }
        if (remainingBytes == 0) {
            return hash;
        }
        int offset = 0;
        if (remainingBytes != 2 & remainingBytes != 4 & remainingBytes != 6) { // 1, 3, 5, 7
            hash = unchecked(hash * HASH_CODE_C1 + hashCodeAsciiSanitizeByte(bytes.charAt(0)));
            offset = 1;
        }
        if (remainingBytes != 1 & remainingBytes != 4 & remainingBytes != 5) { // 2, 3, 6, 7
            hash = unchecked(hash * (offset == 0 ? HASH_CODE_C1 : HASH_CODE_C2)
                    + hashCodeAsciiSanitize(hashCodeAsciiSanitizeShort(bytes, offset)));
            offset += 2;
        }
        if (remainingBytes >= 4) { // 4, 5, 6, 7
            return unchecked(hash * ((offset == 0 | offset == 3) ? HASH_CODE_C1 : HASH_CODE_C2)
                    + hashCodeAsciiSanitizeInt(bytes, offset));
        }
        return hash;
    }

    /**
     * Create a new {@link Queue} which is safe to use for multiple producers (different threads) and a single
     * consumer (one thread!).
     * @return A MPSC queue which may be unbounded.
     */
    public static IQueue<T> newMpscQueue<T>() {
        return Mpsc.newMpscQueue<T>();
    }

    /**
     * Create a new {@link Queue} which is safe to use for multiple producers (different threads) and a single
     * consumer (one thread!).
     */
    public static IQueue<T> newMpscQueue<T>(int maxCapacity) {
        return Mpsc.newMpscQueue<T>(maxCapacity);
    }

    /**
     * Create a new {@link Queue} which is safe to use for multiple producers (different threads) and a single
     * consumer (one thread!).
     * The queue will grow and shrink its capacity in units of the given chunk size.
     */
    public static IQueue<T> newMpscQueue<T>(int chunkSize, int maxCapacity) {
        return Mpsc.newChunkedMpscQueue<T>(chunkSize, maxCapacity);
    }

    /**
     * Create a new {@link Queue} which is safe to use for single producer (one thread!) and a single
     * consumer (one thread!).
     */
    public static IQueue<T> newSpscQueue<T>() {
        throw new NotImplementedException();
        //return hasUnsafe() ? new SpscLinkedQueue<T>() : new SpscLinkedAtomicQueue<T>();
    }

    /**
     * Create a new {@link Queue} which is safe to use for multiple producers (different threads) and a single
     * consumer (one thread!) with the given fixes {@code capacity}.
     */
    public static IQueue<T> newFixedMpscQueue<T>(int capacity) {
        throw new NotImplementedException();
        //return hasUnsafe() ? new MpscArrayQueue<T>(capacity) : new MpscAtomicArrayQueue<T>(capacity);
    }

    /**
     * Create a new un-padded {@link Queue} which is safe to use for multiple producers (different threads) and a single
     * consumer (one thread!) with the given fixes {@code capacity}.<br>
     * This should be preferred to {@link #newFixedMpscQueue(int)} when the queue is not to be heavily contended.
     */
    public static IQueue<T> newFixedMpscUnpaddedQueue<T>(int capacity) {
        throw new NotImplementedException();
        //return hasUnsafe() ? new MpscUnpaddedArrayQueue<T>(capacity) : new MpscAtomicUnpaddedArrayQueue<T>(capacity);
    }

    /**
     * Create a new {@link Queue} which is safe to use for multiple producers (different threads) and multiple
     * consumers with the given fixes {@code capacity}.
     */
    public static IQueue<T> newFixedMpmcQueue<T>(int capacity) {
        throw new NotImplementedException();
        //return hasUnsafe() ? new MpmcArrayQueue<T>(capacity) : new MpmcAtomicArrayQueue<T>(capacity);
    }

    /**
     * Return the {@link ClassLoader} for the given {@link Class}.
     */
    public static Assembly getClassLoader(Type clazz) {
        return PlatformDependent0.getClassLoader(clazz);
    }

    /**
     * Return the context {@link ClassLoader} for the current {@link Thread}.
     */
    public static Assembly getContextClassLoader() {
        return PlatformDependent0.getContextClassLoader();
    }

    /**
     * Return the system {@link ClassLoader}.
     */
    public static Assembly getSystemClassLoader() {
        return PlatformDependent0.getSystemClassLoader();
    }

    /**
     * Returns a new concurrent {@link Deque}.
     */
    public static IQueue<C> newConcurrentDeque<C>()
    {
        throw new NotImplementedException();
        //return new ConcurrentLinkedDeque<C>();
    }

    /**
     * Return a {@link Random} which is not-threadsafe and so can only be used from the same thread.
     * @deprecated Use ThreadLocalRandom.current() instead.
     */
    [Obsolete]
    public static Random threadLocalRandom() {
        return ThreadLocalRandom.current();
    }

    private static bool isWindows0()
    {
        bool windows = string.Equals("windows", NORMALIZED_OS, StringComparison.OrdinalIgnoreCase);
        if (windows) {
            logger.debug("Platform: Windows");
        }
        return windows;
    }

    private static bool isOsx0() {
        bool osx = string.Equals("osx", NORMALIZED_OS, StringComparison.OrdinalIgnoreCase);
        if (osx) {
            logger.debug("Platform: MacOS");
        }
        return osx;
    }

    private static bool maybeSuperUser0() {
        string username = SystemPropertyUtil.get("user.name");
        if (isWindows())
        {
            return "Administrator" == username;
        }
        // Check for root and toor as some BSDs have a toor user that is basically the same as root.
        return "root" == username || "toor" == username;
    }

    private static Exception unsafeUnavailabilityCause0() {
        if (isAndroid()) {
            logger.debug("sun.misc.Unsafe: unavailable (Android)");
            return new NotSupportedException("sun.misc.Unsafe: unavailable (Android)");
        }

        if (isIkvmDotNet()) {
            logger.debug("sun.misc.Unsafe: unavailable (IKVM.NET)");
            return new NotSupportedException("sun.misc.Unsafe: unavailable (IKVM.NET)");
        }

        Exception cause = PlatformDependent0.getUnsafeUnavailabilityCause();
        if (cause != null) {
            return cause;
        }

        try {
            bool hasUnsafe = PlatformDependent0.hasUnsafe();
            logger.debug("sun.misc.Unsafe: {}", hasUnsafe ? "available" : "unavailable");
            return null;
        } catch (Exception t) {
            logger.trace("Could not determine if Unsafe is available", t);
            // Probably failed to initialize PlatformDependent0.
            return new NotSupportedException("Could not determine if Unsafe is available", t);
        }
    }

    /**
     * Returns {@code true} if the running JVM is either <a href="https://developer.ibm.com/javasdk/">IBM J9</a> or
     * <a href="https://www.eclipse.org/openj9/">Eclipse OpenJ9</a>, {@code false} otherwise.
     */
    public static bool isJ9Jvm() {
        return IS_J9_JVM;
    }

    private static bool isJ9Jvm0() {
        string vmName = SystemPropertyUtil.get("java.vm.name", "").ToLower();
        return vmName.StartsWith("ibm j9") || vmName.StartsWith("eclipse openj9");
    }

    /**
     * Returns {@code true} if the running JVM is <a href="https://www.ikvm.net">IKVM.NET</a>, {@code false} otherwise.
     */
    public static bool isIkvmDotNet() {
        return IS_IVKVM_DOT_NET;
    }

    private static bool isIkvmDotNet0() {
        string vmName = SystemPropertyUtil.get(".name", "").ToUpper(CultureInfo.GetCultureInfo("en-US"));
        return vmName.Equals("IKVM.NET");
    }

    /**
     * Compute an estimate of the maximum amount of direct memory available to this JVM.
     * <p>
     * The computation is not cached, so you probably want to use {@link #maxDirectMemory()} instead.
     * <p>
     * This will produce debug log output when called.
     *
     * @return The estimated max direct memory, in bytes.
     */
    //@SuppressWarnings("unchecked")

    private static DirectoryInfo tmpdir0() {
        DirectoryInfo f;
        try {
            f = toDirectory(SystemPropertyUtil.get("io.netty.tmpdir"));
            if (f != null) {
                logger.debug("-Dio.netty.tmpdir: {}", f);
                return f;
            }

            f = toDirectory(SystemPropertyUtil.get("java.io.tmpdir", Path.GetTempPath()));
            if (f != null) {
                logger.debug("-Dio.netty.tmpdir: {} (java.io.tmpdir)", f);
                return f;
            }

            // This shouldn't happen, but just in case ..
            if (isWindows()) {
                f = toDirectory(Environment.GetEnvironmentVariable("TEMP"));
                if (f != null) {
                    logger.debug("-Dio.netty.tmpdir: {} (%TEMP%)", f);
                    return f;
                }

                string userprofile = Environment.GetEnvironmentVariable("USERPROFILE");
                if (userprofile != null) {
                    f = toDirectory(userprofile + "\\AppData\\Local\\Temp");
                    if (f != null) {
                        logger.debug("-Dio.netty.tmpdir: {} (%USERPROFILE%\\AppData\\Local\\Temp)", f);
                        return f;
                    }

                    f = toDirectory(userprofile + "\\Local Settings\\Temp");
                    if (f != null) {
                        logger.debug("-Dio.netty.tmpdir: {} (%USERPROFILE%\\Local Settings\\Temp)", f);
                        return f;
                    }
                }
            } else {
                f = toDirectory(Environment.GetEnvironmentVariable("TMPDIR"));
                if (f != null) {
                    logger.debug("-Dio.netty.tmpdir: {} ($TMPDIR)", f);
                    return f;
                }
            }
        } catch (Exception ignored) {
            // Environment variable inaccessible
        }

        // Last resort.
        if (isWindows()) {
            f = new DirectoryInfo("C:\\Windows\\Temp");
        } else {
            f = new DirectoryInfo("/tmp");
        }

        logger.warn("Failed to get the temporary directory; falling back to: {}", f);
        return f;
    }

    //@SuppressWarnings("ResultOfMethodCallIgnored")
    private static DirectoryInfo toDirectory(string path) {
        if (path == null)
            return null;

        var dir = new DirectoryInfo(path);

        try
        {
            if (!dir.Exists)
                dir.Create();

            if (!dir.Attributes.HasFlag(FileAttributes.Directory))
                return null;

            return dir;
        }
        catch
        {
            return null;
        }
    }

    private static int bitMode0() {
        // Check user-specified bit mode first.
        int bitMode = SystemPropertyUtil.getInt("io.netty.bitMode", 0);
        if (bitMode > 0) {
            logger.debug("-Dio.netty.bitMode: {}", bitMode);
            return bitMode;
        }

        // And then the vendor specific ones which is probably most reliable.
        bitMode = SystemPropertyUtil.getInt("sun.arch.data.model", 0);
        if (bitMode > 0) {
            logger.debug("-Dio.netty.bitMode: {} (sun.arch.data.model)", bitMode);
            return bitMode;
        }
        bitMode = SystemPropertyUtil.getInt("com.ibm.vm.bitmode", 0);
        if (bitMode > 0) {
            logger.debug("-Dio.netty.bitMode: {} (com.ibm.vm.bitmode)", bitMode);
            return bitMode;
        }

        // os.arch also gives us a good hint.
        string arch = SystemPropertyUtil.get("os.arch", "").ToLower(CultureInfo.GetCultureInfo("en-US")).Trim();
        if ("amd64".Equals(arch) || "x86_64".Equals(arch)) {
            bitMode = 64;
        } else if ("i386".Equals(arch) || "i486".Equals(arch) || "i586".Equals(arch) || "i686".Equals(arch)) {
            bitMode = 32;
        }

        if (bitMode > 0) {
            logger.debug("-Dio.netty.bitMode: {} (os.arch: {})", bitMode, arch);
        }

        // Last resort: guess from VM name and then fall back to most common 64-bit mode.
        string vm = SystemPropertyUtil.get("java.vm.name", "").ToLower(CultureInfo.GetCultureInfo("en-US"));
        Regex bitPattern = new Regex("([1-9][0-9]+)-?bit");
        var m = bitPattern.Match(vm);
        if (m.Success) {
            return int.Parse(m.Groups[1].Value);
        } else {
            // CLR adaptation: use the running process width rather than a JVM-name guess.
            return IntPtr.Size * 8;
        }
    }

    private static int addressSize0() {
        // CLR pointer width is available independently of JVM Unsafe.
        return IntPtr.Size;
    }

    private static long byteArrayBaseOffset0() {
        if (!hasUnsafe()) {
            return -1;
        }
        return PlatformDependent0.byteArrayBaseOffset();
    }

    private static bool equalsSafe(byte[] bytes1, int startPos1, byte[] bytes2, int startPos2, int length) {
        int end = startPos1 + length;
        for (; startPos1 < end; ++startPos1, ++startPos2) {
            if (bytes1[startPos1] != bytes2[startPos2]) {
                return false;
            }
        }
        return true;
    }

    private static bool isZeroSafe(byte[] bytes, int startPos, int length) {
        int end = startPos + length;
        for (; startPos < end; ++startPos) {
            if (bytes[startPos] != 0) {
                return false;
            }
        }
        return true;
    }

    /**
     * Package private for testing purposes only!
     */
    public static int hashCodeAsciiSafe(byte[] bytes, int startPos, int length) {
        int hash = HASH_CODE_ASCII_SEED;
        int remainingBytes = length & 7;
        int end = startPos + remainingBytes;
        for (int i = startPos - 8 + length; i >= end; i -= 8) {
            hash = PlatformDependent0.hashCodeAsciiCompute(getLongSafe(bytes, i), hash);
        }
        switch(remainingBytes) {
        case 7:
            return unchecked(((hash * HASH_CODE_C1 + hashCodeAsciiSanitize(bytes[startPos]))
                          * HASH_CODE_C2 + hashCodeAsciiSanitize(getShortSafe(bytes, startPos + 1)))
                          * HASH_CODE_C1 + hashCodeAsciiSanitize(getIntSafe(bytes, startPos + 3)));
        case 6:
            return unchecked((hash * HASH_CODE_C1 + hashCodeAsciiSanitize(getShortSafe(bytes, startPos)))
                         * HASH_CODE_C2 + hashCodeAsciiSanitize(getIntSafe(bytes, startPos + 2)));
        case 5:
            return unchecked((hash * HASH_CODE_C1 + hashCodeAsciiSanitize(bytes[startPos]))
                         * HASH_CODE_C2 + hashCodeAsciiSanitize(getIntSafe(bytes, startPos + 1)));
        case 4:
            return unchecked(hash * HASH_CODE_C1 + hashCodeAsciiSanitize(getIntSafe(bytes, startPos)));
        case 3:
            return unchecked((hash * HASH_CODE_C1 + hashCodeAsciiSanitize(bytes[startPos]))
                         * HASH_CODE_C2 + hashCodeAsciiSanitize(getShortSafe(bytes, startPos + 1)));
        case 2:
            return unchecked(hash * HASH_CODE_C1 + hashCodeAsciiSanitize(getShortSafe(bytes, startPos)));
        case 1:
            return unchecked(hash * HASH_CODE_C1 + hashCodeAsciiSanitize(bytes[startPos]));
        default:
            return hash;
        }
    }

    public static string normalizedArch() {
        return NORMALIZED_ARCH;
    }

    public static string normalizedOs() {
        return NORMALIZED_OS;
    }

    public static ISet<string> normalizedLinuxClassifiers() {
        return LINUX_OS_CLASSIFIERS;
    }

    public static FileInfo createTempFile(string prefix, string suffix, FileInfo directory) {
        string dirPath = directory?.FullName ?? Path.GetTempPath();

        var randomFileName =Path.GetRandomFileName();
        string fileName = $"{prefix}{randomFileName}{suffix}";
        string filePath = Path.Combine(dirPath, fileName);

        {
            using var fs = File.Create(filePath);
        }

        return new FileInfo(filePath);
    }

    /**
     * Adds only those classifier strings to <tt>dest</tt> which are present in <tt>allowed</tt>.
     *
     * @param dest             destination set
     * @param maybeClassifiers potential classifiers to add
     */
    private static void addClassifier(ISet<string> dest, params string[] maybeClassifiers) {
        foreach (string id in maybeClassifiers) {
            if (isAllowedClassifier(id)) {
                dest.Add(id);
            }
        }
    }
    // keep in sync with maven's pom.xml via os.detection.classifierWithLikes!
    private static bool isAllowedClassifier(string classifier) {
        switch (classifier) {
            case "fedora":
            case "suse":
            case "arch":
                return true;
            default:
                return false;
        }
    }

    //replaces value.trim().replaceAll("[\"']", "") to avoid regexp overhead
    private static string normalizeOsReleaseVariableValue(string value) {
        string trimmed = value.Trim();
        StringBuilder sb = new StringBuilder(trimmed.Length);
        for (int i = 0; i < trimmed.Length; i++) {
            char c = trimmed[i];
            if (c != '"' && c != '\'') {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    //replaces value.toLowerCase(CultureInfo.GetCultureInfo("en-US")).replaceAll("[^a-z0-9]+", "") to avoid regexp overhead
    private static string normalize(string value) {
        StringBuilder sb = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++) {
            char c = char.ToLowerInvariant(value[i]);
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static string normalizeArch(string value) {
        value = normalize(value);
        switch (value) {
            case "x8664":
            case "amd64":
            case "ia32e":
            case "em64t":
            case "x64":
                return "x86_64";

            case "x8632":
            case "x86":
            case "i386":
            case "i486":
            case "i586":
            case "i686":
            case "ia32":
            case "x32":
                return "x86_32";

            case "ia64":
            case "itanium64":
                return "itanium_64";

            case "sparc":
            case "sparc32":
                return "sparc_32";

            case "sparcv9":
            case "sparc64":
                return "sparc_64";

            case "arm":
            case "arm32":
                return "arm_32";

            case "aarch64":
            case "arm64":
                return "aarch_64";

            case "riscv64":
                return "riscv64";

            case "ppc":
            case "ppc32":
                return "ppc_32";

            case "ppc64":
                return "ppc_64";

            case "ppc64le":
                return "ppcle_64";

            case "s390":
                return "s390_32";

            case "s390x":
                return "s390_64";

            case "loongarch64":
                return "loongarch_64";

            default:
                return "unknown";
        }
    }

    public static string normalizeRuntime()
    {
        // dotnet version
        string desc = RuntimeInformation.FrameworkDescription ?? "Unknown CLR";

        // 2 runtime check
        if (Type.GetType("Mono.Runtime") != null)
            return "Mono";

        if (Type.GetType("UnityEngine.Application") != null)
            return "Unity (IL2CPP or MonoBackend)";

        if (desc.Contains(".NET Framework", StringComparison.OrdinalIgnoreCase))
            return "CLR (.NET Framework)";

        if (desc.Contains(".NET Core", StringComparison.OrdinalIgnoreCase))
            return "CoreCLR (.NET Core)";

        if (desc.Contains(".NET", StringComparison.OrdinalIgnoreCase))
            return "CoreCLR (.NET 5/6/7/8/9+)";

        return desc; // fallback (NativeAOT, Wasm 등)
    }

    private static string normalizeOs(string value) {
        value = normalize(value);
        if (value.StartsWith("aix")) {
            return "aix";
        }
        if (value.StartsWith("hpux")) {
            return "hpux";
        }
        if (value.StartsWith("os400")) {
            // Avoid the names such as os4000
            if (value.Length <= 5 || !Char.IsDigit(value[5])) {
                return "os400";
            }
        }
        if (value.StartsWith("linux")) {
            return "linux";
        }
        if (value.StartsWith("macosx") || value.StartsWith("osx") || value.StartsWith("darwin")) {
            return "osx";
        }
        if (value.StartsWith("freebsd")) {
            return "freebsd";
        }
        if (value.StartsWith("openbsd")) {
            return "openbsd";
        }
        if (value.StartsWith("netbsd")) {
            return "netbsd";
        }
        if (value.StartsWith("solaris") || value.StartsWith("sunos")) {
            return "sunos";
        }
        if (value.StartsWith("windows")) {
            return "windows";
        }

        return "unknown";
    }

    /**
     * Check if JFR events are supported on this platform.
     */
    public static bool isJfrEnabled() {
        return JFR;
    }

}
