/*
 * Copyright 2014 The Netty Project
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

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Handler for outbound HTTP/2 traffic.
 */
public interface IHttp2ConnectionEncoder : IHttp2FrameWriter
{

    /**
     * Sets the lifecycle manager. Must be called as part of initialization before the encoder is used.
     */
    void LifecycleManager(IHttp2LifecycleManager lifecycleManager);

    /**
     * Provides direct access to the underlying connection.
     */
    IHttp2Connection Connection();

    /**
     * Provides the remote flow controller for managing outbound traffic.
     */
    IHttp2RemoteFlowController FlowController();

    /**
     * Provides direct access to the underlying frame writer object.
     */
    IHttp2FrameWriter FrameWriter();

    /**
     * Gets the local settings on the top of the queue that has been sent but not ACKed. This may
     * return {@code null}.
     */
    Http2Settings PollSentSettings();

    /**
     * Sets the settings for the remote endpoint of the HTTP/2 connection.
     */
    void RemoteSettings(Http2Settings settings);

    /**
     * Writes the given data to the internal {@link Http2FrameWriter} without performing any
     * state checks on the connection/stream.
     */
    // CLR: writeFrame(...) is re-declared with the signature of IHttp2FrameWriter.WriteFrameAsync only to document
    // it, so the comment stays here and the member is inherited (a repeated interface member would hide it, CS0108).
}
