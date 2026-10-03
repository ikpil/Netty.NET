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

/**
 * Copyright (c) 2004-2011 QOS.ch
 * All rights reserved.
 * <p>
 * Permission is hereby granted, free  of charge, to any person obtaining
 * a  copy  of this  software  and  associated  documentation files  (the
 * "Software"), to  deal in  the Software without  restriction, including
 * without limitation  the rights to  use, copy, modify,  merge, publish,
 * distribute,  sublicense, and/or sell  copies of  the Software,  and to
 * permit persons to whom the Software  is furnished to do so, subject to
 * the following conditions:
 * <p>
 * The  above  copyright  notice  and  this permission  notice  shall  be
 * included in all copies or substantial portions of the Software.
 * <p>
 * THE  SOFTWARE IS  PROVIDED  "AS  IS", WITHOUT  WARRANTY  OF ANY  KIND,
 * EXPRESS OR  IMPLIED, INCLUDING  BUT NOT LIMITED  TO THE  WARRANTIES OF
 * MERCHANTABILITY,    FITNESS    FOR    A   PARTICULAR    PURPOSE    AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
 * LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
 * OF CONTRACT, TORT OR OTHERWISE,  ARISING FROM, OUT OF OR IN CONNECTION
 * WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */


using System;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common.Tests.Internal.Logging;

public class MessageFormatterTest
{
    [Fact]
    public void TestNull()
    {
        string result = MessageFormatter.Format(null, 1).GetMessage();
        Assert.Null(result);
    }

    [Fact]
    public void NullParametersShouldBeHandledWithoutBarfing()
    {
        string result = MessageFormatter.Format("Value is {}.", null).GetMessage();
        Assert.Equal("Value is null.", result);

        result = MessageFormatter.Format("Val1 is {}, val2 is {}.", null, null).GetMessage();
        Assert.Equal("Val1 is null, val2 is null.", result);

        result = MessageFormatter.Format("Val1 is {}, val2 is {}.", 1, null).GetMessage();
        Assert.Equal("Val1 is 1, val2 is null.", result);

        result = MessageFormatter.Format("Val1 is {}, val2 is {}.", null, 2).GetMessage();
        Assert.Equal("Val1 is null, val2 is 2.", result);

        result = MessageFormatter.ArrayFormat(
            "Val1 is {}, val2 is {}, val3 is {}", new object[] { null, null, null }).GetMessage();
        Assert.Equal("Val1 is null, val2 is null, val3 is null", result);

        result = MessageFormatter.ArrayFormat(
            "Val1 is {}, val2 is {}, val3 is {}", new object[] { null, 2, 3 }).GetMessage();
        Assert.Equal("Val1 is null, val2 is 2, val3 is 3", result);

        result = MessageFormatter.ArrayFormat(
            "Val1 is {}, val2 is {}, val3 is {}", new object[] { null, null, 3 }).GetMessage();
        Assert.Equal("Val1 is null, val2 is null, val3 is 3", result);
    }

    [Fact]
    public void VerifyOneParameterIsHandledCorrectly()
    {
        string result = MessageFormatter.Format("Value is {}.", 3).GetMessage();
        Assert.Equal("Value is 3.", result);

        result = MessageFormatter.Format("Value is {", 3).GetMessage();
        Assert.Equal("Value is {", result);

        result = MessageFormatter.Format("{} is larger than 2.", 3).GetMessage();
        Assert.Equal("3 is larger than 2.", result);

        result = MessageFormatter.Format("No subst", 3).GetMessage();
        Assert.Equal("No subst", result);

        result = MessageFormatter.Format("Incorrect {subst", 3).GetMessage();
        Assert.Equal("Incorrect {subst", result);

        result = MessageFormatter.Format("Value is {bla} {}", 3).GetMessage();
        Assert.Equal("Value is {bla} 3", result);

        result = MessageFormatter.Format("Escaped \\{} subst", 3).GetMessage();
        Assert.Equal("Escaped {} subst", result);

        result = MessageFormatter.Format("{Escaped", 3).GetMessage();
        Assert.Equal("{Escaped", result);

        result = MessageFormatter.Format("\\{}Escaped", 3).GetMessage();
        Assert.Equal("{}Escaped", result);

        result = MessageFormatter.Format("File name is {{}}.", "App folder.zip").GetMessage();
        Assert.Equal("File name is {App folder.zip}.", result);

        // escaping the escape character
        result = MessageFormatter.Format("File name is C:\\\\{}.", "App folder.zip").GetMessage();
        Assert.Equal("File name is C:\\App folder.zip.", result);
    }

