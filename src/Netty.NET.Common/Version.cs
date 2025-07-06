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
using System.Globalization;
using System.IO;
using System.Reflection;

namespace Netty.NET.Common;

/**
 * Retrieves the version information of available Netty artifacts.
 * <p>
 * This class retrieves the version information from {@code META-INF/io.netty.versions.properties}, which is
 * generated in build time.  Note that it may not be possible to retrieve the information completely, depending on
 * your environment, such as the specified {@link ClassLoader}, the current {@link SecurityManager}.
 * </p>
 */
public sealed class Version
{
    private static readonly string PROP_VERSION = ".version";
    private static readonly string PROP_BUILD_DATE = ".buildDate";
    private static readonly string PROP_COMMIT_DATE = ".commitDate";
    private static readonly string PROP_SHORT_COMMIT_HASH = ".shortCommitHash";
    private static readonly string PROP_LONG_COMMIT_HASH = ".longCommitHash";
    private static readonly string PROP_REPO_STATUS = ".repoStatus";

    /**
     * Retrieves the version information of Netty artifacts using the current
     * {@linkplain Thread#getContextClassLoader() context class loader}.
     *
     * @return A {@link Map} whose keys are Maven artifact IDs and whose values are {@link Version}s
     */
    public static IDictionary<string, Version> identify()
    {
        return identify(null);
    }

    /**
     * Retrieves the version information of Netty artifacts using the specified {@link ClassLoader}.
     *
     * @return A {@link Map} whose keys are Maven artifact IDs and whose values are {@link Version}s
     */
    public static IDictionary<string, Version> identify(Assembly classLoader)
    {
        if (classLoader == null)
        {
            classLoader = Assembly.GetExecutingAssembly();
        }

        // Collect all properties.
        IDictionary<string, string> props = new Dictionary<string, string>();
        try
        {
            var lines = File.ReadLines("io.netty.versions.properties");
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;
                var index = trimmed.IndexOf('=');
                if (index > 0)
                {
                    var key = trimmed.Substring(0, index).Trim();
                    var value = trimmed.Substring(index + 1).Trim();
                    props[key] = value;
                }
            }
        }
        catch (Exception ignore)
        {
            // Not critical. Just ignore.
        }

        // Collect all artifactIds.
        ISet<string> artifactIds = new HashSet<string>();
        foreach (string k in props.Keys)
        {
            int dotIndex = k.IndexOf('.');
            if (dotIndex <= 0)
            {
                continue;
            }

            string artifactId = k.Substring(0, dotIndex);

            // Skip the entries without required information.
            if (!props.ContainsKey(artifactId + PROP_VERSION) ||
                !props.ContainsKey(artifactId + PROP_BUILD_DATE) ||
                !props.ContainsKey(artifactId + PROP_COMMIT_DATE) ||
                !props.ContainsKey(artifactId + PROP_SHORT_COMMIT_HASH) ||
                !props.ContainsKey(artifactId + PROP_LONG_COMMIT_HASH) ||
                !props.ContainsKey(artifactId + PROP_REPO_STATUS))
            {
                continue;
            }

            artifactIds.Add(artifactId);
        }

        IDictionary<string, Version> versions = new SortedDictionary<string, Version>();
        foreach (string artifactId in artifactIds)
        {
            versions.Add(
                artifactId,
                new Version(
                    artifactId,
                    getProperty(props, artifactId + PROP_VERSION),
                    parseIso8601(getProperty(props, artifactId + PROP_BUILD_DATE)),
                    parseIso8601(getProperty(props, artifactId + PROP_COMMIT_DATE)),
                    getProperty(props, artifactId + PROP_SHORT_COMMIT_HASH),
                    getProperty(props, artifactId + PROP_LONG_COMMIT_HASH),
                    getProperty(props, artifactId + PROP_REPO_STATUS)));
        }

        return versions;
    }

    private static string getProperty(IDictionary<string, string> props, string key)
    {
        return props.TryGetValue(key, out var value)
            ? value
            : "";
    }

    private static long parseIso8601(string value)
    {
        try
        {
            // Java 포맷 "yyyy-MM-dd HH:mm:ss Z"
            // C# 포맷 "yyyy-MM-dd HH:mm:ss zzz"
            var dt = DateTime.ParseExact(
                value,
                "yyyy-MM-dd HH:mm:ss zzz",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal);
            // Unix 시간(ms)으로 변환
            return new DateTimeOffset(dt).ToUnixTimeMilliseconds();
        }
        catch (FormatException)
        {
            return 0;
        }
    }

    /**
     * Prints the version information to {@link System#err}.
     */
    public static void Main(string[] args)
    {
        foreach (Version v in identify().Values)
        {
            Console.Error.WriteLine(v);
        }
    }

    private readonly string _artifactId;
    private readonly string _artifactVersion;
    private readonly long _buildTimeMillis;
    private readonly long _commitTimeMillis;
    private readonly string _shortCommitHash;
    private readonly string _longCommitHash;
    private readonly string _repositoryStatus;

    private Version(
        string artifactId, string artifactVersion,
        long buildTimeMillis, long commitTimeMillis,
        string shortCommitHash, string longCommitHash, string repositoryStatus)
    {
        _artifactId = artifactId;
        _artifactVersion = artifactVersion;
        _buildTimeMillis = buildTimeMillis;
        _commitTimeMillis = commitTimeMillis;
        _shortCommitHash = shortCommitHash;
        _longCommitHash = longCommitHash;
        _repositoryStatus = repositoryStatus;
    }

    public string artifactId()
    {
        return _artifactId;
    }

    public string artifactVersion()
    {
        return _artifactVersion;
    }

    public long buildTimeMillis()
    {
        return _buildTimeMillis;
    }

    public long commitTimeMillis()
    {
        return _commitTimeMillis;
    }

    public string shortCommitHash()
    {
        return _shortCommitHash;
    }

    public string longCommitHash()
    {
        return _longCommitHash;
    }

    public string repositoryStatus()
    {
        return _repositoryStatus;
    }

    public override string ToString()
    {
        return _artifactId + '-' + _artifactVersion + '.' + _shortCommitHash +
               ("clean" == _repositoryStatus ? "" : " (repository: " + _repositoryStatus + ')');
    }
}