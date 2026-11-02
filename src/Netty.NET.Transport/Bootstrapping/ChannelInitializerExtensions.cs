/*
 * Copyright 2023 The Netty Project
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
using System.Threading;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Bootstrapping;

/**
 * The configurable facade that decides what {@link ChannelInitializerExtension}s to load and where to find them.
 */
internal static class ChannelInitializerExtensions
{
    private static readonly object Gate = new();
    private static readonly IInternalLogger Logger = InternalLoggerFactory.GetInstance(typeof(ChannelInitializerExtensions));
    private sealed record Entry(ChannelInitializerExtension Extension, double Priority, bool Invoke);
    private static Entry[] _entries = [];

    internal static IDisposable Register(ChannelInitializerExtension extension, bool invoke)
    {
        ArgumentNullException.ThrowIfNull(extension);
        var entry = new Entry(extension, extension.Priority, invoke);
        lock (Gate)
        {
            if (_entries.Any(e => ReferenceEquals(e.Extension, extension)))
                throw new ArgumentException("Extension already registered.", nameof(extension));
            var copy = _entries.Append(entry).ToArray();
            Array.Sort(copy, (a, b) => ComparePriority(a.Priority, b.Priority));
            Volatile.Write(ref _entries, copy);
        }
        Logger.Info("Channel initializer extension " + (invoke ? "registered: " : "detected: ") + extension.GetType().FullName);
        return new Registration(entry);
    }

    /**
     * Get the list of available extensions. The list is unmodifiable.
     */
    /**
     * Get the configuration extensions, which is a no-op implementation by default,
     * or a service-loading implementation if the {@code io.netty.bootstrap.extensions} system property is
     * {@code serviceload}.
     */
    // CLR registration replaces the upstream property/loader mechanism described above.
    internal static IReadOnlyList<ChannelInitializerExtension> Snapshot()
        => Array.AsReadOnly(Volatile.Read(ref _entries).Where(e => e.Invoke).Select(e => e.Extension).ToArray());

    private static int ComparePriority(double left, double right)
    {
        // Java Double.compare places NaN last and -0.0 before +0.0.
        if (double.IsNaN(left)) return double.IsNaN(right) ? 0 : 1;
        if (double.IsNaN(right)) return -1;
        int compare = left.CompareTo(right);
        return compare != 0 ? compare : BitConverter.DoubleToInt64Bits(left).CompareTo(BitConverter.DoubleToInt64Bits(right));
    }

    internal static void Notify(IReadOnlyList<ChannelInitializerExtension> extensions, IChannel channel, Action<ChannelInitializerExtension, IChannel> callback)
    {
        foreach (var extension in extensions)
        {
            try { callback(extension, channel); }
            catch (Exception error) when (error is not OutOfMemoryException)
            { Logger.Warn("Exception thrown from channel initializer extension " + extension.GetType().FullName, error); }
        }
    }

    private sealed class Registration(Entry entry) : IDisposable
    {
        private Entry _entry = entry;
        public void Dispose()
        {
            var removed = Interlocked.Exchange(ref _entry, null);
            if (removed == null) return;
            lock (Gate)
                Volatile.Write(ref _entries, _entries.Where(e => !ReferenceEquals(e, removed)).ToArray());
        }
    }
}
