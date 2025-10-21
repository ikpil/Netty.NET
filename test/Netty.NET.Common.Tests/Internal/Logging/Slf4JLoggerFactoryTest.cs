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
    public class Slf4JLoggerFactoryTest {

        [Fact]
        public void testCreation() {
            IInternalLogger logger = Slf4JLoggerFactory.INSTANCE.newInstance("foo");
            Assert.True(logger instanceof Slf4JLogger || logger instanceof LocationAwareSlf4JLogger);
            Assert.Equal("foo", logger.name());
        }

        [Fact]
        public void testCreationLogger() {
            Logger logger = mock(Logger.class);
            when(logger.getName()).thenReturn("testlogger");
            IInternalLogger internalLogger = Slf4JLoggerFactory.wrapLogger(logger);
            Assert.True(internalLogger instanceof Slf4JLogger);
            Assert.Equal("testlogger", internalLogger.name());
        }

        [Fact]
        public void testCreationLocationAwareLogger() {
            Logger logger = mock(LocationAwareLogger.class);
            when(logger.getName()).thenReturn("testlogger");
            IInternalLogger internalLogger = Slf4JLoggerFactory.wrapLogger(logger);
            Assert.True(internalLogger instanceof LocationAwareSlf4JLogger);
            Assert.Equal("testlogger", internalLogger.name());
        }

        [Fact]
        public void testFormatMessage() {
            ArgumentCaptor<string> captor = ArgumentCaptor.forClass(string.class);
            LocationAwareLogger logger = mock(LocationAwareLogger.class);
            when(logger.isDebugEnabled()).thenReturn(true);
            when(logger.isErrorEnabled()).thenReturn(true);
            when(logger.isInfoEnabled()).thenReturn(true);
            when(logger.isTraceEnabled()).thenReturn(true);
            when(logger.isWarnEnabled()).thenReturn(true);
            when(logger.getName()).thenReturn("testlogger");

            IInternalLogger internalLogger = Slf4JLoggerFactory.wrapLogger(logger);
            internalLogger.debug("{}", "debug");
            internalLogger.debug("{} {}", "debug1", "debug2");
            internalLogger.debug("{} {} {}", "debug1", "debug2", "debug3");

            internalLogger.error("{}", "error");
            internalLogger.error("{} {}", "error1", "error2");
            internalLogger.error("{} {} {}", "error1", "error2", "error3");

            internalLogger.info("{}", "info");
            internalLogger.info("{} {}", "info1", "info2");
            internalLogger.info("{} {} {}", "info1", "info2", "info3");

            internalLogger.trace("{}", "trace");
            internalLogger.trace("{} {}", "trace1", "trace2");
            internalLogger.trace("{} {} {}", "trace1", "trace2", "trace3");

            internalLogger.warn("{}", "warn");
            internalLogger.warn("{} {}", "warn1", "warn2");
            internalLogger.warn("{} {} {}", "warn1", "warn2", "warn3");

            verify(logger, times(3)).log(ArgumentMatchers.<Marker>isNull(), eq(LocationAwareSlf4JLogger.FQCN),
            eq(LocationAwareLogger.DEBUG_INT), captor.capture(), ArgumentMatchers.<Object[]>isNull(),
            ArgumentMatchers.<Throwable>isNull());
            verify(logger, times(3)).log(ArgumentMatchers.<Marker>isNull(), eq(LocationAwareSlf4JLogger.FQCN),
            eq(LocationAwareLogger.ERROR_INT), captor.capture(), ArgumentMatchers.<Object[]>isNull(),
            ArgumentMatchers.<Throwable>isNull());
            verify(logger, times(3)).log(ArgumentMatchers.<Marker>isNull(), eq(LocationAwareSlf4JLogger.FQCN),
            eq(LocationAwareLogger.INFO_INT), captor.capture(), ArgumentMatchers.<Object[]>isNull(),
            ArgumentMatchers.<Throwable>isNull());
            verify(logger, times(3)).log(ArgumentMatchers.<Marker>isNull(), eq(LocationAwareSlf4JLogger.FQCN),
            eq(LocationAwareLogger.TRACE_INT), captor.capture(), ArgumentMatchers.<Object[]>isNull(),
            ArgumentMatchers.<Throwable>isNull());
            verify(logger, times(3)).log(ArgumentMatchers.<Marker>isNull(), eq(LocationAwareSlf4JLogger.FQCN),
            eq(LocationAwareLogger.WARN_INT), captor.capture(), ArgumentMatchers.<Object[]>isNull(),
            ArgumentMatchers.<Throwable>isNull());

            Iterator<string> logMessages = captor.getAllValues().iterator();
            Assert.Equal("debug", logMessages.next());
            Assert.Equal("debug1 debug2", logMessages.next());
            Assert.Equal("debug1 debug2 debug3", logMessages.next());
            Assert.Equal("error", logMessages.next());
            Assert.Equal("error1 error2", logMessages.next());
            Assert.Equal("error1 error2 error3", logMessages.next());
            Assert.Equal("info", logMessages.next());
            Assert.Equal("info1 info2", logMessages.next());
            Assert.Equal("info1 info2 info3", logMessages.next());
            Assert.Equal("trace", logMessages.next());
            Assert.Equal("trace1 trace2", logMessages.next());
            Assert.Equal("trace1 trace2 trace3", logMessages.next());
            Assert.Equal("warn", logMessages.next());
            Assert.Equal("warn1 warn2", logMessages.next());
            Assert.Equal("warn1 warn2 warn3", logMessages.next());
            Assert.False(logMessages.hasNext());
        }
    }
}