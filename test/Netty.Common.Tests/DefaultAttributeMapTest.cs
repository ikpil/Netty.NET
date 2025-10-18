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
namespace Netty.Common.Tests;
public class DefaultAttributeMapTest {

    private DefaultAttributeMap map;

    @BeforeEach
    public void setup() {
        map = new DefaultAttributeMap();
    }

    [Fact]
    public void testMapExists() {
        Assert.NotNull(map);
    }

    [Fact]
    public void testGetSetString() {
        AttributeKey<string> key = AttributeKey.valueOf("Nothing");
        IAttribute<string> one = map.attr(key);

        Assert.Same(one, map.attr(key));

        one.setIfAbsent("Whoohoo");
        Assert.Same("Whoohoo", one.get());

        one.setIfAbsent("What");
        assertNotSame("What", one.get());

        one.remove();
        assertNull(one.get());
    }

    [Fact]
    public void testGetSetInt() {
        AttributeKey<Integer> key = AttributeKey.valueOf("Nada");
        IAttribute<Integer> one = map.attr(key);

        Assert.Same(one, map.attr(key));

        one.setIfAbsent(3653);
        Assert.Equal(Integer.valueOf(3653), one.get());

        one.setIfAbsent(1);
        assertNotSame(1, one.get());

        one.remove();
        assertNull(one.get());
    }

    // See https://github.com/netty/netty/issues/2523
    [Fact]
    public void testSetRemove() {
        AttributeKey<Integer> key = AttributeKey.valueOf("key");

        IAttribute<Integer> attr = map.attr(key);
        attr.set(1);
        Assert.Same(1, attr.getAndRemove());

        IAttribute<Integer> attr2 = map.attr(key);
        attr2.set(2);
        Assert.Same(2, attr2.get());
        assertNotSame(attr, attr2);
    }

    [Fact]
    public void testHasAttrRemoved() {
        AttributeKey<Integer>[] keys = new AttributeKey[20];
        for (int i = 0; i < 20; i++) {
            keys[i] = AttributeKey.valueOf(Integer.toString(i));
        }
        for (int i = 10; i < 20; i++) {
            map.attr(keys[i]);
        }
        for (int i = 0; i < 10; i++) {
            map.attr(keys[i]);
        }
        for (int i = 10; i < 20; i++) {
            AttributeKey<Integer> key = AttributeKey.valueOf(Integer.toString(i));
            Assert.True(map.hasAttr(key));
            map.attr(key).remove();
            Assert.False(map.hasAttr(key));
        }
        for (int i = 0; i < 10; i++) {
            AttributeKey<Integer> key = AttributeKey.valueOf(Integer.toString(i));
            Assert.True(map.hasAttr(key));
            map.attr(key).remove();
            Assert.False(map.hasAttr(key));
        }
    }

    [Fact]
    public void testGetAndSetWithNull() {
        AttributeKey<Integer> key = AttributeKey.valueOf("key");

        IAttribute<Integer> attr = map.attr(key);
        attr.set(1);
        Assert.Same(1, attr.getAndSet(null));

        IAttribute<Integer> attr2 = map.attr(key);
        attr2.set(2);
        Assert.Same(2, attr2.get());
        Assert.Same(attr, attr2);
    }
}
