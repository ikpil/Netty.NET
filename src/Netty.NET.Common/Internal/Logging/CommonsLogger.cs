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

/**
 * Copyright (c) 2004-2011 QOS.ch
 * All rights reserved.
 *
 * Permission is hereby granted, free  of charge, to any person obtaining
 * a  copy  of this  software  and  associated  documentation files  (the
 * "Software"), to  deal in  the Software without  restriction, including
 * without limitation  the rights to  use, copy, modify,  merge, publish,
 * distribute,  sublicense, and/or sell  copies of  the Software,  and to
 * permit persons to whom the Software  is furnished to do so, subject to
 * the following conditions:
 *
 * The  above  copyright  notice  and  this permission  notice  shall  be
 * included in all copies or substantial portions of the Software.
 *
 * THE  SOFTWARE IS  PROVIDED  "AS  IS", WITHOUT  WARRANTY  OF ANY  KIND,
 * EXPRESS OR  IMPLIED, INCLUDING  BUT NOT LIMITED  TO THE  WARRANTIES OF
 * MERCHANTABILITY,    FITNESS    FOR    A   PARTICULAR    PURPOSE    AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
 * LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
 * OF CONTRACT, TORT OR OTHERWISE,  ARISING FROM, OUT OF OR IN CONNECTION
 * WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 *
 */


using System;

namespace Netty.NET.Common.Internal.Logging;

/**
 * <a href="https://commons.apache.org/logging/">Apache Commons Logging</a>
 * logger.
 *
 * @deprecated Please use {@link Log4J2Logger} or {@link Log4JLogger} or
 * {@link Slf4JLogger}.
 */
public class CommonsLogger : AbstractInternalLogger
{
    private IInternalLogger logger;

    public CommonsLogger(IInternalLogger logger, string name)
        : base(name)
    {
        this.logger = ObjectUtil.CheckNotNull(logger, "logger");
    }

    /**
     * Delegates to the {@link Log#isTraceEnabled} method of the underlying
     * {@link Log} instance.
     */
    public override bool IsTraceEnabled()
    {
        return logger.IsTraceEnabled();
    }

    /**
     * Delegates to the {@link Log#trace(object)} method of the underlying
     * {@link Log} instance.
     *
     * @param msg - the message object to be logged
     */
    public override void Trace(string msg)
    {
        logger.Trace(msg);
    }

    /**
     * Delegates to the {@link Log#trace(object)} method of the underlying
     * {@link Log} instance.
     *
     * <p>
     * However, this form avoids superfluous object creation when the logger is disabled
     * for level TRACE.
     * </p>
     *
     * @param format
     *          the format string
     * @param arg
     *          the argument
     */
    public override void Trace(string format, object arg)
    {
        if (logger.IsTraceEnabled())
        {
            FormattingTuple ft = MessageFormatter.Format(format, arg);
            logger.Trace(ft.GetMessage(), ft.GetThrowable());
        }
    }

    /**
     * Delegates to the {@link Log#trace(object)} method of the underlying
     * {@link Log} instance.
     *
     * <p>
     * However, this form avoids superfluous object creation when the logger is disabled
     * for level TRACE.
     * </p>
     *
     * @param format
     *          the format string
     * @param argA
     *          the first argument
     * @param argB
     *          the second argument
     */
    public override void Trace(string format, object argA, object argB)
    {
        if (logger.IsTraceEnabled())
        {
            FormattingTuple ft = MessageFormatter.Format(format, argA, argB);
            logger.Trace(ft.GetMessage(), ft.GetThrowable());
        }
    }

    /**
     * Delegates to the {@link Log#trace(object)} method of the underlying
     * {@link Log} instance.
     *
     * <p>
     * However, this form avoids superfluous object creation when the logger is disabled
     * for level TRACE.
     * </p>
     *
     * @param format the format string
     * @param arguments a list of 3 or more arguments
     */
    public override void Trace(string format, params object[] arguments)
    {
        if (logger.IsTraceEnabled())
        {
            FormattingTuple ft = MessageFormatter.ArrayFormat(format, arguments);
            logger.Trace(ft.GetMessage(), ft.GetThrowable());
        }
    }

    /**
     * Delegates to the {@link Log#trace(object, Exception)} method of
     * the underlying {@link Log} instance.
     *
     * @param msg
     *          the message accompanying the exception
     * @param t
     *          the exception (throwable) to log
     */
    public override void Trace(string msg, Exception t)
    {
        logger.Trace(msg, t);
    }

    /**
     * Delegates to the {@link Log#isDebugEnabled} method of the underlying
     * {@link Log} instance.
     */
    public override bool IsDebugEnabled()
    {
        return logger.IsDebugEnabled();
    }

    //

    /**
     * Delegates to the {@link Log#debug(object)} method of the underlying
     * {@link Log} instance.
     *
     * @param msg - the message object to be logged
     */
    public override void Debug(string msg)
    {
        logger.Debug(msg);
    }

