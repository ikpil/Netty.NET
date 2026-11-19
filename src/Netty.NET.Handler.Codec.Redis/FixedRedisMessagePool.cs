/*
 * Copyright 2016 The Netty Project
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
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Redis;

/**
 * A default fixed redis message pool.
 */
// CLR: Map/HashMap(initialCapacity, 1.0f) is Dictionary(capacity), whose lookups return null for a missing key through
// GetValueOrDefault as HashMap.get does. HashMap "permits null values and the null key"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/HashMap.html) while Dictionary throws
// ArgumentNullException for a null key (https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.dictionary-2.trygetvalue),
// so the lookups by string or ByteBuf return null for a null key. ByteBuf keys use the content-based ByteBuf
// Equals/GetHashCode, as in Java. LongObjectMap/LongObjectHashMap (io.netty.util.collection; owner decision
// 2026-11-19 keeps Dictionary, docs/common-porting-manifest.json) is
// Dictionary<long, V>: the primitive maps exist because "Type arguments may be either reference types or wildcards"
// (JLS 4.5.1: https://docs.oracle.com/javase/specs/jls/se17/html/jls-4.html#jls-4.5.1), while the CLR creates a
// specialized generic type for a value type argument, so Dictionary<long, V> stores long keys without boxing
// (https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/generics/generics-in-the-run-time).
// ByteBuf.array() has no C# ByteBuf counterpart (docs/codec-base-porting-manifest.json, ReplayingDecoderByteBuf), so
// new String(buffer.array()) decodes the same wrapped bytes with ToString(Encoding.Default), the default charset as in
// codec-base (Charset.defaultCharset() is Encoding.Default). RedisReplyKey.name() is the C# enum name from ToString()
// (https://learn.microsoft.com/en-us/dotnet/api/system.enum.tostring) and RedisReplyKey.values() is
// Enum.GetValues<RedisReplyKey>(), which is sorted by value and so in declaration order
// (https://learn.microsoft.com/en-us/dotnet/api/system.enum.getvalues).
[UnstableApi]
public sealed class FixedRedisMessagePool : IRedisMessagePool
{

    public enum RedisReplyKey
    {
        OK, PONG, QUEUED
    }

    // CLR: the Java enum with a constructor, a field and toString() is a sealed class with static readonly instances
    // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/enum,
    // https://docs.oracle.com/javase/specs/jls/se17/html/jls-8.html#jls-8.9); Values() returns the constants in
    // declaration order as the implicit Java values() does.
    public sealed class RedisErrorKey
    {
        public static readonly RedisErrorKey ERR = new RedisErrorKey("ERR");
        public static readonly RedisErrorKey ERR_IDX = new RedisErrorKey("ERR index out of range");
        public static readonly RedisErrorKey ERR_NOKEY = new RedisErrorKey("ERR no such key");
        public static readonly RedisErrorKey ERR_SAMEOBJ = new RedisErrorKey("ERR source and destination objects are the same");
        public static readonly RedisErrorKey ERR_SYNTAX = new RedisErrorKey("ERR syntax error");
        public static readonly RedisErrorKey BUSY = new RedisErrorKey("BUSY Redis is busy running a script. You can only call SCRIPT KILL or SHUTDOWN NOSAVE.");
        public static readonly RedisErrorKey BUSYKEY = new RedisErrorKey("BUSYKEY Target key name already exists.");
        public static readonly RedisErrorKey EXECABORT = new RedisErrorKey("EXECABORT Transaction discarded because of previous errors.");
        public static readonly RedisErrorKey LOADING = new RedisErrorKey("LOADING Redis is loading the dataset in memory");
        public static readonly RedisErrorKey MASTERDOWN = new RedisErrorKey("MASTERDOWN Link with MASTER is down and slave-serve-stale-data is set to 'no'.");
        public static readonly RedisErrorKey MISCONF = new RedisErrorKey("MISCONF Redis is configured to save RDB snapshots, but is currently not able to persist on disk. " +
            "Commands that may modify the data set are disabled. Please check Redis logs for details " +
            "about the error.");
        public static readonly RedisErrorKey NOREPLICAS = new RedisErrorKey("NOREPLICAS Not enough good slaves to write.");
        public static readonly RedisErrorKey NOSCRIPT = new RedisErrorKey("NOSCRIPT No matching script. Please use EVAL.");
        public static readonly RedisErrorKey OOM = new RedisErrorKey("OOM command not allowed when used memory > 'maxmemory'.");
        public static readonly RedisErrorKey READONLY = new RedisErrorKey("READONLY You can't write against a read only slave.");
        public static readonly RedisErrorKey WRONGTYPE = new RedisErrorKey("WRONGTYPE Operation against a key holding the wrong kind of value");
        public static readonly RedisErrorKey NOT_AUTH = new RedisErrorKey("NOAUTH Authentication required.");

