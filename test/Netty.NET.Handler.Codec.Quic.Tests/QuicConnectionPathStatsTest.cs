/*
 * Copyright 2024 The Netty Project
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

// CLR: testPathStatsAreCollected, the only method of QuicConnectionPathStatsTest, is CLR-inapplicable
// (docs/codec-native-quic-porting-manifest.json), so the class declares no test. It collects QuicConnectionStats and the
// QuicConnectionPathStats of path 0 (collectPathStats(0)) and asserts their packet, byte and RTT counters.
// System.Net.Quic reports neither connection nor path statistics: IQuicChannel.CollectStatsAsync and
// CollectPathStatsAsync fail with NotSupportedException (docs/codec-classes-quic-porting.md, Unsupported features).
// Its original comments:
// Let's just echo back the message.
// Close the underlying QuicChannel as well.
// Wait until closure
public class QuicConnectionPathStatsTest : AbstractQuicTest
{
}
