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
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

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
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(PlatformDependent));

    // constants borrowed from murmur3
    private const int HASH_CODE_ASCII_SEED = unchecked((int)0xc2b2ae35);
    private const int HASH_CODE_C1 = unchecked((int)0xcc9e2d51);
    private const int HASH_CODE_C2 = unchecked((int)0x1b873593);
    private static readonly bool IS_ANDROID = IsAndroid0();

    private static readonly bool CAN_ENABLE_TCP_NODELAY_BY_DEFAULT = !IsAndroid();

    // CLR identity cannot be overridden by JVM system-property environment keys.
    // Initialize the OS flags before temporary-directory fallback uses them.
    private static readonly bool IS_WINDOWS = OperatingSystem.IsWindows();
    private static readonly bool IS_OSX = OperatingSystem.IsMacOS();
    private static readonly DirectoryInfo TMPDIR = Tmpdir0();
    private static readonly string NORMALIZED_ARCH = NormalizeArch(RuntimeInformation.ProcessArchitecture.ToString());
    private static readonly string NORMALIZED_OS = NormalizeOs(
        OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "Mac OS X" :
        OperatingSystem.IsLinux() || OperatingSystem.IsAndroid() ? "Linux" :
        OperatingSystem.IsFreeBSD() ? "FreeBSD" : RuntimeInformation.OSDescription);
    // Classifier preferences belong to this cache; malformed settings must not
    // disable unrelated OS, hashing or memory operations during type initialization.
    private static readonly Lazy<IReadOnlyList<string>> LINUX_OS_CLASSIFIERS =
        new(CreateLinuxClassifiers, LazyThreadSafetyMode.ExecutionAndPublication);
    private const string LINUX_ID_PREFIX = "ID=";
    private const string LINUX_ID_LIKE_PREFIX = "ID_LIKE=";
    public static readonly bool BIG_ENDIAN_NATIVE_ORDER = ByteOrder.NativeOrder() == ByteOrder.BIG_ENDIAN;

    // For specifications, see https://www.freedesktop.org/software/systemd/man/os-release.html
    public static void AddFilesystemOsClassifiers(ICollection<string> availableClassifiers) {
        if (ProcessOsReleaseFile("/etc/os-release", availableClassifiers)) {
            return;
        }
        ProcessOsReleaseFile("/usr/lib/os-release", availableClassifiers);
    }

    private static bool ProcessOsReleaseFile(string osReleaseFileName, ICollection<string> availableClassifiers)
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
                        if (line.StartsWith(LINUX_ID_PREFIX, StringComparison.Ordinal))
                        {
                            string id = NormalizeOsReleaseVariableValue(line[LINUX_ID_PREFIX.Length..]);
                            AddClassifier(availableClassifiers, id);
                        }
                        else if (line.StartsWith(LINUX_ID_LIKE_PREFIX, StringComparison.Ordinal))
                        {
                            line = NormalizeOsReleaseVariableValue(line[LINUX_ID_LIKE_PREFIX.Length..]);
                            AddClassifier(availableClassifiers, line.Split(" "));
                        }
                    }
                } catch (SecurityException e) {
                    logger.Debug("Unable to read {}", osReleaseFileName, e);
                } catch (IOException e) {
                    logger.Debug("Error while reading content of {}", osReleaseFileName, e);
                } catch (UnauthorizedAccessException e) {
                    // Java AccessDeniedException is an IOException; CLR access denial is separate.
                    logger.Debug("Unable to read {}", osReleaseFileName, e);
                }
                // specification states we should only fall back if /etc/os-release does not exist
                return true;
            }

        } catch (SecurityException e) {
            logger.Debug("Unable to check if {} exists", osReleaseFileName, e);
        }
        return false;
    }

    public static bool AddPropertyOsClassifiers(ICollection<string> availableClassifiers) {
        // empty: -Dio.netty.osClassifiers (no distro specific classifiers for native libs)
        // single ID: -Dio.netty.osClassifiers=ubuntu
        // pair ID, ID_LIKE: -Dio.netty.osClassifiers=ubuntu,debian
        // illegal otherwise
        string osClassifiersPropertyName = "io.netty.osClassifiers";
        string osClassifiers = SystemPropertyUtil.Get(osClassifiersPropertyName);
        if (osClassifiers == null) {
            return false;
        }
        if (osClassifiers.IsEmpty()) {
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
            AddClassifier(availableClassifiers, classifier);
        }
        return true;
    }

    /**
     * Returns {@code true} if and only if the current platform is Android
     */
    public static bool IsAndroid() {
        return IS_ANDROID;
    }

    /**
     * Return {@code true} if the JVM is running on Windows
     */
    public static bool IsWindows() {
        return IS_WINDOWS;
    }

    /**
     * Return {@code true} if the JVM is running on OSX / MacOS
     */
    public static bool IsOsx() {
        return IS_OSX;
    }

    /**
     * Returns {@code true} if and only if it is fine to enable TCP_NODELAY socket option by default.
     */
    public static bool CanEnableTcpNoDelayByDefault() {
        return CAN_ENABLE_TCP_NODELAY_BY_DEFAULT;
    }

    /**
     * Returns the temporary directory.
     */
    public static DirectoryInfo Tmpdir() {
        return TMPDIR;
    }

    /**
     * Creates a new fastest {@link ConcurrentDictionary} implementation for the current platform.
     * @deprecated please use new ConcurrentDictionary<K, V>() directly.
     */
    [Obsolete]
    public static ConcurrentDictionary<K, V> NewConcurrentHashMap<K, V>() {
        return new ConcurrentDictionary<K, V>();
    }

    /**
     * Creates a new fastest {@link ConcurrentDictionary} implementation for the current platform.
     * @deprecated please use new ConcurrentDictionary<K, V>() directly.
     */
    [Obsolete]
    public static ConcurrentDictionary<K, V> NewConcurrentHashMap<K, V>(int initialCapacity) {
        return new ConcurrentDictionary<K, V>();
    }

    /**
     * Creates a new fastest {@link ConcurrentDictionary} implementation for the current platform.
     * @deprecated please use new ConcurrentDictionary<K, V>() directly.
     */
    [Obsolete]
    public static ConcurrentDictionary<K, V> NewConcurrentHashMap<K, V>(int initialCapacity, float loadFactor) {
        return new ConcurrentDictionary<K, V>();
    }

    /**
     * Creates a new fastest {@link ConcurrentDictionary} implementation for the current platform.
     * @deprecated please use new ConcurrentDictionary<K, V>() directly.
     */
    [Obsolete]
    public static ConcurrentDictionary<K, V> NewConcurrentHashMap<K, V>(
            int initialCapacity, float loadFactor, int concurrencyLevel)
    {
        return new ConcurrentDictionary<K, V>();
    }

    /**
     * Creates a new fastest {@link ConcurrentDictionary} implementation for the current platform.
     * @deprecated please use new ConcurrentDictionary<K, V>() directly.
     */
    [Obsolete]
    public static ConcurrentDictionary<K, V> NewConcurrentHashMap<K, V>(IDictionary<K, V> map) {
        return new ConcurrentDictionary<K, V>(map);
    }

    /**
     * Identical to {@link PlatformDependent0#hashCodeAsciiCompute(long, int)} but for {@link CharSequence}.
     */
    private static int HashCodeAsciiCompute(ICharSequence value, int offset, int hash) {
        if (BIG_ENDIAN_NATIVE_ORDER) {
            return unchecked(hash * HASH_CODE_C1 +
                    // Low order int
                    HashCodeAsciiSanitizeInt(value, offset + 4) * HASH_CODE_C2 +
                    // High order int
                    HashCodeAsciiSanitizeInt(value, offset));
        }
        return unchecked(hash * HASH_CODE_C1 +
                // Low order int
                HashCodeAsciiSanitizeInt(value, offset) * HASH_CODE_C2 +
                // High order int
                HashCodeAsciiSanitizeInt(value, offset + 4));
    }

    /**
     * Identical to {@link PlatformDependent0#hashCodeAsciiSanitize(int)} but for {@link CharSequence}.
     */
    private static int HashCodeAsciiSanitizeInt(ICharSequence value, int offset) {
        if (BIG_ENDIAN_NATIVE_ORDER) {
            // mimic a unsafe.getInt call on a big endian machine
            return (value.CharAt(offset + 3) & 0x1f) |
                   (value.CharAt(offset + 2) & 0x1f) << 8 |
                   (value.CharAt(offset + 1) & 0x1f) << 16 |
                   (value.CharAt(offset) & 0x1f) << 24;
        }
        return (value.CharAt(offset + 3) & 0x1f) << 24 |
               (value.CharAt(offset + 2) & 0x1f) << 16 |
               (value.CharAt(offset + 1) & 0x1f) << 8 |
               (value.CharAt(offset) & 0x1f);
    }

    /**
     * Identical to {@link PlatformDependent0#hashCodeAsciiSanitize(short)} but for {@link CharSequence}.
     */
    private static int HashCodeAsciiSanitizeShort(ICharSequence value, int offset) {
        if (BIG_ENDIAN_NATIVE_ORDER) {
            // mimic a unsafe.getShort call on a big endian machine
            return (value.CharAt(offset + 1) & 0x1f) |
                    (value.CharAt(offset) & 0x1f) << 8;
        }
        return (value.CharAt(offset + 1) & 0x1f) << 8 |
                (value.CharAt(offset) & 0x1f);
    }

    /**
     * Identical to {@link PlatformDependent0#hashCodeAsciiSanitize(byte)} but for {@link CharSequence}.
     */
    private static int HashCodeAsciiSanitizeByte(char value) {
        return value & 0x1f;
    }

    public static void CopyMemory(byte[] src, int srcIndex, byte[] dst, int dstIndex, long length) {
        ArgumentNullException.ThrowIfNull(src);
        ArgumentNullException.ThrowIfNull(dst);
        int count = checked((int)length);
        // CLR adaptation: bounded CopyTo also supports overlapping ranges. No
        // JVM object-header offset, native pin or manual safe-point loop is needed.
        src.AsSpan(srcIndex, count).CopyTo(dst.AsSpan(dstIndex, count));
    }

    public static void SetMemory(byte[] dst, int dstIndex, long bytes, byte value) {
        ArgumentNullException.ThrowIfNull(dst);
        dst.AsSpan(dstIndex, checked((int)bytes)).Fill(value);
    }

    public static long Align(long value, int alignment) {
        return Pow2.Align(value, alignment);
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
    public static bool Equals(byte[] bytes1, int startPos1, byte[] bytes2, int startPos2, int length) {
        ArgumentNullException.ThrowIfNull(bytes1);
        ArgumentNullException.ThrowIfNull(bytes2);
        // Keep the existing empty result for nonpositive comparison lengths.
        if (length <= 0) return true;
        return bytes1.AsSpan(startPos1, length).SequenceEqual(bytes2.AsSpan(startPos2, length));
    }

    /**
     * Determine if a subsection of an array is zero.
     * @param bytes The byte array.
     * @param startPos The starting index (inclusive) in {@code bytes}.
     * @param length The amount of bytes to check for zero.
     * @return {@code false} if {@code bytes[startPos:startsPos+length)} contains a value other than zero.
     */
    public static bool IsZero(byte[] bytes, int startPos, int length) {
        ArgumentNullException.ThrowIfNull(bytes);
        if (length <= 0) return true;
        return bytes.AsSpan(startPos, length).IndexOfAnyExcept((byte)0) < 0;
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
    public static int EqualsConstantTime(byte[] bytes1, int startPos1, byte[] bytes2, int startPos2, int length) {
        // CLR bounded fixed-time comparison replaces both JVM Unsafe branches.
        return ConstantTimeUtils.EqualsConstantTime(bytes1, startPos1, bytes2, startPos2, length);
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
    public static int HashCodeAscii(byte[] bytes, int startPos, int length) {
        ArgumentNullException.ThrowIfNull(bytes);
        ReadOnlySpan<byte> data = bytes.AsSpan(startPos, length);
        // Native-order words preserve Netty's low-five-bit hash contract. The
        // complete range is validated before any word or tail is inspected.
        int hash = HASH_CODE_ASCII_SEED;
        int remainingBytes = length & 7;
        int end = remainingBytes;
        for (int i = length - 8; i >= end; i -= 8) {
            hash = HashCodeAsciiCompute(MemoryMarshal.Read<long>(data.Slice(i)), hash);
        }
        switch(remainingBytes) {
        case 7:
            return unchecked(((hash * HASH_CODE_C1 + HashCodeAsciiSanitize(data[0]))
                          * HASH_CODE_C2 + HashCodeAsciiSanitize(MemoryMarshal.Read<short>(data.Slice(1))))
                          * HASH_CODE_C1 + HashCodeAsciiSanitize(MemoryMarshal.Read<int>(data.Slice(3))));
        case 6:
            return unchecked((hash * HASH_CODE_C1 + HashCodeAsciiSanitize(MemoryMarshal.Read<short>(data.Slice(0))))
                         * HASH_CODE_C2 + HashCodeAsciiSanitize(MemoryMarshal.Read<int>(data.Slice(2))));
        case 5:
            return unchecked((hash * HASH_CODE_C1 + HashCodeAsciiSanitize(data[0]))
                         * HASH_CODE_C2 + HashCodeAsciiSanitize(MemoryMarshal.Read<int>(data.Slice(1))));
        case 4:
            return unchecked(hash * HASH_CODE_C1 + HashCodeAsciiSanitize(MemoryMarshal.Read<int>(data.Slice(0))));
        case 3:
            return unchecked((hash * HASH_CODE_C1 + HashCodeAsciiSanitize(data[0]))
                         * HASH_CODE_C2 + HashCodeAsciiSanitize(MemoryMarshal.Read<short>(data.Slice(1))));
        case 2:
            return unchecked(hash * HASH_CODE_C1 + HashCodeAsciiSanitize(MemoryMarshal.Read<short>(data.Slice(0))));
        case 1:
            return unchecked(hash * HASH_CODE_C1 + HashCodeAsciiSanitize(data[0]));
        default:
            return hash;
        }
    }

    public static int HashCodeAscii(string bytes)
    {
        var scs = new StringCharSequence(bytes);
        return HashCodeAscii(scs);
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
    public static int HashCodeAscii(ICharSequence bytes)
    {
        int length = bytes.Length();
        int remainingBytes = length & 7;
        int hash = HASH_CODE_ASCII_SEED;
        // Benchmarking shows that by just naively looping for inputs 8~31 bytes long we incur a relatively large
        // performance penalty (only achieve about 60% performance of loop which iterates over each char). So because
        // of this we take special provisions to unroll the looping for these conditions.
        if (length >= 32) {
            for (int i = length - 8; i >= remainingBytes; i -= 8) {
                hash = HashCodeAsciiCompute(bytes, i, hash);
            }
        } else if (length >= 8) {
            hash = HashCodeAsciiCompute(bytes, length - 8, hash);
            if (length >= 16) {
                hash = HashCodeAsciiCompute(bytes, length - 16, hash);
                if (length >= 24) {
                    hash = HashCodeAsciiCompute(bytes, length - 24, hash);
                }
            }
        }
        if (remainingBytes == 0) {
            return hash;
        }
        int offset = 0;
        if (remainingBytes != 2 & remainingBytes != 4 & remainingBytes != 6) { // 1, 3, 5, 7
            hash = unchecked(hash * HASH_CODE_C1 + HashCodeAsciiSanitizeByte(bytes.CharAt(0)));
            offset = 1;
        }
        if (remainingBytes != 1 & remainingBytes != 4 & remainingBytes != 5) { // 2, 3, 6, 7
            hash = unchecked(hash * (offset == 0 ? HASH_CODE_C1 : HASH_CODE_C2)
                    + HashCodeAsciiSanitize(HashCodeAsciiSanitizeShort(bytes, offset)));
            offset += 2;
        }
        if (remainingBytes >= 4) { // 4, 5, 6, 7
            return unchecked(hash * ((offset == 0 | offset == 3) ? HASH_CODE_C1 : HASH_CODE_C2)
                    + HashCodeAsciiSanitizeInt(bytes, offset));
        }
        return hash;
    }

    /**
     * Return a {@link Random} which is not-threadsafe and so can only be used from the same thread.
     * @deprecated Use ThreadLocalRandom.current() instead.
     */
    [Obsolete]
    public static Random ThreadLocalRandom() {
        return global::Netty.NET.Common.Internal.ThreadLocalRandom.Current();
    }

    private static DirectoryInfo Tmpdir0() {
        DirectoryInfo f;
        try {
            f = ToDirectory(SystemPropertyUtil.Get("io.netty.tmpdir"));
            if (f != null) {
                logger.Debug("-Dio.netty.tmpdir: {}", f);
                return f;
            }

            f = ToDirectory(SystemPropertyUtil.Get("java.io.tmpdir", Path.GetTempPath()));
            if (f != null) {
                logger.Debug("-Dio.netty.tmpdir: {} (java.io.tmpdir)", f);
                return f;
            }

            // This shouldn't happen, but just in case ..
            if (IsWindows()) {
                f = ToDirectory(Environment.GetEnvironmentVariable("TEMP"));
                if (f != null) {
                    logger.Debug("-Dio.netty.tmpdir: {} (%TEMP%)", f);
                    return f;
                }

                string userprofile = Environment.GetEnvironmentVariable("USERPROFILE");
                if (userprofile != null) {
                    f = ToDirectory(userprofile + "\\AppData\\Local\\Temp");
                    if (f != null) {
                        logger.Debug("-Dio.netty.tmpdir: {} (%USERPROFILE%\\AppData\\Local\\Temp)", f);
                        return f;
                    }

                    f = ToDirectory(userprofile + "\\Local Settings\\Temp");
                    if (f != null) {
                        logger.Debug("-Dio.netty.tmpdir: {} (%USERPROFILE%\\Local Settings\\Temp)", f);
                        return f;
                    }
                }
            } else {
                f = ToDirectory(Environment.GetEnvironmentVariable("TMPDIR"));
                if (f != null) {
                    logger.Debug("-Dio.netty.tmpdir: {} ($TMPDIR)", f);
                    return f;
                }
            }
        } catch (Exception ignored) {
            // Environment variable inaccessible
        }

        // Last resort.
        if (IsWindows()) {
            f = new DirectoryInfo("C:\\Windows\\Temp");
        } else {
            f = new DirectoryInfo("/tmp");
        }

        logger.Warn("Failed to get the temporary directory; falling back to: {}", f);
        return f;
    }

    //@SuppressWarnings("ResultOfMethodCallIgnored")
    private static DirectoryInfo ToDirectory(string path) {
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

    public static string NormalizedArch() {
        return NORMALIZED_ARCH;
    }

    public static string NormalizedOs() {
        return NORMALIZED_OS;
    }

    public static IReadOnlyList<string> NormalizedLinuxClassifiers() {
        return LINUX_OS_CLASSIFIERS.Value;
    }

    private static IReadOnlyList<string> CreateLinuxClassifiers()
    {
        // Native library candidates must retain ID/ID_LIKE priority, unique entries
        // and an immutable cached result. No mutable builder escapes publication.
        var available = new List<string>(3);
        if (!AddPropertyOsClassifiers(available)) AddFilesystemOsClassifiers(available);
        return Array.AsReadOnly(available.ToArray());
    }

    /// <summary>
    /// Creates an empty file in the supplied directory, or the CLR temporary directory.
    /// Null prefix means no prefix; null suffix means .tmp. The caller owns deletion.
    /// </summary>
    public static FileInfo CreateTempFile(string prefix, string suffix, DirectoryInfo directory) {
        prefix ??= string.Empty;
        suffix ??= ".tmp";
        char[] invalidNameChars = Path.GetInvalidFileNameChars();
        if (prefix.IndexOfAny(invalidNameChars) >= 0)
            throw new ArgumentException("A temporary-file prefix must be a filename component.", nameof(prefix));
        if (suffix.IndexOfAny(invalidNameChars) >= 0)
            throw new ArgumentException("A temporary-file suffix must be a filename component.", nameof(suffix));

        string dirPath = directory?.FullName ?? Path.GetTempPath();
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        for (int attempt = 0; ; ++attempt) {
            string filePath = Path.Combine(dirPath, prefix + Path.GetRandomFileName() + suffix);
            FileStream stream;
            try {
                // CreateNew reserves the name atomically; never truncate or follow an existing file/link.
                stream = new FileStream(filePath, options);
            } catch (IOException error) when (attempt < 100 &&
                (OperatingSystem.IsWindows()
                    ? error.HResult == unchecked((int)0x80070050) || error.HResult == unchecked((int)0x800700b7)
                    : error.HResult == 17)) {
                // CLR maps Windows FILE_EXISTS/ALREADY_EXISTS and Unix EEXIST differently.
                // Retry only a name collision, with a bound as in the CLR Windows temp-file API.
                continue;
            }
            stream.Dispose();
            return new FileInfo(filePath);
        }
    }

    /**
     * Adds only those classifier strings to <tt>dest</tt> which are present in <tt>allowed</tt>.
     *
     * @param dest             destination set
     * @param maybeClassifiers potential classifiers to add
     */
    private static void AddClassifier(ICollection<string> dest, params string[] maybeClassifiers) {
        foreach (string id in maybeClassifiers) {
            if (IsAllowedClassifier(id) && !dest.Contains(id)) {
                dest.Add(id);
            }
        }
    }
    // keep in sync with maven's pom.xml via os.detection.classifierWithLikes!
    private static bool IsAllowedClassifier(string classifier) {
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
    private static string NormalizeOsReleaseVariableValue(string value) {
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
    private static string Normalize(string value) {
        StringBuilder sb = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++) {
            char c = char.ToLowerInvariant(value[i]);
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static string NormalizeArch(string value) {
        value = Normalize(value);
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
            // CLR ProcessArchitecture distinguishes ARMv6; its native artifact is ARM32.
            case "armv6":
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

    public static string NormalizeRuntime()
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

    private static string NormalizeOs(string value) {
        value = Normalize(value);
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

    private static int HashCodeAsciiCompute(long value, int hash)
    {
        // CLR adaptation: this operation is pure integer arithmetic and needs
        // neither JVM Unsafe nor a native-memory implementation. Java's int
        // arithmetic wraps, including when checked CLR callers are enabled.
        // masking with 0x1f reduces the number of overall bits that impact the hash code but makes the hash
        // code the same regardless of character case (upper case or lower case hash is the same).
        return unchecked(hash * HASH_CODE_C1 +
                // Low order int
                HashCodeAsciiSanitize((int)value) * HASH_CODE_C2 +
                // High order int
                (int)((value & 0x1f1f1f1f00000000L) >>> 32));
    }

    private static int HashCodeAsciiSanitize(int value)
    {
        return value & 0x1f1f1f1f;
    }

    private static int HashCodeAsciiSanitize(short value)
    {
        return value & 0x1f1f;
    }

    private static int HashCodeAsciiSanitize(byte value)
    {
        return value & 0x1f;
    }

    private static bool IsAndroid0()
    {
        // Idea: Sometimes java binaries include Android classes on the classpath, even if it isn't actually Android.
        // Rather than check if certain classes are present, just check the VM, which is tied to the JDK.

        // Optional improvement: check if `android.os.Build.VERSION` is >= 24. On later versions of Android, the
        // OpenJDK is used, which means `Unsafe` will actually work as expected.

        // Android sets this property to Dalvik, regardless of whether it actually is.
        // CLR adaptation: detect the OS directly, independent of JVM properties.
        bool isAndroid = OperatingSystem.IsAndroid();
        if (isAndroid)
        {
            logger.Debug("Platform: Android");
        }

        return isAndroid;
    }
}
