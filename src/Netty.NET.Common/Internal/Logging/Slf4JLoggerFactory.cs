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
namespace Netty.NET.Common.Internal.Logging;

/**
 * Logger factory which creates a <a href="https://www.slf4j.org/">SLF4J</a>
 * logger.
 */
public class Slf4JLoggerFactory : InternalLoggerFactory {

    public static readonly InternalLoggerFactory INSTANCE = new Slf4JLoggerFactory();

    private Slf4JLoggerFactory() 
    {
    }

    Slf4JLoggerFactory(bool failIfNOP) {
        assert failIfNOP; // Should be always called with true.
        if (LoggerFactory.getILoggerFactory() instanceof NOPLoggerFactory) {
            throw new NoClassDefFoundError("NOPLoggerFactory not supported");
        }
    }

    @Override
    public IInternalLogger newInstance(string name) {
        return wrapLogger(LoggerFactory.getLogger(name));
    }

    // package-private for testing.
    static IInternalLogger wrapLogger(Logger logger) {
        return logger instanceof LocationAwareLogger ?
                new LocationAwareSlf4JLogger((LocationAwareLogger) logger) : new Slf4JLogger(logger);
    }

    public static InternalLoggerFactory getInstanceWithNopCheck() {
        return NopInstanceHolder.INSTANCE_WITH_NOP_CHECK;
    }

    private static class NopInstanceHolder {
        private static readonly InternalLoggerFactory INSTANCE_WITH_NOP_CHECK = new Slf4JLoggerFactory(true);
    }
}
