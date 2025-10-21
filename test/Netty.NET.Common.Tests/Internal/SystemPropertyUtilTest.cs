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
namespace Netty.NET.Common.Tests.Internal;


public class SystemPropertyUtilTest {

    @BeforeEach
    public void clearSystemPropertyBeforeEach() {
        System.clearProperty("key");
    }

    [Fact]
    public void testGetWithKeyNull() {
        Assert.Throws<NullReferenceException>(new Executable() {
            @Override
            public void execute() {
                SystemPropertyUtil.get(null, null);
            }
        });
    }

    [Fact]
    public void testGetWithKeyEmpty() {
        Assert.Throws<ArgumentException>(new Executable() {
            @Override
            public void execute() {
                SystemPropertyUtil.get("", null);
            }
        });
    }

    [Fact]
    public void testGetDefaultValueWithPropertyNull() {
        Assert.Equal("default", SystemPropertyUtil.get("key", "default"));
    }

    [Fact]
    public void testGetPropertyValue() {
        System.setProperty("key", "value");
        Assert.Equal("value", SystemPropertyUtil.get("key"));
    }

    [Fact]
    public void testGetBooleanDefaultValueWithPropertyNull() {
        Assert.True(SystemPropertyUtil.getBoolean("key", true));
        Assert.False(SystemPropertyUtil.getBoolean("key", false));
    }

    [Fact]
    public void testGetBooleanDefaultValueWithEmptyString() {
        System.setProperty("key", "");
        Assert.True(SystemPropertyUtil.getBoolean("key", true));
        Assert.False(SystemPropertyUtil.getBoolean("key", false));
    }

    [Fact]
    public void testGetBooleanWithTrueValue() {
        System.setProperty("key", "true");
        Assert.True(SystemPropertyUtil.getBoolean("key", false));
        System.setProperty("key", "yes");
        Assert.True(SystemPropertyUtil.getBoolean("key", false));
        System.setProperty("key", "1");
        Assert.True(SystemPropertyUtil.getBoolean("key", true));
    }

    [Fact]
    public void testGetBooleanWithFalseValue() {
        System.setProperty("key", "false");
        Assert.False(SystemPropertyUtil.getBoolean("key", true));
        System.setProperty("key", "no");
        Assert.False(SystemPropertyUtil.getBoolean("key", false));
        System.setProperty("key", "0");
        Assert.False(SystemPropertyUtil.getBoolean("key", true));
    }

    [Fact]
    public void testGetBooleanDefaultValueWithWrongValue() {
        System.setProperty("key", "abc");
        Assert.True(SystemPropertyUtil.getBoolean("key", true));
        System.setProperty("key", "123");
        Assert.False(SystemPropertyUtil.getBoolean("key", false));
    }

    [Fact]
    public void getIntDefaultValueWithPropertyNull() {
        Assert.Equal(1, SystemPropertyUtil.getInt("key", 1));
    }

    [Fact]
    public void getIntWithPropertValueIsInt() {
        System.setProperty("key", "123");
        Assert.Equal(123, SystemPropertyUtil.getInt("key", 1));
    }

    [Fact]
    public void getIntDefaultValueWithPropertValueIsNotInt() {
        System.setProperty("key", "NotInt");
        Assert.Equal(1, SystemPropertyUtil.getInt("key", 1));
    }

    [Fact]
    public void getLongDefaultValueWithPropertyNull() {
        Assert.Equal(1, SystemPropertyUtil.getLong("key", 1));
    }

    [Fact]
    public void getLongWithPropertValueIsLong() {
        System.setProperty("key", "123");
        Assert.Equal(123, SystemPropertyUtil.getLong("key", 1));
    }

    [Fact]
    public void getLongDefaultValueWithPropertValueIsNotLong() {
        System.setProperty("key", "NotInt");
        Assert.Equal(1, SystemPropertyUtil.getLong("key", 1));
    }

}