    /**
     * Delegates to the {@link Log#debug(object)} method of the underlying
     * {@link Log} instance.
     *
     * <p>
     * However, this form avoids superfluous object creation when the logger is disabled
     * for level DEBUG.
     * </p>
     *
     * @param format
     *          the format string
     * @param arg
     *          the argument
     */
    public override void Debug(string format, object arg)
    {
        if (logger.IsDebugEnabled())
        {
            FormattingTuple ft = MessageFormatter.Format(format, arg);
            logger.Debug(ft.GetMessage(), ft.GetThrowable());
        }
    }

    /**
     * Delegates to the {@link Log#debug(object)} method of the underlying
     * {@link Log} instance.
     *
     * <p>
     * However, this form avoids superfluous object creation when the logger is disabled
     * for level DEBUG.
     * </p>
     *
     * @param format
     *          the format string
     * @param argA
     *          the first argument
     * @param argB
     *          the second argument
     */
    public override void Debug(string format, object argA, object argB)
    {
        if (logger.IsDebugEnabled())
        {
            FormattingTuple ft = MessageFormatter.Format(format, argA, argB);
            logger.Debug(ft.GetMessage(), ft.GetThrowable());
        }
    }


    /**
     * Delegates to the {@link Log#debug(object)} method of the underlying
     * {@link Log} instance.
     *
     * <p>
     * However, this form avoids superfluous object creation when the logger is disabled
     * for level DEBUG.
     * </p>
     *
     * @param format the format string
     * @param arguments a list of 3 or more arguments
     */
    public override void Debug(string format, params object[] arguments)
    {
        if (logger.IsDebugEnabled())
        {
            FormattingTuple ft = MessageFormatter.ArrayFormat(format, arguments);
            logger.Debug(ft.GetMessage(), ft.GetThrowable());
        }
    }

    /**
     * Delegates to the {@link Log#debug(object, Exception)} method of
     * the underlying {@link Log} instance.
     *
     * @param msg
     *          the message accompanying the exception
     * @param t
     *          the exception (throwable) to log
     */
    public override void Debug(string msg, Exception t)
    {
        logger.Debug(msg, t);
    }

    /**
     * Delegates to the {@link Log#isInfoEnabled} method of the underlying
     * {@link Log} instance.
     */
    public override bool IsInfoEnabled()
    {
        return logger.IsInfoEnabled();
    }

    /**
     * Delegates to the {@link Log#debug(object)} method of the underlying
     * {@link Log} instance.
     *
     * @param msg - the message object to be logged
     */
    public override void Info(string msg)
    {
        logger.Info(msg);
    }

    /**
     * Delegates to the {@link Log#info(object)} method of the underlying
     * {@link Log} instance.
     *
     * <p>
     * However, this form avoids superfluous object creation when the logger is disabled
     * for level INFO.
     * </p>
     *
     * @param format
     *          the format string
     * @param arg
     *          the argument
     */
    public override void Info(string format, object arg)
    {
        if (logger.IsInfoEnabled())
        {
            FormattingTuple ft = MessageFormatter.Format(format, arg);
            logger.Info(ft.GetMessage(), ft.GetThrowable());
        }
    }

    /**
     * Delegates to the {@link Log#info(object)} method of the underlying
     * {@link Log} instance.
     *
     * <p>
     * However, this form avoids superfluous object creation when the logger is disabled
     * for level INFO.
     * </p>
     *
     * @param format
     *          the format string
     * @param argA
     *          the first argument
     * @param argB
     *          the second argument
     */
    public override void Info(string format, object argA, object argB)
    {
        if (logger.IsInfoEnabled())
        {
            FormattingTuple ft = MessageFormatter.Format(format, argA, argB);
            logger.Info(ft.GetMessage(), ft.GetThrowable());
        }
    }

    /**
     * Delegates to the {@link Log#info(object)} method of the underlying
     * {@link Log} instance.
     *
     * <p>
     * However, this form avoids superfluous object creation when the logger is disabled
     * for level INFO.
     * </p>
     *
     * @param format the format string
     * @param arguments a list of 3 or more arguments
     */
    public override void Info(string format, params object[] arguments)
    {
        if (logger.IsInfoEnabled())
        {
            FormattingTuple ft = MessageFormatter.ArrayFormat(format, arguments);
            logger.Info(ft.GetMessage(), ft.GetThrowable());
        }
    }

    /**
     * Delegates to the {@link Log#info(object, Exception)} method of
     * the underlying {@link Log} instance.
     *
     * @param msg
     *          the message accompanying the exception
     * @param t
     *          the exception (throwable) to log
     */
    public override void Info(string msg, Exception t)
    {
        logger.Info(msg, t);
    }

    /**
     * Delegates to the {@link Log#isWarnEnabled} method of the underlying
     * {@link Log} instance.
     */
    public override bool IsWarnEnabled()
    {
        return logger.IsWarnEnabled();
    }

