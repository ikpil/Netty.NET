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
 * <a href="https://www.slf4j.org/">SLF4J</a> logger.
 */
final class Slf4JLogger extends AbstractInternalLogger {

    private static readonly long serialVersionUID = 108038972685130825L;

    private readonly transient Logger logger;

    Slf4JLogger(Logger logger) {
        super(logger.getName());
        this.logger = logger;
    }

    @Override
    public bool isTraceEnabled() {
        return logger.isTraceEnabled();
    }

    @Override
    public void trace(string msg) {
        logger.trace(msg);
    }

    @Override
    public void trace(string format, object arg) {
        logger.trace(format, arg);
    }

    @Override
    public void trace(string format, object argA, object argB) {
        logger.trace(format, argA, argB);
    }

    @Override
    public void trace(string format, params object[] argArray) {
        logger.trace(format, argArray);
    }

    @Override
    public void trace(string msg, Exception t) {
        logger.trace(msg, t);
    }

    @Override
    public bool isDebugEnabled() {
        return logger.isDebugEnabled();
    }

    @Override
    public void debug(string msg) {
        logger.debug(msg);
    }

    @Override
    public void debug(string format, object arg) {
        logger.debug(format, arg);
    }

    @Override
    public void debug(string format, object argA, object argB) {
        logger.debug(format, argA, argB);
    }

    @Override
    public void debug(string format, params object[] argArray) {
        logger.debug(format, argArray);
    }

    @Override
    public void debug(string msg, Exception t) {
        logger.debug(msg, t);
    }

    @Override
    public bool isInfoEnabled() {
        return logger.isInfoEnabled();
    }

    @Override
    public void info(string msg) {
        logger.info(msg);
    }

    @Override
    public void info(string format, object arg) {
        logger.info(format, arg);
    }

    @Override
    public void info(string format, object argA, object argB) {
        logger.info(format, argA, argB);
    }

    @Override
    public void info(string format, params object[] argArray) {
        logger.info(format, argArray);
    }

    @Override
    public void info(string msg, Exception t) {
        logger.info(msg, t);
    }

    @Override
    public bool isWarnEnabled() {
        return logger.isWarnEnabled();
    }

    @Override
    public void warn(string msg) {
        logger.warn(msg);
    }

    @Override
    public void warn(string format, object arg) {
        logger.warn(format, arg);
    }

    @Override
    public void warn(string format, params object[] argArray) {
        logger.warn(format, argArray);
    }

    @Override
    public void warn(string format, object argA, object argB) {
        logger.warn(format, argA, argB);
    }

    @Override
    public void warn(string msg, Exception t) {
        logger.warn(msg, t);
    }

    @Override
    public bool isErrorEnabled() {
        return logger.isErrorEnabled();
    }

    @Override
    public void error(string msg) {
        logger.error(msg);
    }

    @Override
    public void error(string format, object arg) {
        logger.error(format, arg);
    }

    @Override
    public void error(string format, object argA, object argB) {
        logger.error(format, argA, argB);
    }

    @Override
    public void error(string format, params object[] argArray) {
        logger.error(format, argArray);
    }

    @Override
    public void error(string msg, Exception t) {
        logger.error(msg, t);
    }
}
