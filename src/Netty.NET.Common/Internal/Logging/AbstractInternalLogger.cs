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

using System;

namespace Netty.NET.Common.Internal.Logging;


/**
 * A skeletal implementation of {@link InternalLogger}.  This class implements
 * all methods that have a {@link InternalLogLevel} parameter by default to call
 * specific logger methods such as {@link #info(string)} or {@link #isInfoEnabled()}.
 */
public abstract class AbstractInternalLogger : IInternalLogger
{
    private static readonly string EXCEPTION_MESSAGE = "Unexpected exception:";

    private readonly string _name;

    /**
     * Creates a new instance.
     */
    protected AbstractInternalLogger(string name) {
        this._name = ObjectUtil.checkNotNull(name, "name");
    }

    public string name() {
        return _name;
    }

    public abstract bool isTraceEnabled();
    public abstract void trace(string msg);
    public abstract void trace(string format, object arg);
    public abstract void trace(string format, object argA, object argB);
    public abstract void trace(string format, params object[] arguments);
    public abstract void trace(string msg, Exception t);

    public bool isEnabled(InternalLogLevel level) {
        switch (level) {
        case InternalLogLevel.TRACE:
            return isTraceEnabled();
        case InternalLogLevel.DEBUG:
            return isDebugEnabled();
        case InternalLogLevel.INFO:
            return isInfoEnabled();
        case InternalLogLevel.WARN:
            return isWarnEnabled();
        case InternalLogLevel.ERROR:
            return isErrorEnabled();
        default:
            throw new ArgumentOutOfRangeException();
        }
    }

    public void trace(Exception t) {
        trace(EXCEPTION_MESSAGE, t);
    }

    public abstract bool isDebugEnabled();
    public abstract void debug(string msg);
    public abstract void debug(string format, object arg);
    public abstract void debug(string format, object argA, object argB);
    public abstract void debug(string format, params object[] arguments);
    public abstract void debug(string msg, Exception t);

    public void debug(Exception t) {
        debug(EXCEPTION_MESSAGE, t);
    }

    public abstract bool isInfoEnabled();
    public abstract void info(string msg);
    public abstract void info(string format, object arg);
    public abstract void info(string format, object argA, object argB);
    public abstract void info(string format, params object[] arguments);
    public abstract void info(string msg, Exception t);

    public void info(Exception t) {
        info(EXCEPTION_MESSAGE, t);
    }

    public abstract bool isWarnEnabled();
    public abstract void warn(string msg);
    public abstract void warn(string format, object arg);
    public abstract void warn(string format, params object[] arguments);
    public abstract void warn(string format, object argA, object argB);
    public abstract void warn(string msg, Exception t);

    public void warn(Exception t) {
        warn(EXCEPTION_MESSAGE, t);
    }

    public abstract bool isErrorEnabled();
    public abstract void error(string msg);
    public abstract void error(string format, object arg);
    public abstract void error(string format, object argA, object argB);
    public abstract void error(string format, params object[] arguments);
    public abstract void error(string msg, Exception t);

    public void error(Exception t) {
        error(EXCEPTION_MESSAGE, t);
    }

    public void log(InternalLogLevel level, string msg, Exception cause) {
        switch (level) {
        case InternalLogLevel.TRACE:
            trace(msg, cause);
            break;
        case InternalLogLevel.DEBUG:
            debug(msg, cause);
            break;
        case InternalLogLevel.INFO:
            info(msg, cause);
            break;
        case InternalLogLevel.WARN:
            warn(msg, cause);
            break;
        case InternalLogLevel.ERROR:
            error(msg, cause);
            break;
        default:
            throw new ArgumentOutOfRangeException();
        }
    }

    public void log(InternalLogLevel level, Exception cause) {
        switch (level) {
            case InternalLogLevel.TRACE:
                trace(cause);
                break;
            case InternalLogLevel.DEBUG:
                debug(cause);
                break;
            case InternalLogLevel.INFO:
                info(cause);
                break;
            case InternalLogLevel.WARN:
                warn(cause);
                break;
            case InternalLogLevel.ERROR:
                error(cause);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public void log(InternalLogLevel level, string msg) {
        switch (level) {
        case InternalLogLevel.TRACE:
            trace(msg);
            break;
        case InternalLogLevel.DEBUG:
            debug(msg);
            break;
        case InternalLogLevel.INFO:
            info(msg);
            break;
        case InternalLogLevel.WARN:
            warn(msg);
            break;
        case InternalLogLevel.ERROR:
            error(msg);
            break;
        default:
            throw new ArgumentOutOfRangeException();
        }
    }

    public void log(InternalLogLevel level, string format, object arg) {
        switch (level) {
        case InternalLogLevel.TRACE:
            trace(format, arg);
            break;
        case InternalLogLevel.DEBUG:
            debug(format, arg);
            break;
        case InternalLogLevel.INFO:
            info(format, arg);
            break;
        case InternalLogLevel.WARN:
            warn(format, arg);
            break;
        case InternalLogLevel.ERROR:
            error(format, arg);
            break;
        default:
            throw new ArgumentOutOfRangeException();
        }
    }

    public void log(InternalLogLevel level, string format, object argA, object argB) {
        switch (level) {
        case InternalLogLevel.TRACE:
            trace(format, argA, argB);
            break;
        case InternalLogLevel.DEBUG:
            debug(format, argA, argB);
            break;
        case InternalLogLevel.INFO:
            info(format, argA, argB);
            break;
        case InternalLogLevel.WARN:
            warn(format, argA, argB);
            break;
        case InternalLogLevel.ERROR:
            error(format, argA, argB);
            break;
        default:
            throw new ArgumentOutOfRangeException();
        }
    }

    public void log(InternalLogLevel level, string format, params object[] arguments) {
        switch (level) {
        case InternalLogLevel.TRACE:
            trace(format, arguments);
            break;
        case InternalLogLevel.DEBUG:
            debug(format, arguments);
            break;
        case InternalLogLevel.INFO:
            info(format, arguments);
            break;
        case InternalLogLevel.WARN:
            warn(format, arguments);
            break;
        case InternalLogLevel.ERROR:
            error(format, arguments);
            break;
        default:
            throw new ArgumentOutOfRangeException();
        }
    }

    protected object readResolve() {
        return InternalLoggerFactory.getInstance(name());
    }

    public override string ToString() {
        return StringUtil.simpleClassName(this) + '(' + name() + ')';
    }
}
