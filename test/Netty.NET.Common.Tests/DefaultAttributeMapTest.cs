/*
 * Copyright 2012 The Netty Project
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

using System.Runtime.CompilerServices;
using Netty.NET.Common;

namespace Netty.NET.Common.Tests;
public class DefaultAttributeMapTest {

    private DefaultAttributeMap map;

    public DefaultAttributeMapTest() {
        map = new DefaultAttributeMap();
    }

    [Fact]
    public void testMapExists() {
        Assert.NotNull(map);
    }

    [Fact]
    public void testGetSetString() {
        AttributeKey<string> key = AttributeKey.valueOf<string>("Nothing");
        IAttribute<string> one = map.attr(key);

        Assert.Same(one, map.attr(key));

        one.setIfAbsent("Whoohoo");
        Assert.Same("Whoohoo", one.get());

        one.setIfAbsent("What");
        Assert.NotSame("What", one.get());

        one.remove();
        Assert.Null(one.get());
    }

    [Fact]
    public void testGetSetInt() {
        AttributeKey<object> key = AttributeKey.valueOf<object>("Nada");
        IAttribute<object> one = map.attr(key);

        Assert.Same(one, map.attr(key));

        one.setIfAbsent(3653);
        Assert.Equal(3653, (int)one.get());

        one.setIfAbsent(1);
        Assert.NotSame(1, one.get());

        one.remove();
        Assert.Null(one.get());
    }

    // See https://github.com/netty/netty/issues/2523
    [Fact]
    public void testSetRemove() {
        AttributeKey<object> key = AttributeKey.valueOf<object>("key");

        IAttribute<object> attr = map.attr(key);
        object one = 1;
        attr.set(one);
        Assert.Same(one, attr.getAndRemove());

        IAttribute<object> attr2 = map.attr(key);
        object two = 2;
        attr2.set(two);
        Assert.Same(two, attr2.get());
        Assert.NotSame(attr, attr2);
    }

    [Fact]
    public void testHasAttrRemoved() {
        AttributeKey<object>[] keys = new AttributeKey<object>[20];
        for (int i = 0; i < 20; i++) {
            keys[i] = AttributeKey.valueOf<object>(i.ToString());
        }
        for (int i = 10; i < 20; i++) {
            map.attr(keys[i]);
        }
        for (int i = 0; i < 10; i++) {
            map.attr(keys[i]);
        }
        for (int i = 10; i < 20; i++) {
            AttributeKey<object> key = AttributeKey.valueOf<object>(i.ToString());
            Assert.True(map.hasAttr(key));
            map.attr(key).remove();
            Assert.False(map.hasAttr(key));
        }
        for (int i = 0; i < 10; i++) {
            AttributeKey<object> key = AttributeKey.valueOf<object>(i.ToString());
            Assert.True(map.hasAttr(key));
            map.attr(key).remove();
            Assert.False(map.hasAttr(key));
        }
    }

    [Fact]
    public void testGetAndSetWithNull() {
        AttributeKey<object> key = AttributeKey.valueOf<object>("key");

        IAttribute<object> attr = map.attr(key);
        object one = 1;
        attr.set(one);
        Assert.Same(one, attr.getAndSet(null));
        Assert.Null(attr.get());

        IAttribute<object> attr2 = map.attr(key);
        object two = 2;
        attr2.set(two);
        Assert.Same(two, attr2.get());
        Assert.Same(attr, attr2);
    }
}
