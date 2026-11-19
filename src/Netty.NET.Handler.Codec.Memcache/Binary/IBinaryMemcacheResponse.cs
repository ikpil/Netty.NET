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

using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Memcache.Binary;

/**
 * Represents a full {@link BinaryMemcacheResponse}, which contains the header and optional key and extras.
 */
// CLR: retain(), retain(int), touch() and touch(Object) are re-declared only to narrow the return type, so they are
// inherited from IReferenceCounted, not repeated (owner decision 2026-11-19; docs/transport-porting.md, Decisions).
[UnstableApi]
public interface IBinaryMemcacheResponse : IBinaryMemcacheMessage
{

    /**
     * Returns the status of the response.
     *
     * @return the status of the response.
     */
    short Status();

    /**
     * Sets the status of the response.
     *
     * @param status the status to set.
     */
    IBinaryMemcacheResponse SetStatus(short status);
}
