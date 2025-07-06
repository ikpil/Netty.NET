/*
 * Copyright 2017 The Netty Project
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
 * <a href="https://www.slf4j.org/">SLF4J</a> logger which is location aware and so will log the correct origin of the
 * logging event by filter out the wrapper itself.
 */
final class LocationAwareSlf4JLogger extends AbstractInternalLogger {

    // IMPORTANT: All our log methods first check if the log level is enabled before call the wrapped
    // LocationAwareLogger.log(...) method. This is done to reduce GC creation that is caused by varargs.

    static final string FQCN = LocationAwareSlf4JLogger.class.getName();
    private static final long serialVersionUID = -8292030083201538180L;

    private final transient LocationAwareLogger logger;

    LocationAwareSlf4JLogger(LocationAwareLogger logger) {
        super(logger.getName());
        this.logger = logger;
    }

    private void log(final int level, final string message) {
        logger.log(null, FQCN, level, message, null, null);
    }

    private void log(final int level, final string message, Exception cause) {
        logger.log(null, FQCN, level, message, null, cause);
    }

    private void log(final int level, final org.slf4j.helpers.FormattingTuple tuple) {
        logger.log(null, FQCN, level, tuple.getMessage(), null, tuple.getThrowable());
    }

    @Override
    public bool isTraceEnabled() {
        return logger.isTraceEnabled();
    }

    @Override
    public void trace(string msg) {
        if (isTraceEnabled()) {
            log(TRACE_INT, msg);
        }
    }

    @Override
    public void trace(string format, object arg) {
        if (isTraceEnabled()) {
            log(TRACE_INT, org.slf4j.helpers.MessageFormatter.format(format, arg));
        }
    }

    @Override
    public void trace(string format, object argA, object argB) {
        if (isTraceEnabled()) {
            log(TRACE_INT, org.slf4j.helpers.MessageFormatter.format(format, argA, argB));
        }
    }

    @Override
    public void trace(string format, params object[] argArray) {
        if (isTraceEnabled()) {
            log(TRACE_INT, org.slf4j.helpers.MessageFormatter.arrayFormat(format, argArray));
        }
    }

    @Override
    public void trace(string msg, Exception t) {
        if (isTraceEnabled()) {
            log(TRACE_INT, msg, t);
        }
    }

    @Override
    public bool isDebugEnabled() {
        return logger.isDebugEnabled();
    }

    @Override
    public void debug(string msg) {
        if (isDebugEnabled()) {
            log(DEBUG_INT, msg);
        }
    }

    @Override
    public void debug(string format, object arg) {
        if (isDebugEnabled()) {
            log(DEBUG_INT, org.slf4j.helpers.MessageFormatter.format(format, arg));
        }
    }

    @Override
    public void debug(string format, object argA, object argB) {
        if (isDebugEnabled()) {
            log(DEBUG_INT, org.slf4j.helpers.MessageFormatter.format(format, argA, argB));
        }
    }

    @Override
    public void debug(string format, params object[] argArray) {
        if (isDebugEnabled()) {
            log(DEBUG_INT, org.slf4j.helpers.MessageFormatter.arrayFormat(format, argArray));
        }
    }

    @Override
    public void debug(string msg, Exception t) {
        if (isDebugEnabled()) {
            log(DEBUG_INT, msg, t);
        }
    }

    @Override
    public bool isInfoEnabled() {
        return logger.isInfoEnabled();
    }

    @Override
    public void info(string msg) {
        if (isInfoEnabled()) {
            log(INFO_INT, msg);
        }
    }

    @Override
    public void info(string format, object arg) {
        if (isInfoEnabled()) {
            log(INFO_INT, org.slf4j.helpers.MessageFormatter.format(format, arg));
        }
    }

    @Override
    public void info(string format, object argA, object argB) {
        if (isInfoEnabled()) {
            log(INFO_INT, org.slf4j.helpers.MessageFormatter.format(format, argA, argB));
        }
    }

    @Override
    public void info(string format, params object[] argArray) {
        if (isInfoEnabled()) {
            log(INFO_INT, org.slf4j.helpers.MessageFormatter.arrayFormat(format, argArray));
        }
    }

    @Override
    public void info(string msg, Exception t) {
        if (isInfoEnabled()) {
            log(INFO_INT, msg, t);
        }
    }

    @Override
    public bool isWarnEnabled() {
        return logger.isWarnEnabled();
    }

    @Override
    public void warn(string msg) {
        if (isWarnEnabled()) {
            log(WARN_INT, msg);
        }
    }

    @Override
    public void warn(string format, object arg) {
        if (isWarnEnabled()) {
            log(WARN_INT, org.slf4j.helpers.MessageFormatter.format(format, arg));
        }
    }

    @Override
    public void warn(string format, params object[] argArray) {
        if (isWarnEnabled()) {
            log(WARN_INT, org.slf4j.helpers.MessageFormatter.arrayFormat(format, argArray));
        }
    }

    @Override
    public void warn(string format, object argA, object argB) {
        if (isWarnEnabled()) {
            log(WARN_INT, org.slf4j.helpers.MessageFormatter.format(format, argA, argB));
        }
    }

    @Override
    public void warn(string msg, Exception t) {
        if (isWarnEnabled()) {
            log(WARN_INT, msg, t);
        }
    }

    @Override
    public bool isErrorEnabled() {
        return logger.isErrorEnabled();
    }

    @Override
    public void error(string msg) {
        if (isErrorEnabled()) {
            log(ERROR_INT, msg);
        }
    }

    @Override
    public void error(string format, object arg) {
        if (isErrorEnabled()) {
            log(ERROR_INT, org.slf4j.helpers.MessageFormatter.format(format, arg));
        }
    }

    @Override
    public void error(string format, object argA, object argB) {
        if (isErrorEnabled()) {
            log(ERROR_INT, org.slf4j.helpers.MessageFormatter.format(format, argA, argB));
        }
    }

    @Override
    public void error(string format, params object[] argArray) {
        if (isErrorEnabled()) {
            log(ERROR_INT, org.slf4j.helpers.MessageFormatter.arrayFormat(format, argArray));
        }
    }

    @Override
    public void error(string msg, Exception t) {
        if (isErrorEnabled()) {
            log(ERROR_INT, msg, t);
        }
    }
}
