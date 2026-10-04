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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common;

public static class ResourceLeakDetector
{
    internal static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(ResourceLeakDetector));
    public const string PROP_LEVEL_OLD = "io.netty.leakDetectionLevel";
    public const string PROP_LEVEL = "io.netty.leakDetection.level";
    public const ResourceLeakDetectorLevel DEFAULT_LEVEL = ResourceLeakDetectorLevel.SIMPLE;
    public const string PROP_TARGET_RECORDS = "io.netty.leakDetection.targetRecords";
    public const int DEFAULT_TARGET_RECORDS = 4;
    public const string PROP_SAMPLING_INTERVAL = "io.netty.leakDetection.samplingInterval";
    // There is a minor performance benefit in TLR if this is a power of 2.
    public const int DEFAULT_SAMPLING_INTERVAL = 128;
    public const string PROP_TRACK_CLOSE = "io.netty.leakDetection.trackClose";
    public static readonly int TARGET_RECORDS;
    public static readonly int SAMPLING_INTERVAL;
    internal static readonly bool TRACK_CLOSE;
    private static int level;
    internal static readonly AtomicReference<string[]> excludedMethods = new(Array.Empty<string>());

    static ResourceLeakDetector()
    {
        bool disabled = false;
        if (SystemPropertyUtil.Get("io.netty.noResourceLeakDetection") != null)
        {
            disabled = SystemPropertyUtil.GetBoolean("io.netty.noResourceLeakDetection", false);
            logger.Debug("-Dio.netty.noResourceLeakDetection: {}", disabled);
            logger.Warn("-Dio.netty.noResourceLeakDetection is deprecated. Use '-D{}={}' instead.", PROP_LEVEL, "disabled");
        }
        ResourceLeakDetectorLevel defaultLevel = disabled ? ResourceLeakDetectorLevel.DISABLED : DEFAULT_LEVEL;
        // First read old property name
        string levelString = SystemPropertyUtil.Get(PROP_LEVEL_OLD, defaultLevel.ToString());
        // If new property name is present, use it
        levelString = SystemPropertyUtil.Get(PROP_LEVEL, levelString);
        TARGET_RECORDS = SystemPropertyUtil.GetInt(PROP_TARGET_RECORDS, DEFAULT_TARGET_RECORDS);
        SAMPLING_INTERVAL = SystemPropertyUtil.GetInt(PROP_SAMPLING_INTERVAL, DEFAULT_SAMPLING_INTERVAL);
        TRACK_CLOSE = SystemPropertyUtil.GetBoolean(PROP_TRACK_CLOSE, true);
        level = (int)ParseLevel(levelString);
        if (logger.IsDebugEnabled())
        {
            logger.Debug("-D{}: {}", PROP_LEVEL, GetLevel().ToString().ToLowerInvariant());
            logger.Debug("-D{}: {}", PROP_TARGET_RECORDS, TARGET_RECORDS);
        }
    }
    /**
         * Returns level based on string value. Accepts also string that represents ordinal number of enum.
         *
         * @param levelStr - level string : DISABLED, SIMPLE, ADVANCED, PARANOID. Ignores case.
         * @return corresponding level or SIMPLE level in case of no match.
         */
    public static ResourceLeakDetectorLevel ParseLevel(string levelStr)
    {
        ArgumentNullException.ThrowIfNull(levelStr);
        string text = levelStr.Trim();
        foreach (ResourceLeakDetectorLevel candidate in Enum.GetValues<ResourceLeakDetectorLevel>())
            if (string.Equals(text, candidate.ToString(), StringComparison.OrdinalIgnoreCase) ||
                text == ((int)candidate).ToString(CultureInfo.InvariantCulture)) return candidate;
        return DEFAULT_LEVEL;
    }
    /**
     * @deprecated Use {@link #setLevel(Level)} instead.
     */
    [Obsolete]
    public static void SetEnabled(bool enabled) => SetLevel(enabled ? ResourceLeakDetectorLevel.SIMPLE : ResourceLeakDetectorLevel.DISABLED);
    /**
     * Returns {@code true} if resource leak detection is enabled.
     */
    public static bool IsEnabled() => GetLevel() > ResourceLeakDetectorLevel.DISABLED;
    /**
     * Sets the resource leak detection level.
     */
    public static void SetLevel(ResourceLeakDetectorLevel value)
    {
        if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
        Volatile.Write(ref level, (int)value);
    }
    /**
     * Returns the current resource leak detection level.
     */
    public static ResourceLeakDetectorLevel GetLevel() => (ResourceLeakDetectorLevel)Volatile.Read(ref level);

    public static void AddExclusions(Type clz, params string[] methodNames)
    {
        ArgumentNullException.ThrowIfNull(clz);
        ArgumentNullException.ThrowIfNull(methodNames);
        var names = new HashSet<string>(methodNames);
        // Use loop rather than lookup. This avoids knowing the parameters, and doesn't have to handle
        // NoSuchMethodException.
        foreach (MethodInfo method in clz.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                     BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            if (names.Remove(method.Name) && names.Count == 0) break;
        if (names.Count != 0) throw new ArgumentException("Can't find '[" + string.Join(", ", names) + "]' in " + clz.FullName);
        string[] before, after;
        do
        {
            before = excludedMethods.Get();
            after = new string[before.Length + 2 * methodNames.Length];
            Array.Copy(before, after, before.Length);
            for (int i = 0; i < methodNames.Length; i++)
            {
                after[before.Length + i * 2] = ExclusionTypeName(clz);
                after[before.Length + i * 2 + 1] = methodNames[i];
            }
        } while (!excludedMethods.CompareAndSet(before, after));
    }
    // CLR stack methods can expose an open declaring type even for a closed
    // generic invocation. Java exclusions identify the class across all T.
    internal static string ExclusionTypeName(Type type)
        => (type?.IsGenericType == true ? type.GetGenericTypeDefinition() : type)?.FullName;
}

