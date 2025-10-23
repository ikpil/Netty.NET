/*
 * Copyright 2017 The Netty Project
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
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Internal;

public class SystemPropertyUtilTest
{
    public SystemPropertyUtilTest()
    {
        clearSystemPropertyBeforeEach();
    }

    private void clearSystemPropertyBeforeEach()
    {
        Environment.SetEnvironmentVariable("key", null);
    }

    [Fact]
    public void testGetWithKeyNull()
    {
        Assert.Throws<NullReferenceException>(() =>
        {
            SystemPropertyUtil.get(null, null);
        });
    }

    [Fact]
    public void testGetWithKeyEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            SystemPropertyUtil.get("", null);
        });
    }

    [Fact]
    public void testGetDefaultValueWithPropertyNull()
    {
        Assert.Equal("default", SystemPropertyUtil.get("key", "default"));
    }

    [Fact]
    public void testGetPropertyValue()
    {
        Environment.SetEnvironmentVariable("key", "value");
        Assert.Equal("value", SystemPropertyUtil.get("key"));
    }

    [Fact]
    public void testGetBooleanDefaultValueWithPropertyNull()
    {
        Assert.True(SystemPropertyUtil.getBoolean("key", true));
        Assert.False(SystemPropertyUtil.getBoolean("key", false));
    }

    [Fact]
    public void testGetBooleanDefaultValueWithEmptyString()
    {
        Environment.SetEnvironmentVariable("key", "");
        Assert.True(SystemPropertyUtil.getBoolean("key", true));
        Assert.False(SystemPropertyUtil.getBoolean("key", false));
    }

    [Fact]
    public void testGetBooleanWithTrueValue()
    {
        Environment.SetEnvironmentVariable("key", "true");
        Assert.True(SystemPropertyUtil.getBoolean("key", false));
        Environment.SetEnvironmentVariable("key", "yes");
        Assert.True(SystemPropertyUtil.getBoolean("key", false));
        Environment.SetEnvironmentVariable("key", "1");
        Assert.True(SystemPropertyUtil.getBoolean("key", true));
    }

    [Fact]
    public void testGetBooleanWithFalseValue()
    {
        Environment.SetEnvironmentVariable("key", "false");
        Assert.False(SystemPropertyUtil.getBoolean("key", true));
        Environment.SetEnvironmentVariable("key", "no");
        Assert.False(SystemPropertyUtil.getBoolean("key", false));
        Environment.SetEnvironmentVariable("key", "0");
        Assert.False(SystemPropertyUtil.getBoolean("key", true));
    }

    [Fact]
    public void testGetBooleanDefaultValueWithWrongValue()
    {
        Environment.SetEnvironmentVariable("key", "abc");
        Assert.True(SystemPropertyUtil.getBoolean("key", true));
        Environment.SetEnvironmentVariable("key", "123");
        Assert.False(SystemPropertyUtil.getBoolean("key", false));
    }

    [Fact]
    public void getIntDefaultValueWithPropertyNull()
    {
        Assert.Equal(1, SystemPropertyUtil.getInt("key", 1));
    }

    [Fact]
    public void getIntWithPropertValueIsInt()
    {
        Environment.SetEnvironmentVariable("key", "123");
        Assert.Equal(123, SystemPropertyUtil.getInt("key", 1));
    }

    [Fact]
    public void getIntDefaultValueWithPropertValueIsNotInt()
    {
        Environment.SetEnvironmentVariable("key", "NotInt");
        Assert.Equal(1, SystemPropertyUtil.getInt("key", 1));
    }

    [Fact]
    public void getLongDefaultValueWithPropertyNull()
    {
        Assert.Equal(1, SystemPropertyUtil.getLong("key", 1));
    }

    [Fact]
    public void getLongWithPropertValueIsLong()
    {
        Environment.SetEnvironmentVariable("key", "123");
        Assert.Equal(123, SystemPropertyUtil.getLong("key", 1));
    }

    [Fact]
    public void getLongDefaultValueWithPropertValueIsNotLong()
    {
        Environment.SetEnvironmentVariable("key", "NotInt");
        Assert.Equal(1, SystemPropertyUtil.getLong("key", 1));
    }
}