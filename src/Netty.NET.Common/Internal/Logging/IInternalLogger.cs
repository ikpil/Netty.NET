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
 * <em>Internal-use-only</em> logger used by Netty.  <strong>DO NOT</strong>
 * access this class outside of Netty.
 */
public interface IInternalLogger
{
    /**
     * Return the name of this {@link IInternalLogger} instance.
     *
     * @return name of this logger instance
     */
    string Name();

    /**
     * Is the logger instance enabled for the TRACE level?
     *
     * @return True if this Logger is enabled for the TRACE level,
     *         false otherwise.
     */
    bool IsTraceEnabled();

    /**
     * Log a message at the TRACE level.
     *
     * @param msg the message string to be logged
     */
    void Trace(string msg);

    /**
     * Log a message at the TRACE level according to the specified format
     * and argument.
     * <p/>
     * <p>This form avoids superfluous object creation when the logger
     * is disabled for the TRACE level. </p>
     *
     * @param format the format string
     * @param arg    the argument
     */
    void Trace(string format, object arg);

    /**
     * Log a message at the TRACE level according to the specified format
     * and arguments.
     * <p/>
     * <p>This form avoids superfluous object creation when the logger
     * is disabled for the TRACE level. </p>
     *
     * @param format the format string
     * @param argA   the first argument
     * @param argB   the second argument
     */
    void Trace(string format, object argA, object argB);

    /**
     * Log a message at the TRACE level according to the specified format
     * and arguments.
     * <p/>
     * <p>This form avoids superfluous string concatenation when the logger
     * is disabled for the TRACE level. However, this variant incurs the hidden
     * (and relatively small) cost of creating an {@code object[]} before invoking the method,
     * even if this logger is disabled for TRACE. The variants taking {@link #trace(string, object) one} and
     * {@link #trace(string, object, object) two} arguments exist solely in order to avoid this hidden cost.</p>
     *
     * @param format    the format string
     * @param arguments a list of 3 or more arguments
     */
    void Trace(string format, params object[] arguments);

    /**
     * Log an exception (throwable) at the TRACE level with an
     * accompanying message.
     *
     * @param msg the message accompanying the exception
     * @param t   the exception (throwable) to log
     */
    void Trace(string msg, Exception t);

    /**
     * Log an exception (throwable) at the TRACE level.
     *
     * @param t   the exception (throwable) to log
     */
    void Trace(Exception t);

    /**
     * Is the logger instance enabled for the DEBUG level?
     *
     * @return True if this Logger is enabled for the DEBUG level,
     *         false otherwise.
     */
    bool IsDebugEnabled();

    /**
     * Log a message at the DEBUG level.
     *
     * @param msg the message string to be logged
     */
    void Debug(string msg);

    /**
     * Log a message at the DEBUG level according to the specified format
     * and argument.
     * <p/>
     * <p>This form avoids superfluous object creation when the logger
     * is disabled for the DEBUG level. </p>
     *
     * @param format the format string
     * @param arg    the argument
     */
    void Debug(string format, object arg);

    /**
     * Log a message at the DEBUG level according to the specified format
     * and arguments.
     * <p/>
     * <p>This form avoids superfluous object creation when the logger
     * is disabled for the DEBUG level. </p>
     *
     * @param format the format string
     * @param argA   the first argument
     * @param argB   the second argument
     */
    void Debug(string format, object argA, object argB);

    /**
     * Log a message at the DEBUG level according to the specified format
     * and arguments.
     * <p/>
     * <p>This form avoids superfluous string concatenation when the logger
     * is disabled for the DEBUG level. However, this variant incurs the hidden
     * (and relatively small) cost of creating an {@code object[]} before invoking the method,
     * even if this logger is disabled for DEBUG. The variants taking
     * {@link #debug(string, object) one} and {@link #debug(string, object, object) two}
     * arguments exist solely in order to avoid this hidden cost.</p>
     *
     * @param format    the format string
     * @param arguments a list of 3 or more arguments
     */
    void Debug(string format, params object[] arguments);

    /**
     * Log an exception (throwable) at the DEBUG level with an
     * accompanying message.
     *
     * @param msg the message accompanying the exception
     * @param t   the exception (throwable) to log
     */
    void Debug(string msg, Exception t);

