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

namespace Netty.NET.Handler.Codec.Quic.Tests;

// CLR: testParameters, the only method of QuicTransportParametersTest, is CLR-inapplicable
// (docs/codec-native-quic-porting-manifest.json), so the class declares no test. It reads the peer's
// QuicTransportParameters on both peers (peerTransportParameters()) and asserts their values. System.Net.Quic does not
// expose the peer's transport parameters: IQuicChannel.PeerTransportParameters() throws NotSupportedException
// (docs/codec-classes-quic-porting.md, Unsupported features).
// Its original comment:
// -1 is the max value for an uint64_t that is converted to int64_t.
public class QuicTransportParametersTest : AbstractQuicTest
{
}
