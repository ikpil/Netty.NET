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
    public void TestMapExists() {
        Assert.NotNull(map);
    }

    [Fact]
    public void TestGetSetString() {
        AttributeKey<string> key = AttributeKey.ValueOf<string>("Nothing");
        IAttribute<string> one = map.Attr(key);

        Assert.Same(one, map.Attr(key));

        one.SetIfAbsent("Whoohoo");
        Assert.Same("Whoohoo", one.Get());

        one.SetIfAbsent("What");
        Assert.NotSame("What", one.Get());

        one.Remove();
        Assert.Null(one.Get());
    }

    [Fact]
    public void TestGetSetInt() {
        AttributeKey<object> key = AttributeKey.ValueOf<object>("Nada");
        IAttribute<object> one = map.Attr(key);

        Assert.Same(one, map.Attr(key));

        one.SetIfAbsent(3653);
        Assert.Equal(3653, (int)one.Get());

        one.SetIfAbsent(1);
        Assert.NotSame(1, one.Get());

        one.Remove();
        Assert.Null(one.Get());
    }

    // See https://github.com/netty/netty/issues/2523
    [Fact]
    public void TestSetRemove() {
        AttributeKey<object> key = AttributeKey.ValueOf<object>("key");

        IAttribute<object> attr = map.Attr(key);
        object one = 1;
        attr.Set(one);
        Assert.Same(one, attr.GetAndRemove());

        IAttribute<object> attr2 = map.Attr(key);
        object two = 2;
        attr2.Set(two);
        Assert.Same(two, attr2.Get());
        Assert.NotSame(attr, attr2);
    }

    [Fact]
    public void TestHasAttrRemoved() {
        AttributeKey<object>[] keys = new AttributeKey<object>[20];
        for (int i = 0; i < 20; i++) {
            keys[i] = AttributeKey.ValueOf<object>(i.ToString());
        }
        for (int i = 10; i < 20; i++) {
            map.Attr(keys[i]);
        }
        for (int i = 0; i < 10; i++) {
            map.Attr(keys[i]);
        }
        for (int i = 10; i < 20; i++) {
            AttributeKey<object> key = AttributeKey.ValueOf<object>(i.ToString());
            Assert.True(map.HasAttr(key));
            map.Attr(key).Remove();
            Assert.False(map.HasAttr(key));
        }
        for (int i = 0; i < 10; i++) {
            AttributeKey<object> key = AttributeKey.ValueOf<object>(i.ToString());
            Assert.True(map.HasAttr(key));
            map.Attr(key).Remove();
            Assert.False(map.HasAttr(key));
        }
    }

    [Fact]
    public void TestGetAndSetWithNull() {
        AttributeKey<object> key = AttributeKey.ValueOf<object>("key");

        IAttribute<object> attr = map.Attr(key);
        object one = 1;
        attr.Set(one);
        Assert.Same(one, attr.GetAndSet(null));
        Assert.Null(attr.Get());

        IAttribute<object> attr2 = map.Attr(key);
        object two = 2;
        attr2.Set(two);
        Assert.Same(two, attr2.Get());
        Assert.Same(attr, attr2);
    }
}
