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


internal.logging;

namespace Netty.NET.Common.Tests.Internal.Logging
{
    public class InternalLoggerFactoryTest {
        private static final Exception e = new Exception();
        private InternalLoggerFactory oldLoggerFactory;
        private IInternalLogger mockLogger;

        @BeforeEach
        public void init() {
            oldLoggerFactory = InternalLoggerFactory.getDefaultFactory();

            final InternalLoggerFactory mockFactory = mock(InternalLoggerFactory.class);
            mockLogger = mock(IInternalLogger.class);
            when(mockFactory.newInstance("mock")).thenReturn(mockLogger);
            InternalLoggerFactory.setDefaultFactory(mockFactory);
        }

        @AfterEach
        public void destroy() {
            reset(mockLogger);
            InternalLoggerFactory.setDefaultFactory(oldLoggerFactory);
        }

        [Fact]
        public void shouldNotAllowNullDefaultFactory() {
            Assert.Throws<NullReferenceException>(new Executable() {
                @Override
                public void execute() {
                InternalLoggerFactory.setDefaultFactory(null);
            }
            });
        }

        [Fact]
        public void shouldGetInstance() {
            InternalLoggerFactory.setDefaultFactory(oldLoggerFactory);

            string helloWorld = "Hello, world!";

            IInternalLogger one = InternalLoggerFactory.getInstance("helloWorld");
            IInternalLogger two = InternalLoggerFactory.getInstance(helloWorld.getClass());

            Assert.NotNull(one);
            Assert.NotNull(two);
            Assert.NotSame(one, two);
        }

        [Fact]
        public void testIsTraceEnabled() {
            when(mockLogger.isTraceEnabled()).thenReturn(true);

            IInternalLogger logger = InternalLoggerFactory.getInstance("mock");
            Assert.True(logger.isTraceEnabled());
            verify(mockLogger).isTraceEnabled();
        }

        [Fact]
        public void testIsDebugEnabled() {
            when(mockLogger.isDebugEnabled()).thenReturn(true);

            IInternalLogger logger = InternalLoggerFactory.getInstance("mock");
            Assert.True(logger.isDebugEnabled());
            verify(mockLogger).isDebugEnabled();
        }

        [Fact]
        public void testIsInfoEnabled() {
            when(mockLogger.isInfoEnabled()).thenReturn(true);

            IInternalLogger logger = InternalLoggerFactory.getInstance("mock");
            Assert.True(logger.isInfoEnabled());
            verify(mockLogger).isInfoEnabled();
        }

        [Fact]
        public void testIsWarnEnabled() {
            when(mockLogger.isWarnEnabled()).thenReturn(true);

            IInternalLogger logger = InternalLoggerFactory.getInstance("mock");
            Assert.True(logger.isWarnEnabled());
            verify(mockLogger).isWarnEnabled();
        }

        [Fact]
        public void testIsErrorEnabled() {
            when(mockLogger.isErrorEnabled()).thenReturn(true);

            IInternalLogger logger = InternalLoggerFactory.getInstance("mock");
            Assert.True(logger.isErrorEnabled());
            verify(mockLogger).isErrorEnabled();
        }

        [Fact]
        public void testTrace() {
            final IInternalLogger logger = InternalLoggerFactory.getInstance("mock");
            logger.trace("a");
            verify(mockLogger).trace("a");
        }

        [Fact]
        public void testTraceWithException() {
            final IInternalLogger logger = InternalLoggerFactory.getInstance("mock");
            logger.trace("a", e);
            verify(mockLogger).trace("a", e);
        }

        [Fact]
        public void testDebug() {
            final IInternalLogger logger = InternalLoggerFactory.getInstance("mock");
            logger.debug("a");
            verify(mockLogger).debug("a");
        }

        [Fact]
        public void testDebugWithException() {
            final IInternalLogger logger = InternalLoggerFactory.getInstance("mock");
            logger.debug("a", e);
            verify(mockLogger).debug("a", e);
        }

        [Fact]
        public void testInfo() {
            final IInternalLogger logger = InternalLoggerFactory.getInstance("mock");
            logger.info("a");
            verify(mockLogger).info("a");
        }

        [Fact]
        public void testInfoWithException() {
            final IInternalLogger logger = InternalLoggerFactory.getInstance("mock");
            logger.info("a", e);
            verify(mockLogger).info("a", e);
        }

        [Fact]
        public void testWarn() {
            final IInternalLogger logger = InternalLoggerFactory.getInstance("mock");
            logger.warn("a");
            verify(mockLogger).warn("a");
        }

        [Fact]
        public void testWarnWithException() {
            final IInternalLogger logger = InternalLoggerFactory.getInstance("mock");
            logger.warn("a", e);
            verify(mockLogger).warn("a", e);
        }

        [Fact]
        public void testError() {
            final IInternalLogger logger = InternalLoggerFactory.getInstance("mock");
            logger.error("a");
            verify(mockLogger).error("a");
        }

        [Fact]
        public void testErrorWithException() {
            final IInternalLogger logger = InternalLoggerFactory.getInstance("mock");
            logger.error("a", e);
            verify(mockLogger).error("a", e);
        }
    }
}