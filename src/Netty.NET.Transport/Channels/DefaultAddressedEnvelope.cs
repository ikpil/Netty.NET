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

using System;
using System.Net;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Transport.Channels;

/**
 * The default {@link AddressedEnvelope} implementation.
 *
 * @param <M> the type of the wrapped message
 * @param <A> the type of the recipient address
 */
public class DefaultAddressedEnvelope<T, TAddress> : IAddressedEnvelope<T, TAddress> where TAddress : EndPoint
{
    /**
     * Creates a new instance with the specified {@code message}, {@code recipient} address, and
     * {@code sender} address.
     */
    /**
     * Creates a new instance with the specified {@code message} and {@code recipient} address.
     * The sender address becomes {@code null}.
     */
    public DefaultAddressedEnvelope(T message, TAddress recipient, TAddress sender = null)
    {
        if (message == null)
        {
            throw new ArgumentNullException("message");
        }
        if (recipient == null && sender == null)
        {
            throw new ArgumentNullException("recipient and sender");
        }
        Content = message;
        Recipient = recipient;
        Sender = sender;
    }
    public T Content { get; }
    public TAddress Sender { get; }
    public TAddress Recipient { get; }
    public int RefCnt => Content is IReferenceCounted counted ? counted.RefCnt : 1;
    public IReferenceCounted Retain() { ReferenceCountUtil.Retain(Content); return this; }
    public IReferenceCounted Retain(int increment) { ReferenceCountUtil.Retain(Content, increment); return this; }
    public bool Release() => ReferenceCountUtil.Release(Content);
    public bool Release(int decrement) => ReferenceCountUtil.Release(Content, decrement);
    public IReferenceCounted Touch() { ReferenceCountUtil.Touch(Content); return this; }
    public IReferenceCounted Touch(object hint) { ReferenceCountUtil.Touch(Content, hint); return this; }
    // CLR: Java concatenation renders a null recipient as "null".
    public override string ToString()
    {
        string recipient = Recipient?.ToString() ?? "null";
        if (Sender != null)
        {
            return StringUtil.SimpleClassName(this) +
                    '(' + Sender + " => " + recipient + ", " + Content + ')';
        }
        else
        {
            return StringUtil.SimpleClassName(this) +
                    "(=> " + recipient + ", " + Content + ')';
        }
    }
}