    [Fact]
    public void TestTwoParameters()
    {
        string result = MessageFormatter.Format("Value {} is smaller than {}.", 1, 2).GetMessage();
        Assert.Equal("Value 1 is smaller than 2.", result);

        result = MessageFormatter.Format("Value {} is smaller than {}", 1, 2).GetMessage();
        Assert.Equal("Value 1 is smaller than 2", result);

        result = MessageFormatter.Format("{}{}", 1, 2).GetMessage();
        Assert.Equal("12", result);

        result = MessageFormatter.Format("Val1={}, Val2={", 1, 2).GetMessage();
        Assert.Equal("Val1=1, Val2={", result);

        result = MessageFormatter.Format("Value {} is smaller than \\{}", 1, 2).GetMessage();
        Assert.Equal("Value 1 is smaller than {}", result);

        result = MessageFormatter.Format("Value {} is smaller than \\{} tail", 1, 2).GetMessage();
        Assert.Equal("Value 1 is smaller than {} tail", result);

        result = MessageFormatter.Format("Value {} is smaller than \\{", 1, 2).GetMessage();
        Assert.Equal("Value 1 is smaller than \\{", result);

        result = MessageFormatter.Format("Value {} is smaller than {tail", 1, 2).GetMessage();
        Assert.Equal("Value 1 is smaller than {tail", result);

        result = MessageFormatter.Format("Value \\{} is smaller than {}", 1, 2).GetMessage();
        Assert.Equal("Value {} is smaller than 1", result);
    }

    class TestObject
    {
        public override string ToString()
        {
            throw new Exception("a");
        }
    }

    [Fact]
    public void TestExceptionIn_toString()
    {
        var o = new TestObject();
        string result = MessageFormatter.Format("Troublesome object {}", o).GetMessage();
        Assert.Equal("Troublesome object [FAILED ToString()]", result);
    }

    [Fact]
    public void TestNullArray()
    {
        string msg0 = "msg0";
        string msg1 = "msg1 {}";
        string msg2 = "msg2 {} {}";
        string msg3 = "msg3 {} {} {}";

        object[] args = null;

        string result = MessageFormatter.ArrayFormat(msg0, args).GetMessage();
        Assert.Equal(msg0, result);

        result = MessageFormatter.ArrayFormat(msg1, args).GetMessage();
        Assert.Equal(msg1, result);

        result = MessageFormatter.ArrayFormat(msg2, args).GetMessage();
        Assert.Equal(msg2, result);

        result = MessageFormatter.ArrayFormat(msg3, args).GetMessage();
        Assert.Equal(msg3, result);
    }

    // tests the case when the parameters are supplied in a single array
    [Fact]
    public void TestArrayFormat()
    {
        object[] ia0 = { 1, 2, 3 };

        string result = MessageFormatter.ArrayFormat("Value {} is smaller than {} and {}.", ia0).GetMessage();
        Assert.Equal("Value 1 is smaller than 2 and 3.", result);

        result = MessageFormatter.ArrayFormat("{}{}{}", ia0).GetMessage();
        Assert.Equal("123", result);

        result = MessageFormatter.ArrayFormat("Value {} is smaller than {}.", ia0).GetMessage();
        Assert.Equal("Value 1 is smaller than 2.", result);

        result = MessageFormatter.ArrayFormat("Value {} is smaller than {}", ia0).GetMessage();
        Assert.Equal("Value 1 is smaller than 2", result);

        result = MessageFormatter.ArrayFormat("Val={}, {, Val={}", ia0).GetMessage();
        Assert.Equal("Val=1, {, Val=2", result);

        result = MessageFormatter.ArrayFormat("Val={}, {, Val={}", ia0).GetMessage();
        Assert.Equal("Val=1, {, Val=2", result);

        result = MessageFormatter.ArrayFormat("Val1={}, Val2={", ia0).GetMessage();
        Assert.Equal("Val1=1, Val2={", result);
    }

    [Fact]
    public void TestArrayValues()
    {
        object[] p1 = { 2, 3 };

        string result = MessageFormatter.Format("{}{}", 1, p1).GetMessage();
        Assert.Equal("1[2, 3]", result);

        // Integer[]
        result = MessageFormatter.ArrayFormat("{}{}", new object[] { "a", p1 }).GetMessage();
        Assert.Equal("a[2, 3]", result);

        // byte[]
        result = MessageFormatter.ArrayFormat("{}{}", new object[] { "a", new byte[] { 1, 2 } }).GetMessage();
        Assert.Equal("a[1, 2]", result);

        // int[]
        result = MessageFormatter.ArrayFormat("{}{}", new object[] { "a", new int[] { 1, 2 } }).GetMessage();
        Assert.Equal("a[1, 2]", result);

        // float[]
        result = MessageFormatter.ArrayFormat("{}{}", new object[] { "a", new float[] { 1, 2 } }).GetMessage();
        Assert.Equal("a[1.0, 2.0]", result);

        // double[]
        result = MessageFormatter.ArrayFormat("{}{}", new object[] { "a", new double[] { 1, 2 } }).GetMessage();
        Assert.Equal("a[1.0, 2.0]", result);
    }

