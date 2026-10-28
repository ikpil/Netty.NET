/*
 * Copyright 2016 The Netty Project
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
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Moq;
using Netty.NET.Common.Concurrent;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Resolver.Tests;

public class InetSocketAddressResolverTest
{

    [Fact]
    public void TestCloseDelegates()
    {
        var nameResolver = new Mock<INameResolver<IPAddress>>();
        var resolver = new InetSocketAddressResolver(ImmediateEventExecutor.INSTANCE, nameResolver.Object);
        resolver.Dispose();
        nameResolver.Verify(r => r.Dispose(), Times.Once());
    }
}
