/*
 * Copyright 2021 The Netty Project
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
using System.Net;
using Netty.NET.Buffer;

namespace Netty.NET.Transport.Channels.Sockets;

/**
 * Allows to use <a href="https://blog.cloudflare.com/accelerating-udp-packet-transmission-for-quic/">GSO</a>
 * if the underlying OS supports it. Before using this you should ensure your system support it.
 */
public sealed class SegmentedDatagramPacket : DatagramPacket
{
    /**
     * Create a new instance.
     *
     * @param data          the {@link ByteBuf} which must be continguous.
     * @param segmentSize   the segment size.
     * @param recipient     the recipient.
     */
    public SegmentedDatagramPacket(ByteBuf data, int segmentSize, IPEndPoint recipient)
        : this(data, segmentSize, recipient, null) { }

    /**
     * Create a new instance.
     *
     * @param data          the {@link ByteBuf} which must be continguous.
     * @param segmentSize   the segment size.
     * @param recipient     the recipient.
     */
    public SegmentedDatagramPacket(ByteBuf data, int segmentSize, IPEndPoint recipient, IPEndPoint sender)
        : base(data, recipient, sender)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(segmentSize);
        SegmentSize = segmentSize;
    }

    /**
     * Return the size of each segment (the last segment can be smaller).
     *
     * @return size of segments.
     */
    public int SegmentSize { get; }

    public override SegmentedDatagramPacket Copy() => (SegmentedDatagramPacket)base.Copy();
    public override SegmentedDatagramPacket Duplicate() => (SegmentedDatagramPacket)base.Duplicate();
    public override SegmentedDatagramPacket RetainedDuplicate() => (SegmentedDatagramPacket)base.RetainedDuplicate();
    public override SegmentedDatagramPacket Replace(ByteBuf content) => new(content, SegmentSize, Recipient, Sender);
}