        private static readonly RedisErrorKey[] VALUES = {
            ERR, ERR_IDX, ERR_NOKEY, ERR_SAMEOBJ, ERR_SYNTAX, BUSY, BUSYKEY, EXECABORT, LOADING, MASTERDOWN, MISCONF,
            NOREPLICAS, NOSCRIPT, OOM, READONLY, WRONGTYPE, NOT_AUTH,
        };

        private readonly string msg;

        private RedisErrorKey(string msg)
        {
            this.msg = msg;
        }

        public static RedisErrorKey[] Values()
        {
            return (RedisErrorKey[]) VALUES.Clone();
        }

        public override string ToString()
        {
            return msg;
        }
    }

    private const long MIN_CACHED_INTEGER_NUMBER = RedisConstants.NULL_VALUE; // inclusive
    private const long MAX_CACHED_INTEGER_NUMBER = 128; // exclusive

    // cached integer size cannot larger than `int` range because of Collection.
    private const int SIZE_CACHED_INTEGER_NUMBER = (int) (MAX_CACHED_INTEGER_NUMBER - MIN_CACHED_INTEGER_NUMBER);

    /**
     * A shared object for {@link FixedRedisMessagePool}.
     */
    public static readonly FixedRedisMessagePool INSTANCE = new FixedRedisMessagePool();

    // internal caches.
    private readonly Dictionary<ByteBuf, SimpleStringRedisMessage> byteBufToSimpleStrings;
    private readonly Dictionary<string, SimpleStringRedisMessage> stringToSimpleStrings;
    private readonly Dictionary<RedisReplyKey, SimpleStringRedisMessage> keyToSimpleStrings;
    private readonly Dictionary<ByteBuf, ErrorRedisMessage> byteBufToErrors;
    private readonly Dictionary<string, ErrorRedisMessage> stringToErrors;
    private readonly Dictionary<RedisErrorKey, ErrorRedisMessage> keyToErrors;
    private readonly Dictionary<ByteBuf, IntegerRedisMessage> byteBufToIntegers;
    private readonly Dictionary<long, IntegerRedisMessage> longToIntegers;
    private readonly Dictionary<long, byte[]> longToByteBufs;

    /**
     * Creates a {@link FixedRedisMessagePool} instance.
     */
    private FixedRedisMessagePool()
    {
        RedisReplyKey[] replyKeys = Enum.GetValues<RedisReplyKey>();
        keyToSimpleStrings = new Dictionary<RedisReplyKey, SimpleStringRedisMessage>(replyKeys.Length);
        stringToSimpleStrings = new Dictionary<string, SimpleStringRedisMessage>(replyKeys.Length);
        byteBufToSimpleStrings = new Dictionary<ByteBuf, SimpleStringRedisMessage>(replyKeys.Length);
        foreach (RedisReplyKey value in replyKeys)
        {
            ByteBuf key = Unpooled.UnreleasableBuffer(Unpooled.WrappedBuffer(
                Encoding.UTF8.GetBytes(value.ToString()))).AsReadOnly();
            SimpleStringRedisMessage message = new SimpleStringRedisMessage(Unpooled.UnreleasableBuffer(
                Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(value.ToString()))).ToString(Encoding.Default));
            stringToSimpleStrings[value.ToString()] = message;
            keyToSimpleStrings[value] = message;
            byteBufToSimpleStrings[key] = message;
        }