    /**
     * Log an exception (throwable) at the DEBUG level.
     *
     * @param t   the exception (throwable) to log
     */
    void Debug(Exception t);

    /**
     * Is the logger instance enabled for the INFO level?
     *
     * @return True if this Logger is enabled for the INFO level,
     *         false otherwise.
     */
    bool IsInfoEnabled();

    /**
     * Log a message at the INFO level.
     *
     * @param msg the message string to be logged
     */
    void Info(string msg);

    /**
     * Log a message at the INFO level according to the specified format
     * and argument.
     * <p/>
     * <p>This form avoids superfluous object creation when the logger
     * is disabled for the INFO level. </p>
     *
     * @param format the format string
     * @param arg    the argument
     */
    void Info(string format, object arg);

    /**
     * Log a message at the INFO level according to the specified format
     * and arguments.
     * <p/>
     * <p>This form avoids superfluous object creation when the logger
     * is disabled for the INFO level. </p>
     *
     * @param format the format string
     * @param argA   the first argument
     * @param argB   the second argument
     */
    void Info(string format, object argA, object argB);

    /**
     * Log a message at the INFO level according to the specified format
     * and arguments.
     * <p/>
     * <p>This form avoids superfluous string concatenation when the logger
     * is disabled for the INFO level. However, this variant incurs the hidden
     * (and relatively small) cost of creating an {@code object[]} before invoking the method,
     * even if this logger is disabled for INFO. The variants taking
     * {@link #info(string, object) one} and {@link #info(string, object, object) two}
     * arguments exist solely in order to avoid this hidden cost.</p>
     *
     * @param format    the format string
     * @param arguments a list of 3 or more arguments
     */
    void Info(string format, params object[] arguments);

    /**
     * Log an exception (throwable) at the INFO level with an
     * accompanying message.
     *
     * @param msg the message accompanying the exception
     * @param t   the exception (throwable) to log
     */
    void Info(string msg, Exception t);

    /**
     * Log an exception (throwable) at the INFO level.
     *
     * @param t   the exception (throwable) to log
     */
    void Info(Exception t);

    /**
     * Is the logger instance enabled for the WARN level?
     *
     * @return True if this Logger is enabled for the WARN level,
     *         false otherwise.
     */
    bool IsWarnEnabled();

    /**
     * Log a message at the WARN level.
     *
     * @param msg the message string to be logged
     */
    void Warn(string msg);

    /**
     * Log a message at the WARN level according to the specified format
     * and argument.
     * <p/>
     * <p>This form avoids superfluous object creation when the logger
     * is disabled for the WARN level. </p>
     *
     * @param format the format string
     * @param arg    the argument
     */
    void Warn(string format, object arg);

    /**
     * Log a message at the WARN level according to the specified format
     * and arguments.
     * <p/>
     * <p>This form avoids superfluous string concatenation when the logger
     * is disabled for the WARN level. However, this variant incurs the hidden
     * (and relatively small) cost of creating an {@code object[]} before invoking the method,
     * even if this logger is disabled for WARN. The variants taking
     * {@link #warn(string, object) one} and {@link #warn(string, object, object) two}
     * arguments exist solely in order to avoid this hidden cost.</p>
     *
     * @param format    the format string
     * @param arguments a list of 3 or more arguments
     */
    void Warn(string format, params object[] arguments);

    /**
     * Log a message at the WARN level according to the specified format
     * and arguments.
     * <p/>
     * <p>This form avoids superfluous object creation when the logger
     * is disabled for the WARN level. </p>
     *
     * @param format the format string
     * @param argA   the first argument
     * @param argB   the second argument
     */
    void Warn(string format, object argA, object argB);

    /**
     * Log an exception (throwable) at the WARN level with an
     * accompanying message.
     *
     * @param msg the message accompanying the exception
     * @param t   the exception (throwable) to log
     */
    void Warn(string msg, Exception t);

    /**
     * Log an exception (throwable) at the WARN level.
     *
     * @param t   the exception (throwable) to log
     */
    void Warn(Exception t);

