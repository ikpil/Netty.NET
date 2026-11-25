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

namespace Netty.NET.Handler.Codec.Http;

/**
 * Combine the {@link HttpRequest} and {@link FullHttpMessage}, so the request is a <i>complete</i> HTTP
 * request.
 */
// CLR: the Java interface takes the I prefix (owner rule). The re-declarations of copy(), duplicate(),
// retainedDuplicate(), replace(ByteBuf), retain(int), retain(), touch(), touch(Object), setProtocolVersion,
// setMethod and setUri only narrow the return type and are inherited (see IHttpContent and IHttpRequest).
public interface IFullHttpRequest : IHttpRequest, IFullHttpMessage
{
}
