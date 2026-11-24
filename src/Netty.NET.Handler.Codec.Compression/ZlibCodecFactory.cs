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
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Handler.Codec.Compression;

/**
 * Creates a new {@link ZlibEncoder} and a new {@link ZlibDecoder}.
 */
// CLR: the final class is a sealed class with the private constructor. The static initializer is the static
// constructor; SystemPropertyUtil and InternalLoggerFactory are the Common ports. JZlibEncoder and JZlibDecoder need
// com.jcraft.jzlib, which has no BCL counterpart, and are not ported (owner decision 2026-11-23, library-not-ported).
// So the Class.forName probe always takes its ClassNotFoundException branch: JZLIB_AVAILABLE is false (Java's value
// without jzlib on the class path) and isSupportingWindowSizeAndMemLevel() returns false. The branches that create a
// JZlib codec (io.netty.noJdkZlibEncoder/io.netty.noJdkZlibDecoder set, or a window size and memory level other than 15
// and 8) throw NotSupportedException, where the JVM without jzlib fails with NoClassDefFoundError; the other branches
// create the Jdk codecs as in Java. The @Deprecated newZlibDecoder(), newZlibDecoder(ZlibWrapper) and
// newZlibDecoder(byte[]) name their replacements and are not ported (owner decision 2026-11-16); their comments are
// recorded in the manifest.
public sealed class ZlibCodecFactory
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(ZlibCodecFactory));

    private const int DEFAULT_JDK_WINDOW_SIZE = 15;
    private const int DEFAULT_JDK_MEM_LEVEL = 8;

    private static readonly bool noJdkZlibDecoder;
    private static readonly bool noJdkZlibEncoder;

    private static readonly bool JZLIB_AVAILABLE;

    static ZlibCodecFactory()
    {
        noJdkZlibDecoder = SystemPropertyUtil.GetBoolean("io.netty.noJdkZlibDecoder", false);
        logger.Debug("-Dio.netty.noJdkZlibDecoder: {}", noJdkZlibDecoder);

        noJdkZlibEncoder = SystemPropertyUtil.GetBoolean("io.netty.noJdkZlibEncoder", false);
        logger.Debug("-Dio.netty.noJdkZlibEncoder: {}", noJdkZlibEncoder);

        // CLR: com.jcraft.jzlib is not ported, so this is the ClassNotFoundException branch of the Java probe.
        bool jzlibAvailable = false;
        logger.Debug(
            "JZlib not in the classpath; the only window bits supported value will be " +
                DEFAULT_JDK_WINDOW_SIZE.ToString(System.Globalization.CultureInfo.InvariantCulture));
        JZLIB_AVAILABLE = jzlibAvailable;
    }

    /**
     * Returns {@code true} if specify a custom window size and mem level is supported.
     */
    public static bool IsSupportingWindowSizeAndMemLevel()
    {
        return JZLIB_AVAILABLE;
    }

    public static ZlibEncoder NewZlibEncoder(int compressionLevel)
    {
        if (noJdkZlibEncoder)
        {
            throw JZlibNotPorted("JZlibEncoder");
        }
        else
        {
            return new JdkZlibEncoder(compressionLevel);
        }
    }

    public static ZlibEncoder NewZlibEncoder(ZlibWrapper wrapper)
    {
        if (noJdkZlibEncoder)
        {
            throw JZlibNotPorted("JZlibEncoder");
        }
        else
        {
            return new JdkZlibEncoder(wrapper);
        }
    }

    public static ZlibEncoder NewZlibEncoder(ZlibWrapper wrapper, int compressionLevel)
    {
        if (noJdkZlibEncoder)
        {
            throw JZlibNotPorted("JZlibEncoder");
        }
        else
        {
            return new JdkZlibEncoder(wrapper, compressionLevel);
        }
    }

    public static ZlibEncoder NewZlibEncoder(ZlibWrapper wrapper, int compressionLevel, int windowBits, int memLevel)
    {
        if (noJdkZlibEncoder ||
                windowBits != DEFAULT_JDK_WINDOW_SIZE || memLevel != DEFAULT_JDK_MEM_LEVEL)
        {
            throw JZlibNotPorted("JZlibEncoder");
        }
        else
        {
            return new JdkZlibEncoder(wrapper, compressionLevel);
        }
    }

    public static ZlibEncoder NewZlibEncoder(byte[] dictionary)
    {
        if (noJdkZlibEncoder)
        {
            throw JZlibNotPorted("JZlibEncoder");
        }
        else
        {
            return new JdkZlibEncoder(dictionary);
        }
    }

    public static ZlibEncoder NewZlibEncoder(int compressionLevel, byte[] dictionary)
    {
        if (noJdkZlibEncoder)
        {
            throw JZlibNotPorted("JZlibEncoder");
        }
        else
        {
            return new JdkZlibEncoder(compressionLevel, dictionary);
        }
    }

    public static ZlibEncoder NewZlibEncoder(int compressionLevel, int windowBits, int memLevel, byte[] dictionary)
    {
        if (noJdkZlibEncoder ||
                windowBits != DEFAULT_JDK_WINDOW_SIZE || memLevel != DEFAULT_JDK_MEM_LEVEL)
        {
            throw JZlibNotPorted("JZlibEncoder");
        }
        else
        {
            return new JdkZlibEncoder(compressionLevel, dictionary);
        }
    }

    /**
     * Create a new decoder instance with specified maximum buffer allocation.
     *
     * @param maxAllocation
     *           Maximum size of the decompression buffer. Must be &gt;= 0.
     *           If zero, maximum size is not limited by decoder.
     */
    public static ZlibDecoder NewZlibDecoder(int maxAllocation)
    {
        if (noJdkZlibDecoder)
        {
            throw JZlibNotPorted("JZlibDecoder");
        }
        else
        {
            return new JdkZlibDecoder(true, maxAllocation);
        }
    }

    /**
     * Create a new decoder instance with the specified wrapper and maximum buffer allocation.
     *
     * @param maxAllocation
     *          Maximum size of the decompression buffer. Must be &gt;= 0.
     *          If zero, maximum size is not limited by decoder.
     */
    public static ZlibDecoder NewZlibDecoder(ZlibWrapper wrapper, int maxAllocation)
    {
        if (noJdkZlibDecoder)
        {
            throw JZlibNotPorted("JZlibDecoder");
        }
        else
        {
            return new JdkZlibDecoder(wrapper, true, maxAllocation);
        }
    }

    /**
     * Create a new decoder instance with the specified preset dictionary and maximum buffer allocation.
     * The wrapper is always {@link ZlibWrapper#ZLIB} because it is the only format that
     * supports the preset dictionary.
     *
     * @param maxAllocation
     *          Maximum size of the decompression buffer. Must be &gt;= 0.
     *          If zero, maximum size is not limited by decoder.
     */
    public static ZlibDecoder NewZlibDecoder(byte[] dictionary, int maxAllocation)
    {
        if (noJdkZlibDecoder)
        {
            throw JZlibNotPorted("JZlibDecoder");
        }
        else
        {
            return new JdkZlibDecoder(dictionary, maxAllocation);
        }
    }

    // CLR: the exception of a branch that would create a JZlib codec (see the class comment).
    private static NotSupportedException JZlibNotPorted(string type)
    {
        return new NotSupportedException(type + " needs com.jcraft.jzlib, which Netty.NET does not port");
    }

    private ZlibCodecFactory()
    {
        // Unused
    }
}
