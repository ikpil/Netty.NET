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

using System;
using System.Collections.Generic;
using System.Linq;
using Netty.NET.Common;

namespace Netty.NET.Common.Tests;

public class ConstantPoolTest
{
    public class TestConstant : AbstractConstant<TestConstant>
    {
        public TestConstant(int id, string name)
            : base(id, name)
        {
        }
    }

    public class TestContentPool : ConstantPool<TestConstant>
    {
        protected override TestConstant NewConstant(int id, string name)
        {
            return new TestConstant(id, name);
        }
    }

    private static readonly ConstantPool<TestConstant> pool = new TestContentPool();

    [Fact]
    public void TestCannotProvideNullName()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            pool.ValueOf(null);
        });
    }

    [Fact]
    //@SuppressWarnings("RedundantStringConstructorCall")
    public void TestUniqueness()
    {
        TestConstant a = pool.ValueOf(new string("Leroy"));
        TestConstant b = pool.ValueOf(new string("Leroy"));
        Assert.Same(a, b);
    }

    [Fact]
    public void TestIdUniqueness()
    {
        TestConstant one = pool.ValueOf("one");
        TestConstant two = pool.ValueOf("two");
        Assert.NotEqual(one.Id, two.Id);
    }

    [Fact]
    public void TestCompare()
    {
        TestConstant a = pool.ValueOf("a_alpha");
        TestConstant b = pool.ValueOf("b_beta");
        TestConstant c = pool.ValueOf("c_gamma");
        TestConstant d = pool.ValueOf("d_delta");
        TestConstant e = pool.ValueOf("e_epsilon");

        ISet<TestConstant> set = new SortedSet<TestConstant>();
        set.Add(b);
        set.Add(c);
        set.Add(e);
        set.Add(d);
        set.Add(a);

        var array = set.ToList();
        Assert.Equal(5, array.Count);

        // Sort by name
        array.Sort((o1, o2) => string.Compare(o1.Name, o2.Name, StringComparison.Ordinal));

        Assert.Same(a, array[0]);
        Assert.Same(b, array[1]);
        Assert.Same(c, array[2]);
        Assert.Same(d, array[3]);
        Assert.Same(e, array[4]);
    }

    [Fact]
    public void TestComposedName()
    {
        TestConstant a = pool.ValueOf(typeof(object), "A");
        Assert.Equal(typeof(object).FullName + "#A", a.Name);
    }
}
