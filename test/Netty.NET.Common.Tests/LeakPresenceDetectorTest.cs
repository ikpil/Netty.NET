/*
 * Copyright 2025 The Netty Project
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
using System.Runtime.CompilerServices;
using Xunit;

namespace Netty.NET.Common.Tests;

[Collection("Leak detector globals")]
public class LeakPresenceDetectorTest : IDisposable
{
    private readonly ResourceLeakDetectorFactory oldFactory = ResourceLeakDetectorFactory.instance();
    private void setUp() => ResourceLeakDetectorFactory.setResourceLeakDetectorFactory(new Factory());
    public void Dispose() => ResourceLeakDetectorFactory.setResourceLeakDetectorFactory(oldFactory);
    private sealed class Factory : ResourceLeakDetectorFactory
    {
        public override ResourceLeakDetector<T> newResourceLeakDetector<T>(Type resource, int samplingInterval, long maxActive)
            => new LeakPresenceDetector<T>(resource);
    }

    [Fact]
    public void noExceptionInNormalOperation()
    {
        setUp();
        var resource = new Resource();
        resource.close();
        resource.close();
        LeakPresenceDetector.check();
    }

    [Fact]
    public void exceptionOnUnclosed()
    {
        setUp();
        _ = new Resource();
        Assert.Throws<InvalidOperationException>(LeakPresenceDetector.check);
    }

    [Fact]
    public void noExceptionInStaticInitializerGlobal()
    {
        setUp();
        ResourceInStaticVariable1.init();
        LeakPresenceDetector.check();
    }

    private sealed class Resource : IDisposable
    {
        private readonly IResourceLeakTracker<Resource> leak;
        internal Resource() => leak = ResourceLeakDetectorFactory.instance().newResourceLeakDetector<Resource>(typeof(Resource)).track(this);
        internal void close() => leak.close(this);
        public void Dispose() => close();
    }
    private static class ResourceInStaticVariable1
    {
        private static readonly Resource RESOURCE;
        static ResourceInStaticVariable1() => RESOURCE = LeakPresenceDetector.staticInitializer(() => new Resource());
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void init() => GC.KeepAlive(RESOURCE);
    }
}
