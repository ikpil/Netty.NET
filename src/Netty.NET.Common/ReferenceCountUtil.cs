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
 * Collection of method to handle objects that may implement {@link ReferenceCounted}.
 */
public static class ReferenceCountUtil
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(ReferenceCountUtil));
    static ReferenceCountUtil() => ResourceLeakDetector.AddExclusions(typeof(ReferenceCountUtil), nameof(Touch));
    /**
     * Try to call {@link ReferenceCounted#retain()} if the specified message implements {@link ReferenceCounted}.
     * If the specified message doesn't implement {@link ReferenceCounted}, this method does nothing.
     */
    public static T Retain<T>(T msg) => msg is IReferenceCounted reference ? (T)reference.Retain() : msg;
    /**
     * Try to call {@link ReferenceCounted#retain(int)} if the specified message implements {@link ReferenceCounted}.
     * If the specified message doesn't implement {@link ReferenceCounted}, this method does nothing.
     */
    public static T Retain<T>(T msg, int increment)
    {
        ObjectUtil.CheckPositive(increment, "increment");
        return msg is IReferenceCounted reference ? (T)reference.Retain(increment) : msg;
    }
    /**
     * Tries to call {@link ReferenceCounted#touch()} if the specified message implements {@link ReferenceCounted}.
     * If the specified message doesn't implement {@link ReferenceCounted}, this method does nothing.
     */
    public static T Touch<T>(T msg) => msg is IReferenceCounted reference ? (T)reference.Touch() : msg;
    /**
     * Tries to call {@link ReferenceCounted#touch(Object)} if the specified message implements
     * {@link ReferenceCounted}.  If the specified message doesn't implement {@link ReferenceCounted},
     * this method does nothing.
     */
    public static T Touch<T>(T msg, object hint) => msg is IReferenceCounted reference ? (T)reference.Touch(hint) : msg;
    /**
     * Try to call {@link ReferenceCounted#release()} if the specified message implements {@link ReferenceCounted}.
     * If the specified message doesn't implement {@link ReferenceCounted}, this method does nothing.
     */
    public static bool Release(object msg) => msg is IReferenceCounted reference && reference.Release();
    /**
     * Try to call {@link ReferenceCounted#release(int)} if the specified message implements {@link ReferenceCounted}.
     * If the specified message doesn't implement {@link ReferenceCounted}, this method does nothing.
     */
    public static bool Release(object msg, int decrement)
    {
        ObjectUtil.CheckPositive(decrement, "decrement");
        return msg is IReferenceCounted reference && reference.Release(decrement);
    }
    /**
     * Try to call {@link ReferenceCounted#release()} if the specified message implements {@link ReferenceCounted}.
     * If the specified message doesn't implement {@link ReferenceCounted}, this method does nothing.
     * Unlike {@link #release(Object)} this method catches an exception raised by {@link ReferenceCounted#release()}
     * and logs it, rather than rethrowing it to the caller.  It is usually recommended to use {@link #release(Object)}
     * instead, unless you absolutely need to swallow an exception.
     */
    public static void SafeRelease(object msg)
    {
        try { Release(msg); }
        catch (Exception failure) { logger.Warn("Failed to release a message: {}", msg, failure); }
    }
    /**
     * Try to call {@link ReferenceCounted#release(int)} if the specified message implements {@link ReferenceCounted}.
     * If the specified message doesn't implement {@link ReferenceCounted}, this method does nothing.
     * Unlike {@link #release(Object)} this method catches an exception raised by {@link ReferenceCounted#release(int)}
     * and logs it, rather than rethrowing it to the caller.  It is usually recommended to use
     * {@link #release(Object, int)} instead, unless you absolutely need to swallow an exception.
     */
    public static void SafeRelease(object msg, int decrement)
    {
        try
        {
            ObjectUtil.CheckPositive(decrement, "decrement");
            Release(msg, decrement);
        }
        catch (Exception failure)
        {
            if (logger.IsWarnEnabled()) logger.Warn("Failed to release a message: {} (decrement: {})", msg, decrement, failure);
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
    public static T ReleaseLater<T>(T msg) => ReleaseLater(msg, 1);
    /**
     * Schedules the specified object to be released when the caller thread terminates. Note that this operation is
     * intended to simplify reference counting of ephemeral objects during unit tests. Do not use it beyond the
     * intended use case.
     *
     * @deprecated this may introduce a lot of memory usage so it is generally preferable to manually release objects.
     */
    [Obsolete]
    public static T ReleaseLater<T>(T msg, int decrement)
    {
        ObjectUtil.CheckPositive(decrement, "decrement");
        if (msg is IReferenceCounted reference) ThreadDeathWatcher.Watch(Thread.CurrentThread, new ReleasingTask(reference, decrement).Run);
        return msg;
    }
    /**
     * Returns reference count of a {@link ReferenceCounted} object. If object is not type of
     * {@link ReferenceCounted}, {@code -1} is returned.
     */
    public static int GetReferenceCount(object msg) => msg is IReferenceCounted reference ? reference.ReferenceCount : -1;
    /**
     * Releases the objects when the thread that called {@link #releaseLater(Object)} has been terminated.
     */
    private sealed class ReleasingTask(IReferenceCounted obj, int decrement)
    {
        internal void Run()
        {
            try
            {
                if (!obj.Release(decrement)) logger.Warn("Non-zero refCnt: {}", this);
                else logger.Debug("Released: {}", this);
            }
            catch (Exception failure) { logger.Warn("Failed to release an object: {}", obj, failure); }
        }
        public override string ToString() => StringUtil.SimpleClassName(obj) + ".release(" + decrement + ") refCnt: " + obj.ReferenceCount;
    }
}
