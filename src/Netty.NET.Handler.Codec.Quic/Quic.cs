/*
 * Copyright 2020 The Netty Project
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
using System.Collections.Generic;
using System.Linq;
using System.Net.Quic;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Quic;

// CLR: the QUIC implementation is System.Net.Quic on msquic (owner decision 2026-11-27, docs/quic-design.md), so the
// availability probe asks System.Net.Quic instead of loading quiche through JNI (Quiche.quiche_version()): QUIC is
// available when the platform is Windows or Linux (the owner's platforms; the guard also satisfies the CA1416 platform
// analyzer for the [SupportedOSPlatform] QuicListener/QuicConnection types) and QuicListener.IsSupported &&
// QuicConnection.IsSupported ("Gets a value that indicates whether QUIC is supported on the current machine",
// https://learn.microsoft.com/en-us/dotnet/api/system.net.quic.quiclistener.issupported?view=net-10.0), which needs
// msquic (bundled on Windows 11 and Windows Server 2022, the libmsquic package on Linux). Otherwise the unavailability
// cause is a PlatformNotSupportedException, and ensureAvailability() throws a PlatformNotSupportedException with that
// cause where Java throws an UnsatisfiedLinkError with it (initCause). isVersionSupported asks
// Quiche.quiche_version_is_supported; System.Net.Quic does not let the application choose the version, so QUIC version 1
// (0x00000001, https://www.rfc-editor.org/rfc/rfc9000.html#section-15) is the one supported version. Throwable is
// Exception. Map.Entry<ChannelOption<?>, Object>[] is KeyValuePair<ChannelOption, object>[] (as Transport's
// Bootstrap.NewOptionsArray), and the attribute entries carry the value with a typed setter (value, apply), as
// Transport's Bootstrap keeps them, because channel.attr(key).set(value) needs the generic AttributeKey<T>
// (AbstractBootstrap.SetAttributes); HashMap/LinkedHashMap copies are Dictionary copies. The cast setOption of
// config() is the untyped IChannelConfig.SetOptions of that one option (as AbstractBootstrap's setChannelOption). The constants MAX_DATAGRAM_SIZE and RESET_TOKEN_LEN keep their values; the static initializer is
// the static constructor.
public sealed class Quic
{
    internal static readonly KeyValuePair<ChannelOption, object>[] EMPTY_OPTION_ARRAY =
            Array.Empty<KeyValuePair<ChannelOption, object>>();
    internal static readonly KeyValuePair<IAttributeKey, (object Value, Action<IChannel> Apply)>[] EMPTY_ATTRIBUTE_ARRAY =
            Array.Empty<KeyValuePair<IAttributeKey, (object Value, Action<IChannel> Apply)>>();

    internal const int MAX_DATAGRAM_SIZE = 1350;

    internal const int RESET_TOKEN_LEN = 16;

    private static readonly Exception UNAVAILABILITY_CAUSE;

    static Quic()
    {
        Exception cause = null;

        try
        {
            if (!IsQuicSupported())
            {
                cause = new PlatformNotSupportedException(
                        "System.Net.Quic is not supported on this platform (QUIC needs msquic on Windows or Linux)");
            }
        }
        catch (Exception error)
        {
            cause = error;
        }

        UNAVAILABILITY_CAUSE = cause;
    }

    // CLR-only helper: the System.Net.Quic probe (see the class comment).
    private static bool IsQuicSupported()
    {
        return (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) &&
                QuicListener.IsSupported && QuicConnection.IsSupported;
    }

    // CLR-only: QUIC version 1 (see the class comment).
    private const int QUIC_VERSION_1 = 0x00000001;

    /**
     * The maximum length of the <a href="https://www.rfc-editor.org/rfc/rfc9000.html#section-17.2">connection id</a>.
     *
     */
    public const int MAX_CONN_ID_LEN = 20;

    /**
     * Return if the given QUIC version is supported.
     *
     * @param version   the version.
     * @return          {@code true} if supported, {@code false} otherwise.
     */
    public static bool IsVersionSupported(int version)
    {
        return IsAvailable() && version == QUIC_VERSION_1;
    }

    /**
     * Returns {@code true} if and only if the QUIC implementation is usable on the running platform is available.
     *
     * @return {@code true} if this QUIC implementation can be used on the current platform, {@code false} otherwise.
     */
    public static bool IsAvailable()
    {
        return UNAVAILABILITY_CAUSE == null;
    }

    /**
     * Ensure that QUIC implementation is usable on the running platform is available.
     *
     * @throws UnsatisfiedLinkError if unavailable
     */
    public static void EnsureAvailability()
    {
        if (UNAVAILABILITY_CAUSE != null)
        {
            throw new PlatformNotSupportedException(
                    "failed to load the required native library", UNAVAILABILITY_CAUSE);
        }
    }

    /**
     * Returns the cause of unavailability.
     *
     * @return the cause if unavailable. {@code null} if available.
     */
    public static Exception UnavailabilityCause()
    {
        return UNAVAILABILITY_CAUSE;
    }

    internal static KeyValuePair<ChannelOption, object>[] ToOptionsArray(Dictionary<ChannelOption, object> opts)
    {
        return new Dictionary<ChannelOption, object>(opts).ToArray();
    }

    internal static KeyValuePair<IAttributeKey, (object Value, Action<IChannel> Apply)>[] ToAttributesArray(
            Dictionary<IAttributeKey, (object Value, Action<IChannel> Apply)> attributes)
    {
        return new Dictionary<IAttributeKey, (object Value, Action<IChannel> Apply)>(attributes).ToArray();
    }

    private static void SetAttributes(IChannel channel,
                                      KeyValuePair<IAttributeKey, (object Value, Action<IChannel> Apply)>[] attrs)
    {
        foreach (KeyValuePair<IAttributeKey, (object Value, Action<IChannel> Apply)> e in attrs)
        {
            e.Value.Apply(channel);
        }
    }

    private static void SetChannelOptions(
            IChannel channel, KeyValuePair<ChannelOption, object>[] options, IInternalLogger logger)
    {
        foreach (KeyValuePair<ChannelOption, object> e in options)
        {
            SetChannelOption(channel, e.Key, e.Value, logger);
        }
    }

    private static void SetChannelOption(
            IChannel channel, ChannelOption option, object value, IInternalLogger logger)
    {
        try
        {
            if (!channel.Config.SetOptions(new[] { new KeyValuePair<ChannelOption, object>(option, value) }))
            {
                logger.Warn("Unknown channel option '{}' for channel '{}'", option, channel);
            }
        }
        catch (Exception t)
        {
            logger.Warn(
                    "Failed to set channel option '{}' with value '{}' for channel '{}'", option, value, channel, t);
        }
    }

    /**
     * Allow to specify a {@link ChannelOption} which is used for the {@link QuicStreamChannel} instances once they got
     * created. Use a value of {@code null} to remove a previous set {@link ChannelOption}.
     */
    internal static void UpdateOptions<T>(Dictionary<ChannelOption, object> options, ChannelOption<T> option, T value)
    {
        ObjectUtil.CheckNotNull(option, "option");
        if (value is null)
        {
            options.Remove(option);
        }
        else
        {
            options[option] = value;
        }
    }

    /**
     * Allow to specify an initial attribute of the newly created {@link QuicStreamChannel}. If the {@code value} is
     * {@code null}, the attribute of the specified {@code key} is removed.
     */
    internal static void UpdateAttributes<T>(Dictionary<IAttributeKey, (object Value, Action<IChannel> Apply)> attributes,
                                             AttributeKey<T> key, T value) where T : class
    {
        ObjectUtil.CheckNotNull(key, "key");
        if (value == null)
        {
            attributes.Remove(key);
        }
        else
        {
            attributes[key] = (value, channel => channel.Attr(key).Set(value));
        }
    }

    internal static void SetupChannel(IChannel ch, KeyValuePair<ChannelOption, object>[] options,
                                      KeyValuePair<IAttributeKey, (object Value, Action<IChannel> Apply)>[] attrs,
                                      IChannelHandler handler, IInternalLogger logger)
    {
        Quic.SetChannelOptions(ch, options, logger);
        Quic.SetAttributes(ch, attrs);
        if (handler != null)
        {
            ch.Pipeline.AddLast(handler);
        }
    }

    private Quic()
    {
    }
}
