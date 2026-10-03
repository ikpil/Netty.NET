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
    private readonly ResourceLeakDetectorFactory oldFactory = ResourceLeakDetectorFactory.Instance();
    private void SetUp() => ResourceLeakDetectorFactory.SetResourceLeakDetectorFactory(new Factory());
    public void Dispose() => ResourceLeakDetectorFactory.SetResourceLeakDetectorFactory(oldFactory);
    private sealed class Factory : ResourceLeakDetectorFactory
    {
        public override ResourceLeakDetector<T> NewResourceLeakDetector<T>(Type resource, int samplingInterval, long maxActive)
            => new LeakPresenceDetector<T>(resource);
    }

    [Fact]
    public void NoExceptionInNormalOperation()
    {
        SetUp();
        var resource = new Resource();
        resource.Close();
        resource.Close();
        LeakPresenceDetector.Check();
    }

    [Fact]
    public void ExceptionOnUnclosed()
    {
        SetUp();
        _ = new Resource();
        Assert.Throws<InvalidOperationException>(LeakPresenceDetector.Check);
    }

    [Fact]
    public void NoExceptionInStaticInitializerGlobal()
    {
        SetUp();
        ResourceInStaticVariable1.Init();
        LeakPresenceDetector.Check();
    }

    private sealed class Resource : IDisposable
    {
        private readonly IResourceLeakTracker<Resource> leak;
        internal Resource() => leak = ResourceLeakDetectorFactory.Instance().NewResourceLeakDetector<Resource>(typeof(Resource)).Track(this);
        internal void Close() => leak.Close(this);
        public void Dispose() => Close();
    }
    private static class ResourceInStaticVariable1
    {
        private static readonly Resource RESOURCE;
        static ResourceInStaticVariable1() => RESOURCE = LeakPresenceDetector.StaticInitializer(() => new Resource());
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Init() => GC.KeepAlive(RESOURCE);
    }
}
