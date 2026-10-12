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

using Netty.NET.Common;

namespace Netty.NET.Buffer;

public static partial class ByteBufUtil
{
    /**
     * Set {@link AbstractByteBuf#leakDetector}'s {@link ResourceLeakDetector.LeakListener}.
     *
     * @param leakListener If leakListener is not null, it will be notified once a ByteBuf leak is detected.
     */
    /// <remarks>Replaces the shared buffer detector's listener; null removes it.
    /// Notifications follow the existing detector's reporting policy and duplicate suppression,
    /// on the thread that drains collected leaks during a subsequent tracking call.</remarks>
    public static void SetLeakListener(ResourceLeakDetector<ByteBuf>.LeakListener leakListener)
        => ByteBuf.LeakDetector.SetLeakListener(leakListener);
}
