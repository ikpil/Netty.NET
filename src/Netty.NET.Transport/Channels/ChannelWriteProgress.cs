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

using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Transport.Channels;

/// <summary>A channel, its transport write's native completion Task and cumulative accepted byte counts.</summary>
/// <remarks>
/// Operation identifies the actual outbound queue entry, after outbound handlers transform or split
/// messages. It may differ from the Task returned by an asynchronous handler or executor boundary.
/// Progress.Total is null for messages whose size is unknown. Reports hold no borrowed message or
/// buffer reference. Observing or dispatching reports does not own operation completion/cancellation.
/// </remarks>
/**
 * An special {@link ChannelFuture} which is used to indicate the {@link FileRegion} transfer progress
 */
// CLR: channel metadata and a native Task replace the channel-specific Future/await/listener hierarchy.
public readonly record struct ChannelWriteProgress(IChannel Channel, Task Operation, TransferProgress Progress);