        keyToErrors = new Dictionary<RedisErrorKey, ErrorRedisMessage>(RedisErrorKey.Values().Length);
        stringToErrors = new Dictionary<string, ErrorRedisMessage>(RedisErrorKey.Values().Length);
        byteBufToErrors = new Dictionary<ByteBuf, ErrorRedisMessage>(RedisErrorKey.Values().Length);
        foreach (RedisErrorKey value in RedisErrorKey.Values())
        {
            ByteBuf key = Unpooled.UnreleasableBuffer(Unpooled.WrappedBuffer(
                Encoding.UTF8.GetBytes(value.ToString()))).AsReadOnly();
            ErrorRedisMessage message = new ErrorRedisMessage(Unpooled.UnreleasableBuffer(
                Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(value.ToString()))).ToString(Encoding.Default));
            stringToErrors[value.ToString()] = message;
            keyToErrors[value] = message;
            byteBufToErrors[key] = message;
        }

        byteBufToIntegers = new Dictionary<ByteBuf, IntegerRedisMessage>(SIZE_CACHED_INTEGER_NUMBER);
        longToIntegers = new Dictionary<long, IntegerRedisMessage>(SIZE_CACHED_INTEGER_NUMBER);
        longToByteBufs = new Dictionary<long, byte[]>(SIZE_CACHED_INTEGER_NUMBER);
        for (long value = MIN_CACHED_INTEGER_NUMBER; value < MAX_CACHED_INTEGER_NUMBER; value++)
        {
            byte[] keyBytes = RedisCodecUtil.LongToAsciiBytes(value);
            ByteBuf keyByteBuf = Unpooled.UnreleasableBuffer(Unpooled.WrappedBuffer(keyBytes)).AsReadOnly();
            IntegerRedisMessage cached = new IntegerRedisMessage(value);
            byteBufToIntegers[keyByteBuf] = cached;
            longToIntegers[value] = cached;
            longToByteBufs[value] = keyBytes;
        }
    }

    public SimpleStringRedisMessage GetSimpleString(string content)
    {
        return content == null ? null : stringToSimpleStrings.GetValueOrDefault(content);
    }

    /**
     * Returns {@link SimpleStringRedisMessage} for the given {@link RedisReplyKey}
     * or {@code null} if it does not exist.
     */
    public SimpleStringRedisMessage GetSimpleString(RedisReplyKey key)
    {
        return keyToSimpleStrings.GetValueOrDefault(key);
    }

    public SimpleStringRedisMessage GetSimpleString(ByteBuf content)
    {
        return content == null ? null : byteBufToSimpleStrings.GetValueOrDefault(content);
    }

    public ErrorRedisMessage GetError(string content)
    {
        return content == null ? null : stringToErrors.GetValueOrDefault(content);
    }

    /**
     * Returns {@link ErrorRedisMessage} for the given {@link RedisErrorKey}
     * or {@code null} if it does not exist.
     */
    public ErrorRedisMessage GetError(RedisErrorKey key)
    {
        return key == null ? null : keyToErrors.GetValueOrDefault(key);
    }

    public ErrorRedisMessage GetError(ByteBuf content)
    {
        return content == null ? null : byteBufToErrors.GetValueOrDefault(content);
    }

    public IntegerRedisMessage GetInteger(long value)
    {
        return longToIntegers.GetValueOrDefault(value);
    }

    public IntegerRedisMessage GetInteger(ByteBuf content)
    {
        return content == null ? null : byteBufToIntegers.GetValueOrDefault(content);
    }

    public byte[] GetByteBufOfInteger(long value)
    {
        return longToByteBufs.GetValueOrDefault(value);
    }
}