public class ResourceLeakDetector<T> where T : class
{
    /** the collection of active resources */
    private readonly ConcurrentDictionary<DefaultResourceLeak<T>, byte> allLeaks = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentQueue<DefaultResourceLeak<T>> refQueue = new();
    private readonly ConcurrentDictionary<string, byte> reportedLeaks = new(StringComparer.Ordinal);
    private readonly string resourceType;
    private readonly int samplingInterval;
    /**
     * Will be notified once a leak is detected.
     */
    private LeakListener leakListener;
    /**
     * @deprecated use {@link ResourceLeakDetectorFactory#newResourceLeakDetector(Class, int, long)}.
     */
    [Obsolete]
    public ResourceLeakDetector(Type resourceType) : this(TypeName(resourceType)) { }
    /**
     * @deprecated use {@link ResourceLeakDetectorFactory#newResourceLeakDetector(Class, int, long)}.
     */
    [Obsolete]
    public ResourceLeakDetector(string resourceType) : this(resourceType, ResourceLeakDetector.DEFAULT_SAMPLING_INTERVAL, long.MaxValue) { }
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
    public ResourceLeakDetector(Type resourceType, int samplingInterval, long maxActive) : this(resourceType, samplingInterval) { }
    /**
     * This should not be used directly by users of {@link ResourceLeakDetector}.
     * Please use {@link ResourceLeakDetectorFactory#newResourceLeakDetector(Class)}
     * or {@link ResourceLeakDetectorFactory#newResourceLeakDetector(Class, int, long)}
     */
    public ResourceLeakDetector(Type resourceType, int samplingInterval) : this(TypeName(resourceType), samplingInterval, long.MaxValue) { }
    /**
     * @deprecated use {@link ResourceLeakDetectorFactory#newResourceLeakDetector(Class, int, long)}.
     * <p>
     * @param maxActive This is deprecated and will be ignored.
     */
    [Obsolete]
    public ResourceLeakDetector(string resourceType, int samplingInterval, long maxActive)
    {
        ArgumentNullException.ThrowIfNull(resourceType);
        this.resourceType = resourceType;
        this.samplingInterval = samplingInterval;
    }
    private static string TypeName(Type type) { ArgumentNullException.ThrowIfNull(type); return StringUtil.SimpleClassName(type); }
    /**
     * Creates a new {@link ResourceLeak} which is expected to be closed via {@link ResourceLeak#close()} when the
     * related resource is deallocated.
     *
     * @return the {@link ResourceLeak} or {@code null}
     * @deprecated use {@link #track(Object)}
     */
    [Obsolete]
    public IResourceLeak Open(T obj) => Track0(obj, false);
    /**
     * Creates a new {@link ResourceLeakTracker} which is expected to be closed via
     * {@link ResourceLeakTracker#close(Object)} when the related resource is deallocated.
     *
     * @return the {@link ResourceLeakTracker} or {@code null}
     */
    public virtual IResourceLeakTracker<T> Track(T obj) => Track0(obj, false);
    /**
     * Creates a new {@link ResourceLeakTracker} which is expected to be closed via
     * {@link ResourceLeakTracker#close(Object)} when the related resource is deallocated.
     * <p>
     * Unlike {@link #track(Object)}, this method always returns a tracker, regardless
     * of the detection settings.
     *
     * @return the {@link ResourceLeakTracker}
     */
    public virtual IResourceLeakTracker<T> TrackForcibly(T obj) => Track0(obj, true);
    /**
     * Check whether {@link ResourceLeakTracker#record()} does anything for this detector.
     *
     * @return {@code true} if {@link ResourceLeakTracker#record()} should be called
     */
    public virtual bool IsRecordEnabled()
    {
        ResourceLeakDetectorLevel level = ResourceLeakDetector.GetLevel();
        return (level == ResourceLeakDetectorLevel.ADVANCED || level == ResourceLeakDetectorLevel.PARANOID) && ResourceLeakDetector.TARGET_RECORDS > 0;
    }
    private DefaultResourceLeak<T> Track0(T obj, bool force)
    {
        ResourceLeakDetectorLevel level = ResourceLeakDetector.GetLevel();
        if (!force && level != ResourceLeakDetectorLevel.PARANOID)
        {
            if (level == ResourceLeakDetectorLevel.DISABLED) return null;
            if (samplingInterval <= 0) throw new ArgumentOutOfRangeException(nameof(samplingInterval));
            if (ThreadLocalRandom.Current().Next(samplingInterval) != 0) return null;
        }
        ReportLeak();
        return new DefaultResourceLeak<T>(obj, refQueue, allLeaks, GetInitialHint(resourceType));
    }
    /**
     * When the return value is {@code true}, {@link #reportTracedLeak} and {@link #reportUntracedLeak}
     * will be called once a leak is detected, otherwise not.
     *
     * @return {@code true} to enable leak reporting.
     */
    protected virtual bool NeedReport() => ResourceLeakDetector.logger.IsErrorEnabled();
    private void ReportLeak()
    {
        if (!NeedReport())
        {
            while (refQueue.TryDequeue(out var discarded)) discarded.Dispose();
            return;
        }
        // Detect and report previous leaks.
        while (refQueue.TryDequeue(out var leak))
        {
            if (!leak.Dispose()) continue;
            string records = leak.GetReportAndClearRecords();
            if (!reportedLeaks.TryAdd(records, 0)) continue;
            if (records.Length == 0) ReportUntracedLeak(resourceType);
            else ReportTracedLeak(resourceType, records);
            Volatile.Read(ref leakListener)?.OnLeak(resourceType, records);
        }
    }
    /**
     * This method is called when a traced leak is detected. It can be overridden for tracking how many times leaks
     * have been detected.
     */
    protected virtual void ReportTracedLeak(string resourceType, string records)
        => ResourceLeakDetector.logger.Error("LEAK: {}.release() was not called before it's garbage-collected. " +
            "See https://netty.io/wiki/reference-counted-objects.html for more information.{}", resourceType, records);
    /**
     * This method is called when an untraced leak is detected. It can be overridden for tracking how many times leaks
     * have been detected.
     */
    protected virtual void ReportUntracedLeak(string resourceType)
        => ResourceLeakDetector.logger.Error("LEAK: {}.release() was not called before it's garbage-collected. " +
            "Enable advanced leak reporting to find out where the leak occurred. " +
            "To enable advanced leak reporting, specify environment property '{}={}' or call {}.setLevel() " +
            "See https://netty.io/wiki/reference-counted-objects.html for more information.",
            resourceType, ResourceLeakDetector.PROP_LEVEL, "advanced", StringUtil.SimpleClassName(this));
    /**
     * @deprecated This method will no longer be invoked by {@link ResourceLeakDetector}.
     */
    [Obsolete]
    protected virtual void ReportInstancesLeak(string resourceType) { }
    /**
     * Create a hint object to be attached to an object tracked by this record. Similar to the additional information
     * supplied to {@link ResourceLeakTracker#record(Object)}, will be printed alongside the stack trace of the
     * creation of the resource.
     */
    protected virtual object GetInitialHint(string resourceType) => null;
    /**
     * Set leak listener. Previous listener will be replaced.
     */
    public void SetLeakListener(LeakListener listener) => Volatile.Write(ref leakListener, listener);
    public interface LeakListener
    {
        /**
         * Will be called once a leak is detected.
         */
        void OnLeak(string resourceType, string records);
    }
}