    /**
     * Is the logger instance enabled for the ERROR level?
     *
     * @return True if this Logger is enabled for the ERROR level,
     *         false otherwise.
     */
    bool IsErrorEnabled();

    /**
     * Log a message at the ERROR level.
     *
     * @param msg the message string to be logged
     */
    void Error(string msg);

    /**
     * Log a message at the ERROR level according to the specified format
     * and argument.
     * <p/>
     * <p>This form avoids superfluous object creation when the logger
     * is disabled for the ERROR level. </p>
     *
     * @param format the format string
     * @param arg    the argument
     */
    void Error(string format, object arg);

    /**
     * Log a message at the ERROR level according to the specified format
     * and arguments.
     * <p/>
     * <p>This form avoids superfluous object creation when the logger
     * is disabled for the ERROR level. </p>
     *
     * @param format the format string
     * @param argA   the first argument
     * @param argB   the second argument
     */
    void Error(string format, object argA, object argB);

    /**
     * Log a message at the ERROR level according to the specified format
     * and arguments.
     * <p/>
     * <p>This form avoids superfluous string concatenation when the logger
     * is disabled for the ERROR level. However, this variant incurs the hidden
     * (and relatively small) cost of creating an {@code object[]} before invoking the method,
     * even if this logger is disabled for ERROR. The variants taking
     * {@link #error(string, object) one} and {@link #error(string, object, object) two}
     * arguments exist solely in order to avoid this hidden cost.</p>
     *
     * @param format    the format string
     * @param arguments a list of 3 or more arguments
     */
    void Error(string format, params object[] arguments);

    /**
     * Log an exception (throwable) at the ERROR level with an
     * accompanying message.
     *
     * @param msg the message accompanying the exception
     * @param t   the exception (throwable) to log
     */
    void Error(string msg, Exception t);

    /**
     * Log an exception (throwable) at the ERROR level.
     *
     * @param t   the exception (throwable) to log
     */
    void Error(Exception t);

    /**
     * Is the logger instance enabled for the specified {@code level}?
     *
     * @return True if this Logger is enabled for the specified {@code level},
     *         false otherwise.
     */
    bool IsEnabled(InternalLogLevel level);

    /**
     * Log a message at the specified {@code level}.
     *
     * @param msg the message string to be logged
     */
    void Log(InternalLogLevel level, string msg);

    /**
     * Log a message at the specified {@code level} according to the specified format
     * and argument.
     * <p/>
     * <p>This form avoids superfluous object creation when the logger
     * is disabled for the specified {@code level}. </p>
     *
     * @param format the format string
     * @param arg    the argument
     */
    void Log(InternalLogLevel level, string format, object arg);

    /**
     * Log a message at the specified {@code level} according to the specified format
     * and arguments.
     * <p/>
     * <p>This form avoids superfluous object creation when the logger
     * is disabled for the specified {@code level}. </p>
     *
     * @param format the format string
     * @param argA   the first argument
     * @param argB   the second argument
     */
    void Log(InternalLogLevel level, string format, object argA, object argB);

    /**
     * Log a message at the specified {@code level} according to the specified format
     * and arguments.
     * <p/>
     * <p>This form avoids superfluous string concatenation when the logger
     * is disabled for the specified {@code level}. However, this variant incurs the hidden
     * (and relatively small) cost of creating an {@code object[]} before invoking the method,
     * even if this logger is disabled for the specified {@code level}. The variants taking
     * {@link #log(InternalLogLevel, string, object) one} and
     * {@link #log(InternalLogLevel, string, object, object) two} arguments exist solely
     * in order to avoid this hidden cost.</p>
     *
     * @param format    the format string
     * @param arguments a list of 3 or more arguments
     */
    void Log(InternalLogLevel level, string format, params object[] arguments);

    /**
     * Log an exception (throwable) at the specified {@code level} with an
     * accompanying message.
     *
     * @param msg the message accompanying the exception
     * @param t   the exception (throwable) to log
     */
    void Log(InternalLogLevel level, string msg, Exception t);

    /**
     * Log an exception (throwable) at the specified {@code level}.
     *
     * @param t   the exception (throwable) to log
     */
    void Log(InternalLogLevel level, Exception t);
}