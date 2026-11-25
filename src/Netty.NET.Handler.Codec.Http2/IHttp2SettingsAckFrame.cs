/*
 * Copyright 2019 The Netty Project
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

namespace Netty.NET.Handler.Codec.Http2;

/**
 * An ack for a previously received {@link Http2SettingsFrame}.
 * <p>
 * The <a href="https://tools.ietf.org/html/rfc7540#section-6.5">HTTP/2 protocol</a> enforces that ACKs are applied in
 * order, so this ACK will apply to the earliest received and not yet ACKed {@link Http2SettingsFrame} frame.
 */
public interface IHttp2SettingsAckFrame : IHttp2Frame
{
    // CLR: the interface constant is a static readonly interface field (as codec-http ILastHttpContent.EMPTY_LAST_CONTENT).
    public static readonly IHttp2SettingsAckFrame INSTANCE = new DefaultHttp2SettingsAckFrame();

    // CLR: name() is re-declared with the same type and inherited from IHttp2Frame (CS0108).
}
