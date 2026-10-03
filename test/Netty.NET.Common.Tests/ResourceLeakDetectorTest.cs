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
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using Xunit;

namespace Netty.NET.Common.Tests;

[CollectionDefinition("Leak detector globals", DisableParallelization = true)]
public class LeakDetectorGlobalsCollection { }

[Collection("Leak detector globals")]
public class ResourceLeakDetectorTest : IDisposable
{
    private static volatile int sink;
    private readonly ResourceLeakDetectorLevel previous = ResourceLeakDetector.GetLevel();
    public ResourceLeakDetectorTest() => ResourceLeakDetector.SetLevel(ResourceLeakDetectorLevel.SIMPLE);
    public void Dispose() => ResourceLeakDetector.SetLevel(previous);

    // The JVM's @Timeout is 60 seconds. CLR conditional weak values and captured
    // managed stacks need a wider bound; retain all 50 threads and 5,000,000 pairs.
    [Fact(Timeout = 120000)]
    public void TestConcurrentUsage()
    {
        int finished = 0;
        Exception error = null;
        // With 50 threads issue #6087 is reproducible on every run.
        var threads = new Thread[50];
        using var barrier = new Barrier(threads.Length);
        for (int i = 0; i < threads.Length; i++)
        {
            threads[i] = new Thread(() =>
            {
                var resources = new Queue<LeakAwareResource>(100);
                bool CloseResources(bool checkClosed)
                {
                    while (resources.TryDequeue(out LeakAwareResource value))
                        if (!value.Close() && checkClosed)
                        {
                            Interlocked.CompareExchange(ref error, new InvalidOperationException("ResourceLeak.close() returned 'false' but expected 'true'"), null);
                            return true;
                        }
                    return false;
                }
                try
                {
                    Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(10)));
                    // Run 10000 times or until the test is marked as finished.
                    for (int b = 0; b < 1000 && Volatile.Read(ref finished) == 0; b++)
                    {
                        // Allocate 100 LeakAwareResource per run and close them after it.
                        for (int a = 0; a < 100; a++)
                        {
                            Resource resource = new DefaultResource();
                            resources.Enqueue(new LeakAwareResource(resource, DefaultResource.detector.Track(resource)));
                        }
                        if (CloseResources(true)) Volatile.Write(ref finished, 1);
                    }
                }
                catch (ThreadInterruptedException) { Thread.CurrentThread.Interrupt(); }
                catch (Exception failure) { Interlocked.CompareExchange(ref error, failure, null); }
                finally
                {
                    // Just close all resource now without assert it to eliminate more reports.
                    try { CloseResources(false); }
                    catch (Exception failure) { Interlocked.CompareExchange(ref error, failure, null); }
                }
            }) { IsBackground = true };
            threads[i].Start();
        }
        // Just wait until all threads are done.
        var elapsed = Stopwatch.StartNew();
        foreach (Thread thread in threads)
            Assert.True(thread.Join(TimeSpan.FromSeconds(120) - elapsed.Elapsed));
        // Check if we had any leak reports in the ResourceLeakDetector itself
        DefaultResource.detector.AssertNoErrors();
        AssertNoErrors(error);
    }

    [Fact(Timeout = 10000)]
    public void TestLeakSetupHints()
    {
        DefaultResource.detectorWithSetupHint.Initialise();
        LeakResource();
        var deadline = Stopwatch.StartNew();
        do
        {
            // Trigger GC.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            // Track another resource to trigger refqueue visiting.
            Resource resource2 = new DefaultResource();
            DefaultResource.detectorWithSetupHint.Track(resource2).Close(resource2);
            // Give the GC something to work on.
            for (int i = 0; i < 1000; i++) sink = RuntimeHelpers.GetHashCode(new byte[10000]);
        } while (DefaultResource.detectorWithSetupHint.GetLeaksFound() < 1 && deadline.Elapsed < TimeSpan.FromSeconds(10));
        Assert.Equal(1, DefaultResource.detectorWithSetupHint.GetLeaksFound());
        DefaultResource.detectorWithSetupHint.AssertNoErrors();
    }

    [Fact(Timeout = 10000)]
    public void TestLeakBrokenHint()
    {
        DefaultResource.detectorWithSetupHint.Initialise();
        DefaultResource.detectorWithSetupHint.failOnUntraced = false;
        DefaultResource.detectorWithSetupHint.initialHint = new BrokenHint();
        var failure = Assert.Throws<InvalidOperationException>(LeakResource);
        Assert.Equal("expected failure", failure.Message);
        DefaultResource.detectorWithSetupHint.initialHint = DefaultResource.detectorWithSetupHint.canaryString;
        var deadline = Stopwatch.StartNew();
        do
        {
            // Trigger GC.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            // Track another resource to trigger refqueue visiting.
            Resource resource2 = new DefaultResource();
            DefaultResource.detectorWithSetupHint.Track(resource2).Close(resource2);
            // Give the GC something to work on.
            for (int i = 0; i < 1000; i++) sink = RuntimeHelpers.GetHashCode(new byte[10000]);
        } while (DefaultResource.detectorWithSetupHint.GetLeaksFound() < 1 && deadline.Elapsed < TimeSpan.FromSeconds(10));
        Assert.Equal(1, DefaultResource.detectorWithSetupHint.GetLeaksFound());
        DefaultResource.detectorWithSetupHint.AssertNoErrors();
    }
    private sealed class BrokenHint : IResourceLeakHint { public string ToHintString() => throw new InvalidOperationException("expected failure"); }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LeakResource()
    {
        Resource resource = new DefaultResource();
        // We'll never close this ResourceLeakTracker.
        DefaultResource.detectorWithSetupHint.Track(resource);
    }
    // Mimic the way how we implement our classes that should help with leak detection
    private sealed class LeakAwareResource(Resource resource, IResourceLeakTracker<Resource> leak) : Resource
    {
        public bool Close()
        {
            // Using ResourceLeakDetector.close(...) to prove this fixes the leak problem reported
            // in https://github.com/netty/netty/issues/6034 .
            //
            // The following implementation would produce a leak:
            //     return leak.close();
            return leak.Close(resource);
        }
    }
    private sealed class DefaultResource : Resource
    {
        // Sample every allocation
        internal static readonly TestResourceLeakDetector<Resource> detector = new(typeof(Resource), 1, int.MaxValue);
        internal static readonly CreationRecordLeakDetector<Resource> detectorWithSetupHint = new(typeof(Resource), 1);
        public bool Close() => true;
    }
    private interface Resource { bool Close(); }
    private static void AssertNoErrors(Exception error) { if (error != null) ExceptionDispatchInfo.Capture(error).Throw(); }
    private sealed class TestResourceLeakDetector<T> : ResourceLeakDetector<T> where T : class
    {
        private Exception error;
        internal TestResourceLeakDetector(Type type, int sampling, long maxActive) : base(type, sampling, maxActive) { }
        protected override void ReportTracedLeak(string type, string records) => ReportError(new InvalidOperationException("Leak reported for '" + type + "':\n" + records));
        protected override void ReportUntracedLeak(string type) => ReportError(new InvalidOperationException("Leak reported for '" + type + "'"));
        protected override void ReportInstancesLeak(string type) => ReportError(new InvalidOperationException("Leak reported for '" + type + "'"));
        private void ReportError(Exception failure) => Interlocked.CompareExchange(ref error, failure, null);
        internal void AssertNoErrors() => ResourceLeakDetectorTest.AssertNoErrors(error);
    }
    private sealed class CreationRecordLeakDetector<T>(Type type, int sampling) : ResourceLeakDetector<T>(type, sampling) where T : class
    {
        internal string canaryString;
        internal object initialHint;
        internal bool failOnUntraced = true;
        private Exception error;
        private int leaksFound;
        internal void Initialise()
        {
            canaryString = "creation-canary-" + Guid.NewGuid();
            initialHint = canaryString;
            failOnUntraced = true;
            error = null;
            leaksFound = 0;
        }
        protected override bool NeedReport() => true;
        protected override void ReportTracedLeak(string type, string records)
        {
            if (!records.Contains(canaryString, StringComparison.Ordinal)) ReportError(new InvalidOperationException("Leak records did not contain canary string"));
            Interlocked.Increment(ref leaksFound);
        }
        protected override void ReportUntracedLeak(string type)
        {
            if (failOnUntraced) ReportError(new InvalidOperationException("Got untraced leak w/o canary string"));
            Interlocked.Increment(ref leaksFound);
        }
        private void ReportError(Exception failure) => Interlocked.CompareExchange(ref error, failure, null);
        protected override object GetInitialHint(string type) => initialHint;
        internal int GetLeaksFound() => Volatile.Read(ref leaksFound);
        internal void AssertNoErrors() => ResourceLeakDetectorTest.AssertNoErrors(error);
    }
}
