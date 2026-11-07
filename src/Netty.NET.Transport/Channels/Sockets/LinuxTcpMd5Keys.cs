/*
 * Copyright 2015 The Netty Project
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
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Netty.NET.Transport.Channels.Sockets;

internal sealed class LinuxTcpMd5Keys
{
    private const int TcpMd5Sig = 14;
    private const int AddressStorageSize = 128;
    private const int KeyLengthOffset = AddressStorageSize + 2;
    private const int KeyOffset = AddressStorageSize + 8;
    private const int MaxKeyLength = 80;
    private const int NoSuchEntry = 2;

    private readonly Socket _socket;
    private readonly HashSet<IPAddress> _peers = new();

    internal LinuxTcpMd5Keys(Socket socket, bool accepted)
    {
        _socket = socket;
        // The kernel may copy the listener's matching key to the accepted socket.
        if (accepted && socket.RemoteEndPoint is IPEndPoint peer)
            _peers.Add(Normalize(peer.Address));
    }

    internal void Replace(IReadOnlyDictionary<IPAddress, byte[]> newKeys)
    {
        ArgumentNullException.ThrowIfNull(newKeys);
        // Add synchronized as newTcpMp5Sigs might do multiple operations on the socket itself.
        lock (_peers)
        {
            // Validate incoming values
            var replacement = new Dictionary<IPAddress, byte[]>(newKeys.Count);
            foreach (var (address, key) in newKeys)
            {
                ArgumentNullException.ThrowIfNull(address);
                ArgumentNullException.ThrowIfNull(key);
                if (key.Length is 0 or > MaxKeyLength)
                    throw new ArgumentException("A TCP MD5 key must contain between 1 and 80 bytes.", nameof(newKeys));
                replacement.Add(Normalize(address), key);
            }

            // Remove mappings not present in the new set.
            foreach (var peer in _peers.ToArray())
            {
                if (replacement.ContainsKey(peer)) continue;
                Apply(peer, ReadOnlySpan<byte>.Empty);
                _peers.Remove(peer);
            }

            // Set new mappings and store addresses which we set.
            foreach (var (peer, key) in replacement)
            {
                Apply(peer, key);
                _peers.Add(peer);
            }
        }
    }

    private IPAddress Normalize(IPAddress peer)
    {
        if (peer.IsIPv4MappedToIPv6)
            peer = peer.MapToIPv4();
        if (peer.AddressFamily != _socket.AddressFamily)
        {
            if (peer.AddressFamily == AddressFamily.InterNetwork && _socket.AddressFamily == AddressFamily.InterNetworkV6 && _socket.DualMode)
                peer = peer.MapToIPv6();
            else
                throw new ArgumentException("The peer address family is incompatible with the socket.", nameof(peer));
        }
        return new IPAddress(peer.GetAddressBytes());
    }

    private unsafe void Apply(IPAddress peer, ReadOnlySpan<byte> key)
    {
        Span<byte> value = stackalloc byte[KeyOffset + MaxKeyLength];
        value.Clear();
        var address = new IPEndPoint(peer, 0).Serialize();
        address.Buffer.Span[..address.Size].CopyTo(value);
        BitConverter.TryWriteBytes(value[KeyLengthOffset..], (ushort)key.Length);
        key.CopyTo(value[KeyOffset..]);
        try
        {
            fixed (byte* bytes = value)
            {
                if (SetSocketOption(_socket.SafeHandle, (int)SocketOptionLevel.Tcp, TcpMd5Sig, bytes, (uint)value.Length) == 0)
                    return;
                if (key.IsEmpty && Marshal.GetLastPInvokeError() == NoSuchEntry)
                    return;
                throw new SocketException();
            }
        }
        finally { CryptographicOperations.ZeroMemory(value); }
    }

    [DllImport("libc", EntryPoint = "setsockopt", SetLastError = true)]
    private static extern unsafe int SetSocketOption(SafeSocketHandle socket, int level, int name, byte* value, uint length);
}
