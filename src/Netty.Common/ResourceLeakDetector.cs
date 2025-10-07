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
using System.Collections.Generic;
using System.Reflection;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common;

public static class ResourceLeakDetector
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.getInstance(typeof(ResourceLeakDetector));

    public static readonly string PROP_LEVEL_OLD = "io.netty.leakDetectionLevel";
    public static readonly string PROP_LEVEL = "io.netty.leakDetection.level";
    public static readonly ResourceLeakDetectorLevel DEFAULT_LEVEL = ResourceLeakDetectorLevel.SIMPLE;

    public static readonly string PROP_TARGET_RECORDS = "io.netty.leakDetection.targetRecords";
    public static readonly int DEFAULT_TARGET_RECORDS = 4;

    public static readonly string PROP_SAMPLING_INTERVAL = "io.netty.leakDetection.samplingInterval";

    // There is a minor performance benefit in TLR if this is a power of 2.
    public static readonly int DEFAULT_SAMPLING_INTERVAL = 128;

    public static readonly int TARGET_RECORDS;
    public static readonly int SAMPLING_INTERVAL;

    private static ResourceLeakDetectorLevel _level;

    internal static readonly AtomicReference<string[]> excludedMethods = new AtomicReference<string[]>(EmptyArrays.EMPTY_STRINGS);


    static ResourceLeakDetector()
    {
        bool disabled;
        if (SystemPropertyUtil.get("io.netty.noResourceLeakDetection") != null)
        {
            disabled = SystemPropertyUtil.getBoolean("io.netty.noResourceLeakDetection", false);
            logger.debug("-Dio.netty.noResourceLeakDetection: {}", disabled);
            logger.warn(
                "-Dio.netty.noResourceLeakDetection is deprecated. Use '-D{}={}' instead.",
                PROP_LEVEL, nameof(ResourceLeakDetectorLevel.DISABLED).ToLowerInvariant());
        }
        else
        {
            disabled = false;
        }

        ResourceLeakDetectorLevel defaultLevel = disabled ? ResourceLeakDetectorLevel.DISABLED : DEFAULT_LEVEL;

        // First read old property name
        string levelStr = SystemPropertyUtil.get(PROP_LEVEL_OLD, defaultLevel.ToString());

        // If new property name is present, use it
        levelStr = SystemPropertyUtil.get(PROP_LEVEL, levelStr);
        ResourceLeakDetectorLevel level = parseLevel(levelStr);

        TARGET_RECORDS = SystemPropertyUtil.getInt(PROP_TARGET_RECORDS, DEFAULT_TARGET_RECORDS);
        SAMPLING_INTERVAL = SystemPropertyUtil.getInt(PROP_SAMPLING_INTERVAL, DEFAULT_SAMPLING_INTERVAL);

        _level = level;
        if (logger.isDebugEnabled())
        {
            logger.debug("-D{}: {}", PROP_LEVEL, level.ToString().ToLowerInvariant());
            logger.debug("-D{}: {}", PROP_TARGET_RECORDS, TARGET_RECORDS);
        }
    }

    /**
     * Returns level based on string value. Accepts also string that represents ordinal number of enum.
     *
     * @param levelStr - level string : DISABLED, SIMPLE, ADVANCED, PARANOID. Ignores case.
     * @return corresponding level or SIMPLE level in case of no match.
     */
    public static ResourceLeakDetectorLevel parseLevel(string levelStr)
    {
        string trimmedLevelStr = levelStr.Trim();
        if (!Enum.TryParse(trimmedLevelStr, true, out ResourceLeakDetectorLevel level))
        {
            level = DEFAULT_LEVEL;
        }

        return level;
    }


    /**
     * @deprecated Use {@link #setLevel(ResourceLeakDetectorLevel)} instead.
     */
    [Obsolete]
    public static void setEnabled(bool enabled)
    {
        setLevel(enabled ? ResourceLeakDetectorLevel.SIMPLE : ResourceLeakDetectorLevel.DISABLED);
    }

    /**
     * Returns {@code true} if resource leak detection is enabled.
     */
    public static bool isEnabled()
    {
        return getLevel() > ResourceLeakDetectorLevel.DISABLED;
    }

    /**
     * Sets the resource leak detection level.
     */
    public static void setLevel(ResourceLeakDetectorLevel level)
    {
        _level = level;
    }

    /**
     * Returns the current resource leak detection level.
     */
    public static ResourceLeakDetectorLevel getLevel()
    {
        return _level;
    }

    public static void addExclusions(Type clz, params string[] methodNames)
    {
        ISet<string> nameSet = new HashSet<string>(methodNames);
        // Use loop rather than lookup. This avoids knowing the parameters, and doesn't have to handle
        // NoSuchMethodException.
        var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (MethodInfo method in clz.GetMethods(flags))
        {
            if (nameSet.Remove(method.Name) && nameSet.Count == 0)
            {
                break;
            }
        }

        if (0 < nameSet.Count)
        {
            throw new ArgumentException("Can't find '" + nameSet + "' in " + clz.Name);
        }

        string[] oldMethods;
        string[] newMethods;
        do
        {
            oldMethods = excludedMethods.get();
            newMethods = Arrays.copyOf(oldMethods, oldMethods.Length + 2 * methodNames.Length);
            for (int i = 0; i < methodNames.Length; i++)
            {
                newMethods[oldMethods.Length + i * 2] = clz.Name;
                newMethods[oldMethods.Length + i * 2 + 1] = methodNames[i];
            }
        } while (!excludedMethods.compareAndSet(oldMethods, newMethods));
    }
}

