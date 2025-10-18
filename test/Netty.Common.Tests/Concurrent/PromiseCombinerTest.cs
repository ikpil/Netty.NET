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
namespace Netty.Common.Tests.Concurrent;public class PromiseCombinerTest {
    @Mock
    private Promise<Void> p1;
    private GenericFutureListener<Future<Void>> l1;
    private final GenericFutureListenerConsumer l1Consumer = new GenericFutureListenerConsumer() {
        @Override
        public void accept(GenericFutureListener<Future<Void>> listener) {
            l1 = listener;
        }
    };
    @Mock
    private Promise<Void> p2;
    private GenericFutureListener<Future<Void>> l2;
    private final GenericFutureListenerConsumer l2Consumer = new GenericFutureListenerConsumer() {
        @Override
        public void accept(GenericFutureListener<Future<Void>> listener) {
            l2 = listener;
        }
    };
    @Mock
    private Promise<Void> p3;
    private PromiseCombiner combiner;

    @BeforeEach
    public void setup() {
        MockitoAnnotations.initMocks(this);
        combiner = new PromiseCombiner(ImmediateEventExecutor.INSTANCE);
    }

    [Fact]
    public void testNullArgument() {
        try {
            combiner.finish(null);
            Assert.Fail();
        } catch (NullReferenceException expected) {
            // expected
        }
        combiner.finish(p1);
        verify(p1).trySuccess(null);
    }

    [Fact]
    public void testNullAggregatePromise() {
        combiner.finish(p1);
        verify(p1).trySuccess(null);
    }

    [Fact]
    public void testAddNullPromise() {
        assertThrows(NullReferenceException.class, new Executable() {
            @Override
            public void execute() {
                combiner.add(null);
            }
        });
    }

    [Fact]
    public void testAddAllNullPromise() {
        assertThrows(NullReferenceException.class, new Executable() {
            @Override
            public void execute() {
                combiner.addAll(null);
            }
        });
    }

    [Fact]
    public void testAddAfterFinish() {
        combiner.finish(p1);
        assertThrows(IllegalStateException.class, new Executable() {
            @Override
            public void execute() {
                combiner.add(p2);
            }
        });
    }

    //@SuppressWarnings("unchecked")
    [Fact]
    public void testAddAllAfterFinish() {
        combiner.finish(p1);
        assertThrows(IllegalStateException.class, new Executable() {
            @Override
            public void execute() {
                combiner.addAll(p2);
            }
        });
    }

    //@SuppressWarnings("unchecked")
    [Fact]
    public void testFinishCalledTwiceThrows() {
        combiner.finish(p1);
        assertThrows(IllegalStateException.class, new Executable() {
            @Override
            public void execute() {
                combiner.finish(p1);
            }
        });
    }

    [Fact]
    public void testAddAllSuccess() {
        mockSuccessPromise(p1, l1Consumer);
        mockSuccessPromise(p2, l2Consumer);
        combiner.addAll(p1, p2);
        combiner.finish(p3);
        l1.operationComplete(p1);
        verifyNotCompleted(p3);
        l2.operationComplete(p2);
        verifySuccess(p3);
    }

    [Fact]
    public void testAddSuccess() {
        mockSuccessPromise(p1, l1Consumer);
        mockSuccessPromise(p2, l2Consumer);
        combiner.add(p1);
        l1.operationComplete(p1);
        combiner.add(p2);
        l2.operationComplete(p2);
        verifyNotCompleted(p3);
        combiner.finish(p3);
        verifySuccess(p3);
    }

    [Fact]
    public void testAddAllFail() {
        Exception e1 = new Exception("fake exception 1");
        Exception e2 = new Exception("fake exception 2");
        mockFailedPromise(p1, e1, l1Consumer);
        mockFailedPromise(p2, e2, l2Consumer);
        combiner.addAll(p1, p2);
        combiner.finish(p3);
        l1.operationComplete(p1);
        verifyNotCompleted(p3);
        l2.operationComplete(p2);
        verifyFail(p3, e1);
    }

    [Fact]
    public void testAddFail() {
        Exception e1 = new Exception("fake exception 1");
        Exception e2 = new Exception("fake exception 2");
        mockFailedPromise(p1, e1, l1Consumer);
        mockFailedPromise(p2, e2, l2Consumer);
        combiner.add(p1);
        l1.operationComplete(p1);
        combiner.add(p2);
        l2.operationComplete(p2);
        verifyNotCompleted(p3);
        combiner.finish(p3);
        verifyFail(p3, e1);
    }

    [Fact]
    public void testEventExecutor() {
        EventExecutor executor = mock(EventExecutor.class);
        when(executor.inEventLoop()).thenReturn(false);
        combiner = new PromiseCombiner(executor);

        Future<?> future = mock(Future.class);

        try {
            combiner.add(future);
            Assert.Fail();
        } catch (IllegalStateException expected) {
            // expected
        }

        try {
            combiner.addAll(future);
            Assert.Fail();
        } catch (IllegalStateException expected) {
            // expected
        }

        //@SuppressWarnings("unchecked")
        Promise<Void> promise = (Promise<Void>) mock(Promise.class);
        try {
            combiner.finish(promise);
            Assert.Fail();
        } catch (IllegalStateException expected) {
            // expected
        }
    }

    private static void verifyFail(Promise<Void> p, Throwable cause) {
        verify(p).tryFailure(eq(cause));
    }

    private static void verifySuccess(Promise<Void> p) {
        verify(p).trySuccess(null);
    }

    private static void verifyNotCompleted(Promise<Void> p) {
        verify(p, never()).trySuccess(any(Void.class));
        verify(p, never()).tryFailure(any(Throwable.class));
        verify(p, never()).setSuccess(any(Void.class));
        verify(p, never()).setFailure(any(Throwable.class));
    }

    private static void mockSuccessPromise(Promise<Void> p, GenericFutureListenerConsumer consumer) {
        when(p.isDone()).thenReturn(true);
        when(p.isSuccess()).thenReturn(true);
        mockListener(p, consumer);
    }

    private static void mockFailedPromise(Promise<Void> p, Throwable cause, GenericFutureListenerConsumer consumer) {
        when(p.isDone()).thenReturn(true);
        when(p.isSuccess()).thenReturn(false);
        when(p.cause()).thenReturn(cause);
        mockListener(p, consumer);
    }

    //@SuppressWarnings("unchecked")
    private static void mockListener(final Promise<Void> p, final GenericFutureListenerConsumer consumer) {
        doAnswer(new Answer<Promise<Void>>() {
            @SuppressWarnings({ "unchecked", "raw-types" })
            @Override
            public Promise<Void> answer(InvocationOnMock invocation) throws Throwable {
                consumer.accept((GenericFutureListener) invocation.getArgument(0));
                return p;
            }
        }).when(p).addListener(any(GenericFutureListener.class));
    }

    interface GenericFutureListenerConsumer {
        void accept(GenericFutureListener<Future<Void>> listener);
    }
}
