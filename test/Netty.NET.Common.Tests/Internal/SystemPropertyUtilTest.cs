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

[Collection("System properties")]
public class SystemPropertyUtilTest : IDisposable
{
    private readonly string previousValue = Environment.GetEnvironmentVariable("key");
    public void Dispose() => Environment.SetEnvironmentVariable("key", previousValue);

    public SystemPropertyUtilTest()
    {
        ClearSystemPropertyBeforeEach();
    }

    private void ClearSystemPropertyBeforeEach()
    {
        Environment.SetEnvironmentVariable("key", null);
    }

    [Fact]
    public void TestGetWithKeyNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            SystemPropertyUtil.Get(null, null);
        });
    }

    [Fact]
    public void TestGetWithKeyEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            SystemPropertyUtil.Get("", null);
        });
    }

    [Fact]
    public void TestGetDefaultValueWithPropertyNull()
    {
        Assert.Equal("default", SystemPropertyUtil.Get("key", "default"));
    }

    [Fact]
    public void TestGetPropertyValue()
    {
        Environment.SetEnvironmentVariable("key", "value");
        Assert.Equal("value", SystemPropertyUtil.Get("key"));
    }

    [Fact]
    public void TestGetBooleanDefaultValueWithPropertyNull()
    {
        Assert.True(SystemPropertyUtil.GetBoolean("key", true));
        Assert.False(SystemPropertyUtil.GetBoolean("key", false));
    }

    [Fact]
    public void TestGetBooleanDefaultValueWithEmptyString()
    {
        Environment.SetEnvironmentVariable("key", "");
        Assert.True(SystemPropertyUtil.GetBoolean("key", true));
        Assert.False(SystemPropertyUtil.GetBoolean("key", false));
    }

    [Fact]
    public void TestGetBooleanWithTrueValue()
    {
        Environment.SetEnvironmentVariable("key", "true");
        Assert.True(SystemPropertyUtil.GetBoolean("key", false));
        Environment.SetEnvironmentVariable("key", "yes");
        Assert.True(SystemPropertyUtil.GetBoolean("key", false));
        Environment.SetEnvironmentVariable("key", "1");
        Assert.True(SystemPropertyUtil.GetBoolean("key", true));
    }

    [Fact]
    public void TestGetBooleanWithFalseValue()
    {
        Environment.SetEnvironmentVariable("key", "false");
        Assert.False(SystemPropertyUtil.GetBoolean("key", true));
        Environment.SetEnvironmentVariable("key", "no");
        Assert.False(SystemPropertyUtil.GetBoolean("key", false));
        Environment.SetEnvironmentVariable("key", "0");
        Assert.False(SystemPropertyUtil.GetBoolean("key", true));
    }

    [Fact]
    public void TestGetBooleanDefaultValueWithWrongValue()
    {
        Environment.SetEnvironmentVariable("key", "abc");
        Assert.True(SystemPropertyUtil.GetBoolean("key", true));
        Environment.SetEnvironmentVariable("key", "123");
        Assert.False(SystemPropertyUtil.GetBoolean("key", false));
    }

    [Fact]
    public void GetIntDefaultValueWithPropertyNull()
    {
        Assert.Equal(1, SystemPropertyUtil.GetInt("key", 1));
    }

    [Fact]
    public void GetIntWithPropertValueIsInt()
    {
        Environment.SetEnvironmentVariable("key", "123");
        Assert.Equal(123, SystemPropertyUtil.GetInt("key", 1));
    }

    [Fact]
    public void GetIntDefaultValueWithPropertValueIsNotInt()
    {
        Environment.SetEnvironmentVariable("key", "NotInt");
        Assert.Equal(1, SystemPropertyUtil.GetInt("key", 1));
    }

    [Fact]
    public void GetLongDefaultValueWithPropertyNull()
    {
        Assert.Equal(1, SystemPropertyUtil.GetLong("key", 1));
    }

    [Fact]
    public void GetLongWithPropertValueIsLong()
    {
        Environment.SetEnvironmentVariable("key", "123");
        Assert.Equal(123, SystemPropertyUtil.GetLong("key", 1));
    }

    [Fact]
    public void GetLongDefaultValueWithPropertValueIsNotLong()
    {
        Environment.SetEnvironmentVariable("key", "NotInt");
        Assert.Equal(1, SystemPropertyUtil.GetLong("key", 1));
    }
}