public class ResourceLeakDetector<T> where T : class
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.getInstance<ResourceLeakDetector<T>>();

    /** the collection of active resources */
    private readonly ConcurrentHashSet<DefaultResourceLeak<T>> allLeaks = new ConcurrentHashSet<DefaultResourceLeak<T>>();

    private readonly WeakReferenceQueue<DefaultResourceLeak<T>> refQueue = new WeakReferenceQueue<DefaultResourceLeak<T>>();
    private readonly ConcurrentHashSet<string> reportedLeaks = new ConcurrentHashSet<string>();

    private readonly string resourceType;
    private readonly int samplingInterval;

    /**
     * Will be notified once a leak is detected.
     */
    private volatile LeakListener leakListener;

    /**
     * @deprecated use {@link ResourceLeakDetectorFactory#newResourceLeakDetector(Class, int, long)}.
     */
    [Obsolete]
    public ResourceLeakDetector(Type resourceType)
        : this(resourceType.Name)
    {
    }

    /**
     * @deprecated use {@link ResourceLeakDetectorFactory#newResourceLeakDetector(Class, int, long)}.
     */
    [Obsolete]
    public ResourceLeakDetector(string resourceType)
        : this(resourceType, ResourceLeakDetector.DEFAULT_SAMPLING_INTERVAL, long.MaxValue)
    {
    }

    /**
     * @deprecated Use {@link ResourceLeakDetector#ResourceLeakDetector(Class, int)}.
     * <p>
     * This should not be used directly by users of {@link ResourceLeakDetector}.
     * Please use {@link ResourceLeakDetectorFactory#newResourceLeakDetector(Class)}
     * or {@link ResourceLeakDetectorFactory#newResourceLeakDetector(Class, int, long)}
     *
     * @param maxActive This is deprecated and will be ignored.
     */
    [Obsolete]
    public ResourceLeakDetector(Type resourceType, int samplingInterval, long maxActive)
        : this(resourceType, samplingInterval)
    {
    }

    /**
     * This should not be used directly by users of {@link ResourceLeakDetector}.
     * Please use {@link ResourceLeakDetectorFactory#newResourceLeakDetector(Class)}
     * or {@link ResourceLeakDetectorFactory#newResourceLeakDetector(Class, int, long)}
     */
    //@SuppressWarnings("deprecation")
    public ResourceLeakDetector(Type resourceType, int samplingInterval)
        : this(resourceType.Name, samplingInterval, long.MaxValue)
    {
    }

    /**
     * @deprecated use {@link ResourceLeakDetectorFactory#newResourceLeakDetector(Class, int, long)}.
     * <p>
     * @param maxActive This is deprecated and will be ignored.
     */
    [Obsolete]
    public ResourceLeakDetector(string resourceType, int samplingInterval, long maxActive)
    {
        this.resourceType = ObjectUtil.checkNotNull(resourceType, "resourceType");
        this.samplingInterval = samplingInterval;
    }

    /**
     * Creates a new {@link IResourceLeak} which is expected to be closed via {@link IResourceLeak#close()} when the
     * related resource is deallocated.
     *
     * @return the {@link IResourceLeak} or {@code null}
     * @deprecated use {@link #track(object)}
     */
    [Obsolete]
    public IResourceLeakTracker<T> open(T obj)
    {
        return track0(obj, false);
    }

    /**
     * Creates a new {@link IResourceLeakTracker} which is expected to be closed via
     * {@link IResourceLeakTracker#close(object)} when the related resource is deallocated.
     *
     * @return the {@link IResourceLeakTracker} or {@code null}
     */
    //@SuppressWarnings("unchecked")
    public IResourceLeakTracker<T> track(T obj)
    {
        return track0(obj, false);
    }

    /**
     * Creates a new {@link IResourceLeakTracker} which is expected to be closed via
     * {@link IResourceLeakTracker#close(object)} when the related resource is deallocated.
     *
     * Unlike {@link #track(object)}, this method always returns a tracker, regardless
     * of the detection settings.
     *
     * @return the {@link IResourceLeakTracker}
     */
    //@SuppressWarnings("unchecked")
    public IResourceLeakTracker<T> trackForcibly(T obj)
    {
        return track0(obj, true);
    }

    //@SuppressWarnings("unchecked")
    private DefaultResourceLeak<T> track0(T obj, bool force)
    {
        ResourceLeakDetectorLevel level = ResourceLeakDetector.getLevel();
        if (force ||
            level == ResourceLeakDetectorLevel.PARANOID ||
            (level != ResourceLeakDetectorLevel.DISABLED && ThreadLocalRandom.current().Next(samplingInterval) == 0))
        {
            reportLeak();
            return new DefaultResourceLeak<T>(obj, refQueue, allLeaks, getInitialHint(resourceType));
        }

        return null;
    }

    private void clearRefQueue()
    {
        for (;;)
        {
            DefaultResourceLeak<T> @ref = (DefaultResourceLeak<T>)refQueue.poll();
            if (@ref == null)
            {
                break;
            }

            @ref.dispose();
        }
    }

    /**
     * When the return value is {@code true}, {@link #reportTracedLeak} and {@link #reportUntracedLeak}
     * will be called once a leak is detected, otherwise not.
     *
     * @return {@code true} to enable leak reporting.
     */
    protected bool needReport()
    {
        return logger.isErrorEnabled();
    }

    private void reportLeak()
    {
        if (!needReport())
        {
            clearRefQueue();
            return;
        }

        // Detect and report previous leaks.
        for (;;)
        {
            DefaultResourceLeak<T> @ref = (DefaultResourceLeak<T>)refQueue.poll();
            if (@ref == null)
            {
                break;
            }

            if (!@ref.dispose())
            {
                continue;
            }

            string records = @ref.getReportAndClearRecords();
            if (reportedLeaks.Add(records))
            {
                if (records.isEmpty())
                {
                    reportUntracedLeak(resourceType);
                }
                else
                {
                    reportTracedLeak(resourceType, records);
                }

                LeakListener listener = leakListener;
                if (listener != null)
                {
                    listener.onLeak(resourceType, records);
                }
            }
        }
    }

    /**
     * This method is called when a traced leak is detected. It can be overridden for tracking how many times leaks
     * have been detected.
     */
    protected void reportTracedLeak(string resourceType, string records)
    {
        logger.error(
            "LEAK: {}.release() was not called before it's garbage-collected. " +
            "See https://netty.io/wiki/reference-counted-objects.html for more information.{}",
            resourceType, records);
    }

    /**
     * This method is called when an untraced leak is detected. It can be overridden for tracking how many times leaks
     * have been detected.
     */
    protected void reportUntracedLeak(string resourceType)
    {
        logger.error("LEAK: {}.release() was not called before it's garbage-collected. " +
                     "Enable advanced leak reporting to find out where the leak occurred. " +
                     "To enable advanced leak reporting, " +
                     "specify the JVM option '-D{}={}' or call {}.setLevel() " +
                     "See https://netty.io/wiki/reference-counted-objects.html for more information.",
            resourceType, ResourceLeakDetector.PROP_LEVEL, nameof(ResourceLeakDetectorLevel.ADVANCED).ToLower(), StringUtil.simpleClassName(this));
    }

    /**
     * @deprecated This method will no longer be invoked by {@link ResourceLeakDetector}.
     */
    [Obsolete]
    protected void reportInstancesLeak(string resourceType)
    {
    }

    /**
     * Create a hint object to be attached to an object tracked by this record. Similar to the additional information
     * supplied to {@link IResourceLeakTracker#record(object)}, will be printed alongside the stack trace of the
     * creation of the resource.
     */
    protected object getInitialHint(string resourceType)
    {
        return null;
    }

    /**
     * Set leak listener. Previous listener will be replaced.
     */
    public void setLeakListener(LeakListener leakListener)
    {
        this.leakListener = leakListener;
    }

    public interface LeakListener
    {
        /**
         * Will be called once a leak is detected.
         */
        void onLeak(string resourceType, string records);
    }
}