    [Fact]
    public void TestMultiDimensionalArrayValues()
    {
        object[] ia0 = { 1, 2, 3 };
        object[] ia1 = { 10, 20, 30 };

        object[][] multiIntegerA = { ia0, ia1 };
        string result = MessageFormatter.ArrayFormat("{}{}", new object[] { "a", multiIntegerA }).GetMessage();
        Assert.Equal("a[[1, 2, 3], [10, 20, 30]]", result);

        int[][] multiIntA = { [1, 2], [10, 20] };
        result = MessageFormatter.ArrayFormat("{}{}", new object[] { "a", multiIntA }).GetMessage();
        Assert.Equal("a[[1, 2], [10, 20]]", result);

        float[][] multiFloatA = { [1, 2], [10, 20] };
        result = MessageFormatter.ArrayFormat("{}{}", new object[] { "a", multiFloatA }).GetMessage();
        Assert.Equal("a[[1.0, 2.0], [10.0, 20.0]]", result);

        object[][] multiOA = { ia0, ia1 };
        result = MessageFormatter.ArrayFormat("{}{}", new object[] { "a", multiOA }).GetMessage();
        Assert.Equal("a[[1, 2, 3], [10, 20, 30]]", result);

        object[][][] _3DOA = { multiOA, multiOA };
        result = MessageFormatter.ArrayFormat("{}{}", new object[] { "a", _3DOA }).GetMessage();
        Assert.Equal("a[[[1, 2, 3], [10, 20, 30]], [[1, 2, 3], [10, 20, 30]]]", result);

        byte[] ba0 = { 0, (byte)sbyte.MaxValue, unchecked((byte)sbyte.MinValue) };
        short[] sa0 = { 0, short.MinValue, short.MaxValue };
        result = MessageFormatter.ArrayFormat("{}\\{}{}", new object[] { new object[] { ba0, sa0 }, ia1 }).GetMessage();
        Assert.Equal("[[0, 127, -128], [0, -32768, 32767]]{}[10, 20, 30]", result);
    }

    [Fact]
    public void TestCyclicArrays()
    {
        object[] cyclicA = new object[1];
        cyclicA[0] = cyclicA;
        Assert.Equal("[[...]]", MessageFormatter.ArrayFormat("{}", cyclicA).GetMessage());

        object[] a = new object[2];
        a[0] = 1;
        object[] c = { 3, a };
        object[] b = { 2, c };
        a[1] = b;
        Assert.Equal("1[2, [3, [1, [...]]]]",
            MessageFormatter.ArrayFormat("{}{}", a).GetMessage());
    }

    [Fact]
    public void TestArrayThrowable()
    {
        FormattingTuple ft;
        Exception t = new Exception();
        object[] ia = { 1, 2, 3, t };

        ft = MessageFormatter.ArrayFormat("Value {} is smaller than {} and {}.", ia);
        Assert.Equal("Value 1 is smaller than 2 and 3.", ft.GetMessage());
        Assert.Equal(t, ft.GetThrowable());

        ft = MessageFormatter.ArrayFormat("{}{}{}", ia);
        Assert.Equal("123", ft.GetMessage());
        Assert.Equal(t, ft.GetThrowable());

        ft = MessageFormatter.ArrayFormat("Value {} is smaller than {}.", ia);
        Assert.Equal("Value 1 is smaller than 2.", ft.GetMessage());
        Assert.Equal(t, ft.GetThrowable());

        ft = MessageFormatter.ArrayFormat("Value {} is smaller than {}", ia);
        Assert.Equal("Value 1 is smaller than 2", ft.GetMessage());
        Assert.Equal(t, ft.GetThrowable());

        ft = MessageFormatter.ArrayFormat("Val={}, {, Val={}", ia);
        Assert.Equal("Val=1, {, Val=2", ft.GetMessage());
        Assert.Equal(t, ft.GetThrowable());

        ft = MessageFormatter.ArrayFormat("Val={}, \\{, Val={}", ia);
        Assert.Equal("Val=1, \\{, Val=2", ft.GetMessage());
        Assert.Equal(t, ft.GetThrowable());

        ft = MessageFormatter.ArrayFormat("Val1={}, Val2={", ia);
        Assert.Equal("Val1=1, Val2={", ft.GetMessage());
        Assert.Equal(t, ft.GetThrowable());

        ft = MessageFormatter.ArrayFormat("Value {} is smaller than {} and {}.", ia);
        Assert.Equal("Value 1 is smaller than 2 and 3.", ft.GetMessage());
        Assert.Equal(t, ft.GetThrowable());

        ft = MessageFormatter.ArrayFormat("{}{}{}{}", ia);
        Assert.Equal("123" + t, ft.GetMessage());
        Assert.Null(ft.GetThrowable());
    }
}
