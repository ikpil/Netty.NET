/*
 * Copyright 2012 The Netty Project
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

namespace Netty.NET.Handler.Codec.Socks;

/**
 * An abstract class that defines a SocksRequest, providing common properties for
 * {@link SocksInitRequest}, {@link SocksAuthRequest}, {@link SocksCmdRequest} and {@link UnknownSocksRequest}.
 *
 * @see SocksInitRequest
 * @see SocksAuthRequest
 * @see SocksCmdRequest
 * @see UnknownSocksRequest
 */
public abstract class SocksRequest : SocksMessage
{
    private readonly SocksRequestType requestType;

    // CLR: SocksRequestType is a C# enum (a value type), so checkNotNull(requestType, "requestType") has no
    // counterpart; see SocksMessage.
    protected SocksRequest(SocksRequestType requestType) : base(SocksMessageType.REQUEST)
    {
        this.requestType = requestType;
    }

    /**
     * Returns socks request type
     *
     * @return socks request type
     */
    public SocksRequestType RequestType()
    {
        return requestType;
    }
}
