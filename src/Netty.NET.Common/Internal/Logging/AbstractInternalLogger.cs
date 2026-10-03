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
 * A skeletal implementation of {@link IInternalLogger}.  This class implements
 * all methods that have a {@link InternalLogLevel} parameter by default to call
 * specific logger methods such as {@link #info(string)} or {@link #isInfoEnabled()}.
 */
public abstract class AbstractInternalLogger : IInternalLogger
{
    protected static readonly string EXCEPTION_MESSAGE = "Unexpected exception:";

    private readonly string _name;

    /**
     * Creates a new instance.
     */
    protected AbstractInternalLogger(string name)
    {
        _name = ObjectUtil.CheckNotNull(name, "name");
    }

    public string Name()
    {
        return _name;
    }

    public abstract bool IsTraceEnabled();
    public abstract void Trace(string msg);
    public abstract void Trace(string format, object arg);
    public abstract void Trace(string format, object argA, object argB);
    public abstract void Trace(string format, params object[] arguments);
    public abstract void Trace(string msg, Exception t);

    public bool IsEnabled(InternalLogLevel level)
    {
        switch (level)
        {
            case InternalLogLevel.TRACE:
                return IsTraceEnabled();
            case InternalLogLevel.DEBUG:
                return IsDebugEnabled();
            case InternalLogLevel.INFO:
                return IsInfoEnabled();
            case InternalLogLevel.WARN:
                return IsWarnEnabled();
            case InternalLogLevel.ERROR:
                return IsErrorEnabled();
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public void Trace(Exception t)
    {
        Trace(EXCEPTION_MESSAGE, t);
    }

    public abstract bool IsDebugEnabled();
    public abstract void Debug(string msg);
    public abstract void Debug(string format, object arg);
    public abstract void Debug(string format, object argA, object argB);
    public abstract void Debug(string format, params object[] arguments);
    public abstract void Debug(string msg, Exception t);

    public void Debug(Exception t)
    {
        Debug(EXCEPTION_MESSAGE, t);
    }

    public abstract bool IsInfoEnabled();
    public abstract void Info(string msg);
    public abstract void Info(string format, object arg);
    public abstract void Info(string format, object argA, object argB);
    public abstract void Info(string format, params object[] arguments);
    public abstract void Info(string msg, Exception t);

    public void Info(Exception t)
    {
        Info(EXCEPTION_MESSAGE, t);
    }

    public abstract bool IsWarnEnabled();
    public abstract void Warn(string msg);
    public abstract void Warn(string format, object arg);
    public abstract void Warn(string format, params object[] arguments);
    public abstract void Warn(string format, object argA, object argB);
    public abstract void Warn(string msg, Exception t);

    public void Warn(Exception t)
    {
        Warn(EXCEPTION_MESSAGE, t);
    }

    public abstract bool IsErrorEnabled();
    public abstract void Error(string msg);
    public abstract void Error(string format, object arg);
    public abstract void Error(string format, object argA, object argB);
    public abstract void Error(string format, params object[] arguments);
    public abstract void Error(string msg, Exception t);

    public void Error(Exception t)
    {
        Error(EXCEPTION_MESSAGE, t);
    }

    public void Log(InternalLogLevel level, string msg, Exception cause)
    {
        switch (level)
        {
            case InternalLogLevel.TRACE:
                Trace(msg, cause);
                break;
            case InternalLogLevel.DEBUG:
                Debug(msg, cause);
                break;
            case InternalLogLevel.INFO:
                Info(msg, cause);
                break;
            case InternalLogLevel.WARN:
                Warn(msg, cause);
                break;
            case InternalLogLevel.ERROR:
                Error(msg, cause);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public void Log(InternalLogLevel level, Exception cause)
    {
        switch (level)
        {
            case InternalLogLevel.TRACE:
                Trace(cause);
                break;
            case InternalLogLevel.DEBUG:
                Debug(cause);
                break;
            case InternalLogLevel.INFO:
                Info(cause);
                break;
            case InternalLogLevel.WARN:
                Warn(cause);
                break;
            case InternalLogLevel.ERROR:
                Error(cause);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public void Log(InternalLogLevel level, string msg)
    {
        switch (level)
        {
            case InternalLogLevel.TRACE:
                Trace(msg);
                break;
            case InternalLogLevel.DEBUG:
                Debug(msg);
                break;
            case InternalLogLevel.INFO:
                Info(msg);
                break;
            case InternalLogLevel.WARN:
                Warn(msg);
                break;
            case InternalLogLevel.ERROR:
                Error(msg);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public void Log(InternalLogLevel level, string format, object arg)
    {
        switch (level)
        {
            case InternalLogLevel.TRACE:
                Trace(format, arg);
                break;
            case InternalLogLevel.DEBUG:
                Debug(format, arg);
                break;
            case InternalLogLevel.INFO:
                Info(format, arg);
                break;
            case InternalLogLevel.WARN:
                Warn(format, arg);
                break;
            case InternalLogLevel.ERROR:
                Error(format, arg);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public void Log(InternalLogLevel level, string format, object argA, object argB)
    {
        switch (level)
        {
            case InternalLogLevel.TRACE:
                Trace(format, argA, argB);
                break;
            case InternalLogLevel.DEBUG:
                Debug(format, argA, argB);
                break;
            case InternalLogLevel.INFO:
                Info(format, argA, argB);
                break;
            case InternalLogLevel.WARN:
                Warn(format, argA, argB);
                break;
            case InternalLogLevel.ERROR:
                Error(format, argA, argB);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public void Log(InternalLogLevel level, string format, params object[] arguments)
    {
        switch (level)
        {
            case InternalLogLevel.TRACE:
                Trace(format, arguments);
                break;
            case InternalLogLevel.DEBUG:
                Debug(format, arguments);
                break;
            case InternalLogLevel.INFO:
                Info(format, arguments);
                break;
            case InternalLogLevel.WARN:
                Warn(format, arguments);
                break;
            case InternalLogLevel.ERROR:
                Error(format, arguments);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    protected object ReadResolve()
    {
        return InternalLoggerFactory.GetInstance(Name());
    }

    public override string ToString()
    {
        return StringUtil.SimpleClassName(this) + '(' + Name() + ')';
    }
}