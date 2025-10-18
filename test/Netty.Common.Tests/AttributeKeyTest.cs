/*
 * Copyright 2015 The Netty Project
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

using Netty.NET.Common;

namespace Netty.Common.Tests;

public class AttributeKeyTest
{
    [Fact]
    public void testExists()
    {
        string name = "test";
        Assert.False(AttributeKey.exists<string>(name));
        AttributeKey<string> attr = AttributeKey.valueOf<string>(name);

        Assert.True(AttributeKey.exists<string>(name));
        Assert.NotNull(attr);
    }

    [Fact]
    public void testValueOf()
    {
        string name = "test1";
        Assert.False(AttributeKey.exists<string>(name));
        AttributeKey<string> attr = AttributeKey.valueOf<string>(name);
        AttributeKey<string> attr2 = AttributeKey.valueOf<string>(name);

        Assert.Same(attr, attr2);
    }

    [Fact]
    public void testNewInstance()
    {
        string name = "test2";
        Assert.False(AttributeKey.exists<string>(name));
        AttributeKey<string> attr = AttributeKey.newInstance<string>(name);
        Assert.True(AttributeKey.exists<string>(name));
        Assert.NotNull(attr);

        try
        {
            AttributeKey.newInstance<string>(name);
            Assert.Fail();
        }
        catch (ArgumentException e)
        {
            // expected
        }
    }
}