    /**
     * Delegates to the {@link Log#warn(object)} method of the underlying
     * {@link Log} instance.
     *
     * @param msg - the message object to be logged
     */
    public override void Warn(string msg)
    {
        logger.Warn(msg);
    }

    /**
     * Delegates to the {@link Log#warn(object)} method of the underlying
     * {@link Log} instance.
     *
     * <p>
     * However, this form avoids superfluous object creation when the logger is disabled
     * for level WARN.
     * </p>
     *
     * @param format
     *          the format string
     * @param arg
     *          the argument
     */
    public override void Warn(string format, object arg)
    {
        if (logger.IsWarnEnabled())
        {
            FormattingTuple ft = MessageFormatter.Format(format, arg);
            logger.Warn(ft.GetMessage(), ft.GetThrowable());
        }
    }

    /**
     * Delegates to the {@link Log#warn(object)} method of the underlying
     * {@link Log} instance.
     *
     * <p>
     * However, this form avoids superfluous object creation when the logger is disabled
     * for level WARN.
     * </p>
     *
     * @param format
     *          the format string
     * @param argA
     *          the first argument
     * @param argB
     *          the second argument
     */
    public override void Warn(string format, object argA, object argB)
    {
        if (logger.IsWarnEnabled())
        {
            FormattingTuple ft = MessageFormatter.Format(format, argA, argB);
            logger.Warn(ft.GetMessage(), ft.GetThrowable());
        }
    }

    /**
     * Delegates to the {@link Log#warn(object)} method of the underlying
     * {@link Log} instance.
     *
     * <p>
     * However, this form avoids superfluous object creation when the logger is disabled
     * for level WARN.
     * </p>
     *
     * @param format the format string
     * @param arguments a list of 3 or more arguments
     */
    public override void Warn(string format, params object[] arguments)
    {
        if (logger.IsWarnEnabled())
        {
            FormattingTuple ft = MessageFormatter.ArrayFormat(format, arguments);
            logger.Warn(ft.GetMessage(), ft.GetThrowable());
        }
    }

    /**
     * Delegates to the {@link Log#warn(object, Exception)} method of
     * the underlying {@link Log} instance.
     *
     * @param msg
     *          the message accompanying the exception
     * @param t
     *          the exception (throwable) to log
     */
    public override void Warn(string msg, Exception t)
    {
        logger.Warn(msg, t);
    }

    /**
     * Delegates to the {@link Log#isErrorEnabled} method of the underlying
     * {@link Log} instance.
     */
    public override bool IsErrorEnabled()
    {
        return logger.IsErrorEnabled();
    }

    /**
     * Delegates to the {@link Log#error(object)} method of the underlying
     * {@link Log} instance.
     *
     * @param msg - the message object to be logged
     */
    public override void Error(string msg)
    {
        logger.Error(msg);
    }

    /**
     * Delegates to the {@link Log#error(object)} method of the underlying
     * {@link Log} instance.
     *
     * <p>
     * However, this form avoids superfluous object creation when the logger is disabled
     * for level ERROR.
     * </p>
     *
     * @param format
     *          the format string
     * @param arg
     *          the argument
     */
    public override void Error(string format, object arg)
    {
        if (logger.IsErrorEnabled())
        {
            FormattingTuple ft = MessageFormatter.Format(format, arg);
            logger.Error(ft.GetMessage(), ft.GetThrowable());
        }
    }

    /**
     * Delegates to the {@link Log#error(object)} method of the underlying
     * {@link Log} instance.
     *
     * <p>
     * However, this form avoids superfluous object creation when the logger is disabled
     * for level ERROR.
     * </p>
     *
     * @param format
     *          the format string
     * @param argA
     *          the first argument
     * @param argB
     *          the second argument
     */
    public override void Error(string format, object argA, object argB)
    {
        if (logger.IsErrorEnabled())
        {
            FormattingTuple ft = MessageFormatter.Format(format, argA, argB);
            logger.Error(ft.GetMessage(), ft.GetThrowable());
        }
    }

    /**
     * Delegates to the {@link Log#error(object)} method of the underlying
     * {@link Log} instance.
     *
     * <p>
     * However, this form avoids superfluous object creation when the logger is disabled
     * for level ERROR.
     * </p>
     *
     * @param format the format string
     * @param arguments a list of 3 or more arguments
     */
    public override void Error(string format, params object[] arguments)
    {
        if (logger.IsErrorEnabled())
        {
            FormattingTuple ft = MessageFormatter.ArrayFormat(format, arguments);
            logger.Error(ft.GetMessage(), ft.GetThrowable());
        }
    }

    /**
     * Delegates to the {@link Log#error(object, Exception)} method of
     * the underlying {@link Log} instance.
     *
     * @param msg
     *          the message accompanying the exception
     * @param t
     *          the exception (throwable) to log
     */
    public override void Error(string msg, Exception t)
    {
        logger.Error(msg, t);
    }
}