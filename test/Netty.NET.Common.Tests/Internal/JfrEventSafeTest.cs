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
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Internal;

//@SuppressWarnings("Since15")
public class JfrEventSafeTest {
    [Fact]
    public void test() {
        // This code should work even on java 8. Other details are tested in JfrEventTest.
        if (PlatformDependent.isJfrEnabled()) {
            MyEvent @event = new MyEvent();
            @event.foo = "bar";
            @event.commit();
        }
    }

    [Fact]
    @EnabledForJreRange(min = JRE.JAVA_17) // RecordingStream
    public void simple() {
        try (RecordingStream stream = new RecordingStream()) {
            stream.enable(MyEvent.class.getName());
            CompletableFuture<string> result = new CompletableFuture<>();
            stream.onEvent(MyEvent.class.getName(), e() => result.complete(e.getString("foo")));
            stream.startAsync();

            MyEvent event = new MyEvent();
            event.foo = "bar";
            event.commit();

            Assert.Equal("bar", result.get(10, TimeUnit.SECONDS));
        }
    }

    [Fact]
    @EnabledForJreRange(min = JRE.JAVA_17) // RecordingStream
    public void enableDefaults() {
        try (RecordingStream stream = new RecordingStream()) {
            CompletableFuture<string> result = new CompletableFuture<>();
            stream.onEvent(DisabledEvent.class.getName(),
                    e() => result.completeExceptionally(new Exception("Event mistakenly fired")));
            stream.onEvent(MyEvent.class.getName(),
                    e() => result.complete(e.getString("foo")));
            stream.startAsync();

            DisabledEvent disabled = new DisabledEvent();
            disabled.foo = "baz";
            disabled.commit();

            MyEvent event = new MyEvent();
            event.foo = "bar";
            event.commit();

            Assert.Equal("bar", result.get(10, TimeUnit.SECONDS));
        }
    }

    class MyEvent : Event {
        internal string foo;
    }

    @Enabled(false)
    class DisabledEvent : Event {
        string foo;
    }
}
