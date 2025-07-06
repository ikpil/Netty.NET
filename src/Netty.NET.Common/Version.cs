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

namespace Netty.NET.Common;














/**
 * Retrieves the version information of available Netty artifacts.
 * <p>
 * This class retrieves the version information from {@code META-INF/io.netty.versions.properties}, which is
 * generated in build time.  Note that it may not be possible to retrieve the information completely, depending on
 * your environment, such as the specified {@link ClassLoader}, the current {@link SecurityManager}.
 * </p>
 */
public final class Version {

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
    public static Map<string, Version> identify() {
        return identify(null);
    }

    /**
     * Retrieves the version information of Netty artifacts using the specified {@link ClassLoader}.
     *
     * @return A {@link Map} whose keys are Maven artifact IDs and whose values are {@link Version}s
     */
    public static Map<string, Version> identify(ClassLoader classLoader) {
        if (classLoader == null) {
            classLoader = PlatformDependent.getContextClassLoader();
        }

        // Collect all properties.
        Properties props = new Properties();
        try {
            Enumeration<URL> resources = classLoader.getResources("META-INF/io.netty.versions.properties");
            while (resources.hasMoreElements()) {
                URL url = resources.nextElement();
                InputStream in = url.openStream();
                try {
                    props.load(in);
                } finally {
                    try {
                        in.close();
                    } catch (Exception ignore) {
                        // Ignore.
                    }
                }
            }
        } catch (Exception ignore) {
            // Not critical. Just ignore.
        }

        // Collect all artifactIds.
        Set<string> artifactIds = new HashSet<string>();
        for (object o: props.keySet()) {
            string k = (string) o;

            int dotIndex = k.indexOf('.');
            if (dotIndex <= 0) {
                continue;
            }

            string artifactId = k.substring(0, dotIndex);

            // Skip the entries without required information.
            if (!props.containsKey(artifactId + PROP_VERSION) ||
                !props.containsKey(artifactId + PROP_BUILD_DATE) ||
                !props.containsKey(artifactId + PROP_COMMIT_DATE) ||
                !props.containsKey(artifactId + PROP_SHORT_COMMIT_HASH) ||
                !props.containsKey(artifactId + PROP_LONG_COMMIT_HASH) ||
                !props.containsKey(artifactId + PROP_REPO_STATUS)) {
                continue;
            }

            artifactIds.add(artifactId);
        }

        Map<string, Version> versions = new TreeMap<string, Version>();
        for (string artifactId: artifactIds) {
            versions.put(
                    artifactId,
                    new Version(
                            artifactId,
                            props.getProperty(artifactId + PROP_VERSION),
                            parseIso8601(props.getProperty(artifactId + PROP_BUILD_DATE)),
                            parseIso8601(props.getProperty(artifactId + PROP_COMMIT_DATE)),
                            props.getProperty(artifactId + PROP_SHORT_COMMIT_HASH),
                            props.getProperty(artifactId + PROP_LONG_COMMIT_HASH),
                            props.getProperty(artifactId + PROP_REPO_STATUS)));
        }

        return versions;
    }

    private static long parseIso8601(string value) {
        try {
            return new SimpleDateFormat("yyyy-MM-dd HH:mm:ss Z").parse(value).getTime();
        } catch (ParseException ignored) {
            return 0;
        }
    }

    /**
     * Prints the version information to {@link System#err}.
     */
    public static void main(string[] args) {
        for (Version v: identify().values()) {
            System.err.println(v);
        }
    }

    private readonly string artifactId;
    private readonly string artifactVersion;
    private readonly long buildTimeMillis;
    private readonly long commitTimeMillis;
    private readonly string shortCommitHash;
    private readonly string longCommitHash;
    private readonly string repositoryStatus;

    private Version(
            string artifactId, string artifactVersion,
            long buildTimeMillis, long commitTimeMillis,
            string shortCommitHash, string longCommitHash, string repositoryStatus) {
        this.artifactId = artifactId;
        this.artifactVersion = artifactVersion;
        this.buildTimeMillis = buildTimeMillis;
        this.commitTimeMillis = commitTimeMillis;
        this.shortCommitHash = shortCommitHash;
        this.longCommitHash = longCommitHash;
        this.repositoryStatus = repositoryStatus;
    }

    public string artifactId() {
        return artifactId;
    }

    public string artifactVersion() {
        return artifactVersion;
    }

    public long buildTimeMillis() {
        return buildTimeMillis;
    }

    public long commitTimeMillis() {
        return commitTimeMillis;
    }

    public string shortCommitHash() {
        return shortCommitHash;
    }

    public string longCommitHash() {
        return longCommitHash;
    }

    public string repositoryStatus() {
        return repositoryStatus;
    }

    @Override
    public string toString() {
        return artifactId + '-' + artifactVersion + '.' + shortCommitHash +
               ("clean".equals(repositoryStatus)? "" : " (repository: " + repositoryStatus + ')');
    }
}
