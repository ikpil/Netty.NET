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

[Collection("System properties")]
public class OsClassifiersTest : IDisposable
{
    private static readonly string OS_CLASSIFIERS_PROPERTY = "io.netty.osClassifiers";

    private readonly string previousValue;

    public OsClassifiersTest()
    {
        previousValue = Environment.GetEnvironmentVariable(OS_CLASSIFIERS_PROPERTY);
        Environment.SetEnvironmentVariable(OS_CLASSIFIERS_PROPERTY, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(OS_CLASSIFIERS_PROPERTY, previousValue);
    }

    [Fact]
    void TestOsClassifiersPropertyAbsent()
    {
        ISet<string> available = new LinkedHashSet<string>(2);
        bool added = PlatformDependent.AddPropertyOsClassifiers(available);
        Assert.False(added);
        Assert.True(available.IsEmpty());
    }

    [Fact]
    void TestOsClassifiersPropertyEmpty()
    {
        // empty property -Dio.netty.osClassifiers
        Environment.SetEnvironmentVariable(OS_CLASSIFIERS_PROPERTY, "");
        ISet<string> available = new LinkedHashSet<string>(2);
        bool added = PlatformDependent.AddPropertyOsClassifiers(available);
        Assert.True(added);
        Assert.True(available.IsEmpty());
    }

    [Fact]
    void TestOsClassifiersPropertyNotEmptyNoClassifiers()
    {
        // ID
        Environment.SetEnvironmentVariable(OS_CLASSIFIERS_PROPERTY, ",");
        ISet<string> available = new LinkedHashSet<string>(2);
        Assert.Throws<ArgumentException>(() => PlatformDependent.AddPropertyOsClassifiers(available));
    }

    [Fact]
    void TestOsClassifiersPropertySingle()
    {
        // ID
        Environment.SetEnvironmentVariable(OS_CLASSIFIERS_PROPERTY, "fedora");
        ISet<string> available = new LinkedHashSet<string>(2);
        bool added = PlatformDependent.AddPropertyOsClassifiers(available);
        Assert.True(added);
        Assert.Equal(1, available.Count);
        Assert.Equal("fedora", available.First());
    }

    [Fact]
    void TestOsClassifiersPropertyPair()
    {
        // ID, ID_LIKE
        Environment.SetEnvironmentVariable(OS_CLASSIFIERS_PROPERTY, "manjaro,arch");
        ISet<string> available = new LinkedHashSet<string>(2);
        bool added = PlatformDependent.AddPropertyOsClassifiers(available);
        Assert.True(added);
        Assert.Equal(1, available.Count);
        Assert.Equal("arch", available.First());
    }

    [Fact]
    void TestOsClassifiersPropertyExcessive()
    {
        // ID, ID_LIKE, excessive
        Environment.SetEnvironmentVariable(OS_CLASSIFIERS_PROPERTY, "manjaro,arch,slackware");
        ISet<string> available = new LinkedHashSet<string>(2);
        Assert.Throws<ArgumentException>(() => PlatformDependent.AddPropertyOsClassifiers(available));
    }
}
