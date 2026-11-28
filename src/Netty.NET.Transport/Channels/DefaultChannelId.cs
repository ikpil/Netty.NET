/*
 * Copyright 2013 The Netty Project
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
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Transport.Channels;

/**
 * The default {@link ChannelId} implementation.
 */
// CLR: serialVersionUID is JVM serialization metadata; the concrete JSON converter below carries the long text.
[JsonConverter(typeof(Converter))]
public sealed class DefaultChannelId : IChannelId
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(DefaultChannelId));
    private static readonly byte[] MACHINE_ID;
    private const int PROCESS_ID_LEN = 4;
    private static readonly int PROCESS_ID;
    private const int SEQUENCE_LEN = 4;
    private const int TIMESTAMP_LEN = 8;
    private const int RANDOM_LEN = 4;

    private static int nextSequence;

    private static readonly char[] HEX_CHARS = "0123456789abcdef".ToCharArray();

    /**
     * Returns a new {@link DefaultChannelId} instance.
     */
    public static DefaultChannelId NewInstance()
    {
        // CLR: SystemTimer.NanoTime and DateTimeOffset.ToUnixTimeMilliseconds stand for System.nanoTime() and
        // System.currentTimeMillis(); Random.Shared.Next covers ThreadLocalRandom.nextInt()'s full int range.
        return new DefaultChannelId(MACHINE_ID,
                                    PROCESS_ID,
                                    Interlocked.Increment(ref nextSequence) - 1,
                                    Reverse(SystemTimer.NanoTime()) ^ DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                                    NextInt());
    }

    static DefaultChannelId()
    {
        int processId = -1;
        string customProcessId = SystemPropertyUtil.Get("io.netty.processId");
        if (customProcessId != null)
        {
            if (!int.TryParse(customProcessId, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out processId))
            {
                // Malformed input.
                processId = -1;
            }

            if (processId < 0)
            {
                processId = -1;
                logger.Warn("-Dio.netty.processId: {} (malformed)", customProcessId);
            }
            else if (logger.IsDebugEnabled())
            {
                logger.Debug("-Dio.netty.processId: {} (user-set)", processId);
            }
        }

        if (processId < 0)
        {
            processId = DefaultProcessId();

            if (logger.IsDebugEnabled())
            {
                logger.Debug("-Dio.netty.processId: {} (auto-detected)", processId);
            }
        }

        PROCESS_ID = processId;

        byte[] machineId = null;
        string customMachineId = SystemPropertyUtil.Get("io.netty.machineId");
        if (customMachineId != null)
        {
            try
            {
                machineId = MacAddressUtil.ParseMAC(customMachineId);
            }
            catch (Exception e)
            {
                logger.Warn("-Dio.netty.machineId: {} (malformed)", customMachineId, e);
            }

            if (machineId != null)
            {
                logger.Debug("-Dio.netty.machineId: {} (user-set)", customMachineId);
            }
        }

        if (machineId == null)
        {
            machineId = MacAddressUtil.DefaultMachineId();

            if (logger.IsDebugEnabled())
            {
                logger.Debug("-Dio.netty.machineId: {} (auto-detected)", MacAddressUtil.FormatAddress(machineId));
            }
        }

        MACHINE_ID = machineId;
    }

    // CLR: ProcessHandle.current().pid() through ClassLoader reflection is Environment.ProcessId
    // (https://learn.microsoft.com/dotnet/api/system.environment.processid).
    internal static int ProcessHandlePid()
    {
        // pid is positive on unix, non{-1,0} on windows
        return Environment.ProcessId;
    }

    // CLR: Environment.ProcessId is always available, so the jmxPid(ClassLoader) fallback (JMX/Android reflection)
    // has no counterpart.
    internal static int DefaultProcessId()
    {
        return ProcessHandlePid();
    }

    private readonly byte[] machineId;
    private readonly int processId;
    private readonly int sequence;
    private readonly long timestamp;
    private readonly int random;
    private readonly int hashCode;

    private string shortValue;
    private string longValue;

    /**
     * Visible for testing
     */
    internal DefaultChannelId(byte[] machineId, int processId, int sequence,
                     long timestamp, int random)
    {
        this.machineId = machineId;
        this.processId = processId;
        this.sequence = sequence;
        this.timestamp = timestamp;
        this.random = random;
        hashCode = ComputeHashCode();
    }

    private int ComputeHashCode()
    {
        // CLR: Arrays.hashCode(byte[]) and Long.hashCode(long) are spelled out to keep the Java values.
        int h = 1;

        foreach (byte b in machineId)
        {
            h = unchecked(31 * h + (sbyte)b);
        }

        h = unchecked(31 * h + processId);
        h = unchecked(31 * h + sequence);
        h = unchecked(31 * h + (int)(timestamp ^ (long)((ulong)timestamp >> 32)));
        h = unchecked(31 * h + random);
        return h;
    }

    public string AsShortText()
    {
        string shortValue = this.shortValue;
        if (shortValue == null)
        {
            StringBuilder buf = new StringBuilder(RANDOM_LEN * 2);
            AppendHexInt(buf, random);
            this.shortValue = shortValue = buf.ToString();
        }

        return shortValue;
    }

    public string AsLongText()
    {
        string longValue = this.longValue;
        if (longValue == null)
        {
            this.longValue = longValue = NewLongValue();
        }

        return longValue;
    }

    private string NewLongValue()
    {
        int machineIdLen = machineId.Length;
        StringBuilder buf = new StringBuilder(
                2 * (machineIdLen + PROCESS_ID_LEN + SEQUENCE_LEN + TIMESTAMP_LEN + RANDOM_LEN) + 4);
        AppendHexBytes(buf, machineId);
        buf.Append('-');
        AppendHexInt(buf, processId);
        buf.Append('-');
        AppendHexInt(buf, sequence);
        buf.Append('-');
        AppendHexLong(buf, timestamp);
        buf.Append('-');
        AppendHexInt(buf, random);

        return buf.ToString();
    }

    private static void AppendHexBytes(StringBuilder buf, byte[] bytes)
    {
        foreach (byte b in bytes)
        {
            buf.Append(HEX_CHARS[(b & 0xFF) >> 4]);
            buf.Append(HEX_CHARS[b & 0xF]);
        }
    }

    private static void AppendHexInt(StringBuilder buf, int value)
    {
        for (int i = 28; i >= 0; i -= 4)
        {
            buf.Append(HEX_CHARS[(int)((uint)value >> i) & 0xF]);
        }
    }

    private static void AppendHexLong(StringBuilder buf, long value)
    {
        for (int i = 60; i >= 0; i -= 4)
        {
            buf.Append(HEX_CHARS[(int)(((ulong)value >> i) & 0xF)]);
        }
    }

    // CLR: Long.reverse(long), the bit reversal of the value.
    private static long Reverse(long value)
    {
        ulong v = (ulong)value;
        ulong r = 0;

        for (int i = 0; i < 64; i++)
        {
            r = (r << 1) | (v & 1);
            v >>= 1;
        }

        return (long)r;
    }

    private static int NextInt()
    {
        Span<byte> bytes = stackalloc byte[4];
        Random.Shared.NextBytes(bytes);

        return BitConverter.ToInt32(bytes);
    }

    public override int GetHashCode()
    {
        return hashCode;
    }

    public int CompareTo(IChannelId o)
    {
        if (ReferenceEquals(this, o))
        {
            // short circuit
            return 0;
        }

        if (o is DefaultChannelId other)
        {
            int cmp = CompareBytes(machineId, other.machineId);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = ((uint)processId).CompareTo((uint)other.processId);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = ((uint)sequence).CompareTo((uint)other.sequence);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = ((ulong)timestamp).CompareTo((ulong)other.timestamp);
            if (cmp != 0)
            {
                return cmp;
            }

            return ((uint)random).CompareTo((uint)other.random);
        }

        // CLR: String.compareTo is an ordinal UTF-16 comparison; a null ID sorts first (BCL IComparable convention).
        return o == null ? 1 : string.CompareOrdinal(AsLongText(), o.AsLongText());
    }

    private static int CompareBytes(byte[] a, byte[] b)
    {
        int len1 = a.Length;
        int len2 = b.Length;
        int len = Math.Min(len1, len2);

        for (int k = 0; k < len; k++)
        {
            int cmp = (a[k] & 0xFF) - (b[k] & 0xFF);
            if (cmp != 0)
            {
                return cmp;
            }
        }

        return len1 - len2;
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(this, obj))
        {
            return true;
        }
        if (!(obj is DefaultChannelId))
        {
            return false;
        }

        DefaultChannelId other = (DefaultChannelId)obj;
        return hashCode == other.hashCode
                && random == other.random
                && processId == other.processId
                && sequence == other.sequence
                && timestamp == other.timestamp
                && machineId.AsSpan().SequenceEqual(other.machineId);
    }

    public override string ToString()
    {
        return AsShortText();
    }

    // CLR: a concrete-type JSON string replaces Java ObjectStream serialization. The value is the long text, which
    // carries every field (machine ID, process ID, sequence, timestamp, random); interface polymorphism is not encoded.
    public sealed class Converter : JsonConverter<DefaultChannelId>
    {
        public Converter()
        {
        }

        public override DefaultChannelId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String || !TryParse(reader.GetString(), out DefaultChannelId id))
                throw new JsonException("Expected a DefaultChannelId long text.");

            return id;
        }

        public override void Write(Utf8JsonWriter writer, DefaultChannelId value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.AsLongText());
        }

        private static bool TryParse(string text, out DefaultChannelId id)
        {
            id = null;
            string[] parts = text?.Split('-');
            if (parts == null || parts.Length != 5 || parts[0].Length == 0 || parts[0].Length % 2 != 0 ||
                parts[1].Length != 8 || parts[2].Length != 8 || parts[3].Length != 16 || parts[4].Length != 8)
                return false;

            byte[] machineId;

            try
            {
                machineId = Convert.FromHexString(parts[0]);
            }
            catch (FormatException)
            {
                return false;
            }

            if (!uint.TryParse(parts[1], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint processId) ||
                !uint.TryParse(parts[2], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint sequence) ||
                !ulong.TryParse(parts[3], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong timestamp) ||
                !uint.TryParse(parts[4], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint random))
                return false;

            id = new DefaultChannelId(machineId, (int)processId, (int)sequence, (long)timestamp, (int)random);
            return string.Equals(id.AsLongText(), text, StringComparison.Ordinal);
        }
    }
}
