/*
 * Copyright 2013 The Netty Project
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
using System.Threading;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common;


/**
 * Collection of method to handle objects that may implement {@link IReferenceCounted}.
 */
public sealed class ReferenceCountUtil 
{

    private static readonly IInternalLogger logger = InternalLoggerFactory.getInstance(typeof(ReferenceCountUtil));

    static {
        ResourceLeakDetector.addExclusions(typeof(ReferenceCountUtil), "touch");
    }

    /**
     * Try to call {@link IReferenceCounted#retain()} if the specified message implements {@link IReferenceCounted}.
     * If the specified message doesn't implement {@link IReferenceCounted}, this method does nothing.
     */
    //@SuppressWarnings("unchecked")
    public static <T> T retain(T msg) {
        if (msg instanceof IReferenceCounted) {
            return (T) ((IReferenceCounted) msg).retain();
        }
        return msg;
    }

    /**
     * Try to call {@link IReferenceCounted#retain(int)} if the specified message implements {@link IReferenceCounted}.
     * If the specified message doesn't implement {@link IReferenceCounted}, this method does nothing.
     */
    //@SuppressWarnings("unchecked")
    public static <T> T retain(T msg, int increment) {
        ObjectUtil.checkPositive(increment, "increment");
        if (msg instanceof IReferenceCounted) {
            return (T) ((IReferenceCounted) msg).retain(increment);
        }
        return msg;
    }

    /**
     * Tries to call {@link IReferenceCounted#touch()} if the specified message implements {@link IReferenceCounted}.
     * If the specified message doesn't implement {@link IReferenceCounted}, this method does nothing.
     */
    //@SuppressWarnings("unchecked")
    public static <T> T touch(T msg) {
        if (msg instanceof IReferenceCounted) {
            return (T) ((IReferenceCounted) msg).touch();
        }
        return msg;
    }

    /**
     * Tries to call {@link IReferenceCounted#touch(object)} if the specified message implements
     * {@link IReferenceCounted}.  If the specified message doesn't implement {@link IReferenceCounted},
     * this method does nothing.
     */
    //@SuppressWarnings("unchecked")
    public static <T> T touch(T msg, object hint) {
        if (msg instanceof IReferenceCounted) {
            return (T) ((IReferenceCounted) msg).touch(hint);
        }
        return msg;
    }

    /**
     * Try to call {@link IReferenceCounted#release()} if the specified message implements {@link IReferenceCounted}.
     * If the specified message doesn't implement {@link IReferenceCounted}, this method does nothing.
     */
    public static bool release(object msg) {
        if (msg instanceof IReferenceCounted) {
            return ((IReferenceCounted) msg).release();
        }
        return false;
    }

    /**
     * Try to call {@link IReferenceCounted#release(int)} if the specified message implements {@link IReferenceCounted}.
     * If the specified message doesn't implement {@link IReferenceCounted}, this method does nothing.
     */
    public static bool release(object msg, int decrement) {
        ObjectUtil.checkPositive(decrement, "decrement");
        if (msg instanceof IReferenceCounted) {
            return ((IReferenceCounted) msg).release(decrement);
        }
        return false;
    }

    /**
     * Try to call {@link IReferenceCounted#release()} if the specified message implements {@link IReferenceCounted}.
     * If the specified message doesn't implement {@link IReferenceCounted}, this method does nothing.
     * Unlike {@link #release(object)} this method catches an exception raised by {@link IReferenceCounted#release()}
     * and logs it, rather than rethrowing it to the caller.  It is usually recommended to use {@link #release(object)}
     * instead, unless you absolutely need to swallow an exception.
     */
    public static void safeRelease(object msg) {
        try {
            release(msg);
        } catch (Exception t) {
            logger.warn("Failed to release a message: {}", msg, t);
        }
    }

    /**
     * Try to call {@link IReferenceCounted#release(int)} if the specified message implements {@link IReferenceCounted}.
     * If the specified message doesn't implement {@link IReferenceCounted}, this method does nothing.
     * Unlike {@link #release(object)} this method catches an exception raised by {@link IReferenceCounted#release(int)}
     * and logs it, rather than rethrowing it to the caller.  It is usually recommended to use
     * {@link #release(object, int)} instead, unless you absolutely need to swallow an exception.
     */
    public static void safeRelease(object msg, int decrement) {
        try {
            ObjectUtil.checkPositive(decrement, "decrement");
            release(msg, decrement);
        } catch (Exception t) {
            if (logger.isWarnEnabled()) {
                logger.warn("Failed to release a message: {} (decrement: {})", msg, decrement, t);
            }
        }
    }

    /**
     * Schedules the specified object to be released when the caller thread terminates. Note that this operation is
     * intended to simplify reference counting of ephemeral objects during unit tests. Do not use it beyond the
     * intended use case.
     *
     * @deprecated this may introduce a lot of memory usage so it is generally preferable to manually release objects.
     */
    [Obsolete]
    public static <T> T releaseLater(T msg) {
        return releaseLater(msg, 1);
    }

    /**
     * Schedules the specified object to be released when the caller thread terminates. Note that this operation is
     * intended to simplify reference counting of ephemeral objects during unit tests. Do not use it beyond the
     * intended use case.
     *
     * @deprecated this may introduce a lot of memory usage so it is generally preferable to manually release objects.
     */
    [Obsolete]
    public static <T> T releaseLater(T msg, int decrement) {
        ObjectUtil.checkPositive(decrement, "decrement");
        if (msg instanceof IReferenceCounted) {
            ThreadDeathWatcher.watch(Thread.CurrentThread, new ReleasingTask((IReferenceCounted) msg, decrement));
        }
        return msg;
    }

    /**
     * Returns reference count of a {@link IReferenceCounted} object. If object is not type of
     * {@link IReferenceCounted}, {@code -1} is returned.
     */
    public static int refCnt(object msg) {
        return msg instanceof IReferenceCounted ? ((IReferenceCounted) msg).refCnt() : -1;
    }

    /**
     * Releases the objects when the thread that called {@link #releaseLater(object)} has been terminated.
     */
    private static readonly class ReleasingTask : IRunnable {

        private readonly IReferenceCounted obj;
        private readonly int decrement;

        ReleasingTask(IReferenceCounted obj, int decrement) {
            this.obj = obj;
            this.decrement = decrement;
        }

        @Override
        public void run() {
            try {
                if (!obj.release(decrement)) {
                    logger.warn("Non-zero refCnt: {}", this);
                } else {
                    logger.debug("Released: {}", this);
                }
            } catch (Exception ex) {
                logger.warn("Failed to release an object: {}", obj, ex);
            }
        }

        @Override
        public string toString() {
            return StringUtil.simpleClassName(obj) + ".release(" + decrement + ") refCnt: " + obj.refCnt();
        }
    }

    private ReferenceCountUtil() { }
}
