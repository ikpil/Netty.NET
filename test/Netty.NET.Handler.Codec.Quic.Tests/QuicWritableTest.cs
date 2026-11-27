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

namespace Netty.NET.Handler.Codec.Quic.Tests;

// CLR: every method of QuicWritableTest is CLR-inapplicable (docs/codec-native-quic-porting-manifest.json), so the class
// declares no test:
// - testCorrectlyHandleWritabilityReadRequestedInReadComplete and testCorrectlyHandleWritabilityReadRequestedInRead
//   expect the server's 64 KiB stream write to stay incomplete until the client, whose stream receive window is 16 KiB
//   (initialMaxStreamDataBidirectionalLocal) and whose auto-read is off, reads (assertFalse(writePromise.isDone()) on
//   the first read). System.Net.Quic's QuicStream takes the received stream data into its own buffer ahead of the
//   application's reads, so the peer's flow control does not hold the write back: measured with .NET 10.0.11, a 64 KiB
//   server write to a client with a 16 KiB window completes without any client read, and the ported test fails its
//   first-read assertion.
// - testBytesUntilUnwritable expects bytesBeforeUnwritable() to be quiche's stream send capacity (the peer's flow
//   control credit), lowered by exactly the bytes of each write (assertEquals(before, newBefore + size)) until the
//   channel turns unwritable at the end of the credit. System.Net.Quic exposes no send capacity of a stream, so the
//   msquic stream channel's writability follows the write buffer water marks, and the ChannelOutboundBuffer counts each
//   entry with CHANNEL_OUTBOUND_BUFFER_ENTRY_OVERHEAD (96 bytes) on top of its bytes; the ported test timed out.
// Their original comments:
// First read
// Let's trigger the reads. This will ensure we will consume the data and the remote peer
// should be notified that it can write more data.
// Close the parent Datagram channel as well.
public class QuicWritableTest : AbstractQuicTest
{
}
