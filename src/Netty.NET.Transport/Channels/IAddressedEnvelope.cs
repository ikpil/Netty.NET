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

using System.Net;
using Netty.NET.Common;

namespace Netty.NET.Transport.Channels;

/**
 * A message that wraps another message with a sender address and a recipient address.
 *
 * @param <M> the type of the wrapped message
 * @param <A> the type of the address
 */
public interface IAddressedEnvelope<out T, out TAddress> : IReferenceCounted where TAddress : EndPoint
{
    /**
     * Returns the message wrapped by this envelope message.
     */
    T Content { get; }
    /**
     * Returns the address of the sender of this message.
     */
    TAddress Sender { get; }
    /**
     * Returns the address of the recipient of this message.
     */
    TAddress Recipient { get; }
}
