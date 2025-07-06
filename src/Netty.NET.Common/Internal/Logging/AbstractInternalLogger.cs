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
 * A skeletal implementation of {@link InternalLogger}.  This class implements
 * all methods that have a {@link InternalLogLevel} parameter by default to call
 * specific logger methods such as {@link #info(string)} or {@link #isInfoEnabled()}.
 */
public abstract class AbstractInternalLogger : IInternalLogger
{

    private static final long serialVersionUID = -6382972526573193470L;

    static final string EXCEPTION_MESSAGE = "Unexpected exception:";

    private final string name;

    /**
     * Creates a new instance.
     */
    protected AbstractInternalLogger(string name) {
        this.name = ObjectUtil.checkNotNull(name, "name");
    }

    @Override
    public string name() {
        return name;
    }

    @Override
    public bool isEnabled(InternalLogLevel level) {
        switch (level) {
        case TRACE:
            return isTraceEnabled();
        case DEBUG:
            return isDebugEnabled();
        case INFO:
            return isInfoEnabled();
        case WARN:
            return isWarnEnabled();
        case ERROR:
            return isErrorEnabled();
        default:
            throw new Error();
        }
    }

    @Override
    public void trace(Exception t) {
        trace(EXCEPTION_MESSAGE, t);
    }

    @Override
    public void debug(Exception t) {
        debug(EXCEPTION_MESSAGE, t);
    }

    @Override
    public void info(Exception t) {
        info(EXCEPTION_MESSAGE, t);
    }

    @Override
    public void warn(Exception t) {
        warn(EXCEPTION_MESSAGE, t);
    }

    @Override
    public void error(Exception t) {
        error(EXCEPTION_MESSAGE, t);
    }

    @Override
    public void log(InternalLogLevel level, string msg, Exception cause) {
        switch (level) {
        case TRACE:
            trace(msg, cause);
            break;
        case DEBUG:
            debug(msg, cause);
            break;
        case INFO:
            info(msg, cause);
            break;
        case WARN:
            warn(msg, cause);
            break;
        case ERROR:
            error(msg, cause);
            break;
        default:
            throw new Error();
        }
    }

    @Override
    public void log(InternalLogLevel level, Exception cause) {
        switch (level) {
            case TRACE:
                trace(cause);
                break;
            case DEBUG:
                debug(cause);
                break;
            case INFO:
                info(cause);
                break;
            case WARN:
                warn(cause);
                break;
            case ERROR:
                error(cause);
                break;
            default:
                throw new Error();
        }
    }

    @Override
    public void log(InternalLogLevel level, string msg) {
        switch (level) {
        case TRACE:
            trace(msg);
            break;
        case DEBUG:
            debug(msg);
            break;
        case INFO:
            info(msg);
            break;
        case WARN:
            warn(msg);
            break;
        case ERROR:
            error(msg);
            break;
        default:
            throw new Error();
        }
    }

    @Override
    public void log(InternalLogLevel level, string format, object arg) {
        switch (level) {
        case TRACE:
            trace(format, arg);
            break;
        case DEBUG:
            debug(format, arg);
            break;
        case INFO:
            info(format, arg);
            break;
        case WARN:
            warn(format, arg);
            break;
        case ERROR:
            error(format, arg);
            break;
        default:
            throw new Error();
        }
    }

    @Override
    public void log(InternalLogLevel level, string format, object argA, object argB) {
        switch (level) {
        case TRACE:
            trace(format, argA, argB);
            break;
        case DEBUG:
            debug(format, argA, argB);
            break;
        case INFO:
            info(format, argA, argB);
            break;
        case WARN:
            warn(format, argA, argB);
            break;
        case ERROR:
            error(format, argA, argB);
            break;
        default:
            throw new Error();
        }
    }

    @Override
    public void log(InternalLogLevel level, string format, params object[] arguments) {
        switch (level) {
        case TRACE:
            trace(format, arguments);
            break;
        case DEBUG:
            debug(format, arguments);
            break;
        case INFO:
            info(format, arguments);
            break;
        case WARN:
            warn(format, arguments);
            break;
        case ERROR:
            error(format, arguments);
            break;
        default:
            throw new Error();
        }
    }

    protected object readResolve() throws ObjectStreamException {
        return InternalLoggerFactory.getInstance(name());
    }

    @Override
    public string toString() {
        return StringUtil.simpleClassName(this) + '(' + name() + ')';
    }
}
