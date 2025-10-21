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
    public class Slf4JLoggerTest {
        private static final Exception e = new Exception();

        [Fact]
        public void testIsTraceEnabled() {
            Logger mockLogger = mock(Logger.class);

            when(mockLogger.getName()).thenReturn("foo");
            when(mockLogger.isTraceEnabled()).thenReturn(true);

            IInternalLogger logger = new Slf4JLogger(mockLogger);
            Assert.True(logger.isTraceEnabled());

            verify(mockLogger).getName();
            verify(mockLogger).isTraceEnabled();
        }

        [Fact]
        public void testIsDebugEnabled() {
            Logger mockLogger = mock(Logger.class);

            when(mockLogger.getName()).thenReturn("foo");
            when(mockLogger.isDebugEnabled()).thenReturn(true);

            IInternalLogger logger = new Slf4JLogger(mockLogger);
            Assert.True(logger.isDebugEnabled());

            verify(mockLogger).getName();
            verify(mockLogger).isDebugEnabled();
        }

        [Fact]
        public void testIsInfoEnabled() {
            Logger mockLogger = mock(Logger.class);

            when(mockLogger.getName()).thenReturn("foo");
            when(mockLogger.isInfoEnabled()).thenReturn(true);

            IInternalLogger logger = new Slf4JLogger(mockLogger);
            Assert.True(logger.isInfoEnabled());

            verify(mockLogger).getName();
            verify(mockLogger).isInfoEnabled();
        }

        [Fact]
        public void testIsWarnEnabled() {
            Logger mockLogger = mock(Logger.class);

            when(mockLogger.getName()).thenReturn("foo");
            when(mockLogger.isWarnEnabled()).thenReturn(true);

            IInternalLogger logger = new Slf4JLogger(mockLogger);
            Assert.True(logger.isWarnEnabled());

            verify(mockLogger).getName();
            verify(mockLogger).isWarnEnabled();
        }

        [Fact]
        public void testIsErrorEnabled() {
            Logger mockLogger = mock(Logger.class);

            when(mockLogger.getName()).thenReturn("foo");
            when(mockLogger.isErrorEnabled()).thenReturn(true);

            IInternalLogger logger = new Slf4JLogger(mockLogger);
            Assert.True(logger.isErrorEnabled());

            verify(mockLogger).getName();
            verify(mockLogger).isErrorEnabled();
        }

        [Fact]
        public void testTrace() {
            Logger mockLogger = mock(Logger.class);

            when(mockLogger.getName()).thenReturn("foo");

            IInternalLogger logger = new Slf4JLogger(mockLogger);
            logger.trace("a");

            verify(mockLogger).getName();
            verify(mockLogger).trace("a");
        }

        [Fact]
        public void testTraceWithException() {
            Logger mockLogger = mock(Logger.class);

            when(mockLogger.getName()).thenReturn("foo");

            IInternalLogger logger = new Slf4JLogger(mockLogger);
            logger.trace("a", e);

            verify(mockLogger).getName();
            verify(mockLogger).trace("a", e);
        }

        [Fact]
        public void testDebug() {
            Logger mockLogger = mock(Logger.class);

            when(mockLogger.getName()).thenReturn("foo");

            IInternalLogger logger = new Slf4JLogger(mockLogger);
            logger.debug("a");

            verify(mockLogger).getName();
            verify(mockLogger).debug("a");
        }

        [Fact]
        public void testDebugWithException() {
            Logger mockLogger = mock(Logger.class);

            when(mockLogger.getName()).thenReturn("foo");

            IInternalLogger logger = new Slf4JLogger(mockLogger);
            logger.debug("a", e);

            verify(mockLogger).getName();
            verify(mockLogger).debug("a", e);
        }

        [Fact]
        public void testInfo() {
            Logger mockLogger = mock(Logger.class);

            when(mockLogger.getName()).thenReturn("foo");

            IInternalLogger logger = new Slf4JLogger(mockLogger);
            logger.info("a");

            verify(mockLogger).getName();
            verify(mockLogger).info("a");
        }

        [Fact]
        public void testInfoWithException() {
            Logger mockLogger = mock(Logger.class);

            when(mockLogger.getName()).thenReturn("foo");

            IInternalLogger logger = new Slf4JLogger(mockLogger);
            logger.info("a", e);

            verify(mockLogger).getName();
            verify(mockLogger).info("a", e);
        }

        [Fact]
        public void testWarn() {
            Logger mockLogger = mock(Logger.class);

            when(mockLogger.getName()).thenReturn("foo");

            IInternalLogger logger = new Slf4JLogger(mockLogger);
            logger.warn("a");

            verify(mockLogger).getName();
            verify(mockLogger).warn("a");
        }

        [Fact]
        public void testWarnWithException() {
            Logger mockLogger = mock(Logger.class);

            when(mockLogger.getName()).thenReturn("foo");

            IInternalLogger logger = new Slf4JLogger(mockLogger);
            logger.warn("a", e);

            verify(mockLogger).getName();
            verify(mockLogger).warn("a", e);
        }

        [Fact]
        public void testError() {
            Logger mockLogger = mock(Logger.class);

            when(mockLogger.getName()).thenReturn("foo");

            IInternalLogger logger = new Slf4JLogger(mockLogger);
            logger.error("a");

            verify(mockLogger).getName();
            verify(mockLogger).error("a");
        }

        [Fact]
        public void testErrorWithException() {
            Logger mockLogger = mock(Logger.class);

            when(mockLogger.getName()).thenReturn("foo");

            IInternalLogger logger = new Slf4JLogger(mockLogger);
            logger.error("a", e);

            verify(mockLogger).getName();
            verify(mockLogger).error("a", e);
        }
    }
}