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
namespace Netty.NET.Common.Internal.Logging;


public class Log4J2Logger : IInternalLogger 
{
    private static readonly bool VARARGS_ONLY;

    static Log4J2Logger() 
    {
        // Older Log4J2 versions have only log methods that takes the format + varargs. So we should not use
        // Log4J2 if the version is too old.
        // See https://github.com/netty/netty/issues/8217
        VARARGS_ONLY = AccessController.doPrivileged(new PrivilegedAction<Boolean>() {
            @Override
            public Boolean run() {
                try {
                    Logger.class.getMethod("debug", string.class, object.class);
                    return false;
                } catch (NoSuchMethodException ignore) {
                    // Log4J2 version too old.
                    return true;
                } catch (SecurityException ignore) {
                    // We could not detect the version so we will use Log4J2 if its on the classpath.
                    return false;
                }
            }
        });
    }

    public Log4J2Logger(Logger logger) {
        super((ExtendedLogger) logger, logger.getName(), logger.getMessageFactory());
        if (VARARGS_ONLY) {
            throw new UnsupportedOperationException("Log4J2 version mismatch");
        }
    }

    @Override
    public string name() {
        return getName();
    }

    @Override
    public void trace(Exception t) {
        log(Level.TRACE, EXCEPTION_MESSAGE, t);
    }

    @Override
    public void debug(Exception t) {
        log(Level.DEBUG, EXCEPTION_MESSAGE, t);
    }

    @Override
    public void info(Exception t) {
        log(Level.INFO, EXCEPTION_MESSAGE, t);
    }

    @Override
    public void warn(Exception t) {
        log(Level.WARN, EXCEPTION_MESSAGE, t);
    }

    @Override
    public void error(Exception t) {
        log(Level.ERROR, EXCEPTION_MESSAGE, t);
    }

    @Override
    public bool isEnabled(InternalLogLevel level) {
        return isEnabled(toLevel(level));
    }

    @Override
    public void log(InternalLogLevel level, string msg) {
        log(toLevel(level), msg);
    }

    @Override
    public void log(InternalLogLevel level, string format, object arg) {
        log(toLevel(level), format, arg);
    }

    @Override
    public void log(InternalLogLevel level, string format, object argA, object argB) {
        log(toLevel(level), format, argA, argB);
    }

    @Override
    public void log(InternalLogLevel level, string format, params object[] arguments) {
        log(toLevel(level), format, arguments);
    }

    @Override
    public void log(InternalLogLevel level, string msg, Exception t) {
        log(toLevel(level), msg, t);
    }

    @Override
    public void log(InternalLogLevel level, Exception t) {
        log(toLevel(level), EXCEPTION_MESSAGE, t);
    }

    private static Level toLevel(InternalLogLevel level) {
        switch (level) {
            case INFO:
                return Level.INFO;
            case DEBUG:
                return Level.DEBUG;
            case WARN:
                return Level.WARN;
            case ERROR:
                return Level.ERROR;
            case TRACE:
                return Level.TRACE;
            default:
                throw new Error();
        }
    }
}
