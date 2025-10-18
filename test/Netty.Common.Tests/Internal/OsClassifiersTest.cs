/*
 * Copyright 2022 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

namespace Netty.Common.Tests.Internal;


public class OsClassifiersTest {
    private static final string OS_CLASSIFIERS_PROPERTY = "io.netty.osClassifiers";

    private Properties systemProperties;

    @BeforeEach
    void setUp() {
        systemProperties = System.getProperties();
    }

    @AfterEach
    void tearDown() {
        systemProperties.remove(OS_CLASSIFIERS_PROPERTY);
    }

    [Fact]
    void testOsClassifiersPropertyAbsent() {
        Set<string> available = new LinkedHashSet<>(2);
        bool added = PlatformDependent.addPropertyOsClassifiers(available);
        Assert.False(added);
        Assert.True(available.isEmpty());
    }

    [Fact]
    void testOsClassifiersPropertyEmpty() {
        // empty property -Dio.netty.osClassifiers
        systemProperties.setProperty(OS_CLASSIFIERS_PROPERTY, "");
        Set<string> available = new LinkedHashSet<>(2);
        bool added = PlatformDependent.addPropertyOsClassifiers(available);
        Assert.True(added);
        Assert.True(available.isEmpty());
    }

    [Fact]
    void testOsClassifiersPropertyNotEmptyNoClassifiers() {
        // ID
        systemProperties.setProperty(OS_CLASSIFIERS_PROPERTY, ",");
        final Set<string> available = new LinkedHashSet<>(2);
        Assertions.assertThrows(ArgumentException.class,
                () -> PlatformDependent.addPropertyOsClassifiers(available));
    }

    [Fact]
    void testOsClassifiersPropertySingle() {
        // ID
        systemProperties.setProperty(OS_CLASSIFIERS_PROPERTY, "fedora");
        Set<string> available = new LinkedHashSet<>(2);
        bool added = PlatformDependent.addPropertyOsClassifiers(available);
        Assert.True(added);
        Assert.Equal(1, available.size());
        Assert.Equal("fedora", available.iterator().next());
    }

    [Fact]
    void testOsClassifiersPropertyPair() {
        // ID, ID_LIKE
        systemProperties.setProperty(OS_CLASSIFIERS_PROPERTY, "manjaro,arch");
        Set<string> available = new LinkedHashSet<>(2);
        bool added = PlatformDependent.addPropertyOsClassifiers(available);
        Assert.True(added);
        Assert.Equal(1, available.size());
        Assert.Equal("arch", available.iterator().next());
    }

    [Fact]
    void testOsClassifiersPropertyExcessive() {
        // ID, ID_LIKE, excessive
        systemProperties.setProperty(OS_CLASSIFIERS_PROPERTY, "manjaro,arch,slackware");
        final Set<string> available = new LinkedHashSet<>(2);
        Assertions.assertThrows(ArgumentException.class,
                () -> PlatformDependent.addPropertyOsClassifiers(available));
    }
}
