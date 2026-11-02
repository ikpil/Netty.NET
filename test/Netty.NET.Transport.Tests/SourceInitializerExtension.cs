/*
 * Copyright 2023 The Netty Project
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
using System.Threading;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

internal sealed class SourceInitializerExtension : ChannelInitializerExtension, IDisposable
{
    internal readonly ThreadLocal<IChannel> LastSeenClient = new();
    internal readonly ThreadLocal<IChannel> LastSeenListener = new();
    internal readonly ThreadLocal<IChannel> LastSeenChild = new();
    internal void ClearThreadLocals() { LastSeenClient.Value = null; LastSeenListener.Value = null; LastSeenChild.Value = null; }
    public override void PostInitializeClientChannel(IChannel channel) => LastSeenClient.Value = channel;
    public override void PostInitializeServerListenerChannel(IChannel channel) => LastSeenListener.Value = channel;
    public override void PostInitializeServerChildChannel(IChannel channel) => LastSeenChild.Value = channel;
    public void Dispose() { LastSeenClient.Dispose(); LastSeenListener.Dispose(); LastSeenChild.Dispose(); }
}
