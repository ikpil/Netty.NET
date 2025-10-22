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

using System;
using System.Collections.Generic;
using System.Linq;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Internal;

public class OsClassifiersTest : IDisposable
{
    private static readonly string OS_CLASSIFIERS_PROPERTY = "io.netty.osClassifiers";

    private Dictionary<string, string> systemProperties;

    public OsClassifiersTest()
    {
        systemProperties = SystemPropertyUtil.getProperties();
    }

    public void Dispose()
    {
        systemProperties.Remove(OS_CLASSIFIERS_PROPERTY);
    }

    [Fact]
    void testOsClassifiersPropertyAbsent()
    {
        ISet<string> available = new LinkedHashSet<string>(2);
        bool added = PlatformDependent.addPropertyOsClassifiers(available);
        Assert.False(added);
        Assert.True(available.IsEmpty());
    }

    [Fact]
    void testOsClassifiersPropertyEmpty()
    {
        // empty property -Dio.netty.osClassifiers
        systemProperties[OS_CLASSIFIERS_PROPERTY] = "";
        ISet<string> available = new LinkedHashSet<string>(2);
        bool added = PlatformDependent.addPropertyOsClassifiers(available);
        Assert.True(added);
        Assert.True(available.IsEmpty());
    }

    [Fact]
    void testOsClassifiersPropertyNotEmptyNoClassifiers()
    {
        // ID
        systemProperties[OS_CLASSIFIERS_PROPERTY] = ",";
        ISet<string> available = new LinkedHashSet<string>(2);
        Assert.Throws<ArgumentException>(() => PlatformDependent.addPropertyOsClassifiers(available));
    }

    [Fact]
    void testOsClassifiersPropertySingle()
    {
        // ID
        systemProperties[OS_CLASSIFIERS_PROPERTY] = "fedora";
        ISet<string> available = new LinkedHashSet<string>(2);
        bool added = PlatformDependent.addPropertyOsClassifiers(available);
        Assert.True(added);
        Assert.Equal(1, available.Count);
        Assert.Equal("fedora", available.First());
    }

    [Fact]
    void testOsClassifiersPropertyPair()
    {
        // ID, ID_LIKE
        systemProperties[OS_CLASSIFIERS_PROPERTY] = "manjaro,arch";
        ISet<string> available = new LinkedHashSet<string>(2);
        bool added = PlatformDependent.addPropertyOsClassifiers(available);
        Assert.True(added);
        Assert.Equal(1, available.Count);
        Assert.Equal("arch", available.First());
    }

    [Fact]
    void testOsClassifiersPropertyExcessive()
    {
        // ID, ID_LIKE, excessive
        systemProperties[OS_CLASSIFIERS_PROPERTY] = "manjaro,arch,slackware";
        ISet<string> available = new LinkedHashSet<string>(2);
        Assert.Throws<ArgumentException>(() => PlatformDependent.addPropertyOsClassifiers(available));
    }
}