/*
 * Copyright 2021 The Netty Project
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
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Quic.Tests;

// CLR: getClass().getSuperclass().getDeclaredFields() is the declared instance fields of the generic base
// QuicCodecBuilder<TestQuicCodecBuilder> (Type.BaseType with BindingFlags.DeclaredOnly), and Field.set is
// FieldInfo.SetValue, which also sets the readonly field server as Java's setAccessible(true) does for the final field.
// The boxed field types are the nullable values of the CLR builder (Boolean bool?, Integer int?, Long long?,
// QuicCongestionControlAlgorithm QuicCongestionControlAlgorithm?), FlushStrategy is IFlushStrategy, Executor IExecutor,
// boolean/int bool/int, and Function<QuicChannel, ? extends QuicSslEngine> is Func<IQuicChannel, QuicSslEngine>, for which
// Function.identity() is a delegate that returns its argument when it is a QuicSslEngine (no function maps a QuicChannel
// to itself as an engine; the test only compares the reference). Integer.MIN_VALUE/Long.MIN_VALUE are
// int.MinValue/long.MinValue and IllegalArgumentException is ArgumentException. AssertJ's
// usingRecursiveComparison().isEqualTo(original) is AssertRecursiveEquals, which compares every instance field of both
// builders, recursing into objects and comparing arrays element by element. The overridden build(...) takes the CLR
// MsQuicConfig in place of QuicheConfig (docs/codec-classes-quic-porting.md, Stage 2); it is internal abstract as the Java
// method is package-private.
public class QuicCodecBuilderTest
{

    [Fact]
    public void TestCopyConstructor()
    {
        TestQuicCodecBuilder original = new TestQuicCodecBuilder();
        Init(original);
        TestQuicCodecBuilder copy = new TestQuicCodecBuilder(original);
        AssertRecursiveEquals(original, copy);
    }

    private static void Init(TestQuicCodecBuilder builder)
    {
        FieldInfo[] fields = builder.GetType().BaseType.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

        foreach (FieldInfo field in fields)
        {
            ModifyField(builder, field);
        }
    }

    private static void ModifyField(TestQuicCodecBuilder builder, FieldInfo field)
    {
        Type clazz = field.FieldType;
        if (typeof(bool?) == clazz)
        {
            field.SetValue(builder, (bool?) true);
        }
        else if (typeof(int?) == clazz)
        {
            field.SetValue(builder, (int?) int.MinValue);
        }
        else if (typeof(long?) == clazz)
        {
            field.SetValue(builder, (long?) long.MinValue);
        }
        else if (typeof(QuicCongestionControlAlgorithm?) == clazz)
        {
            field.SetValue(builder, (QuicCongestionControlAlgorithm?) QuicCongestionControlAlgorithm.CUBIC);
        }
        else if (typeof(IFlushStrategy) == clazz)
        {
            field.SetValue(builder, IFlushStrategy.AfterNumBytes(10));
        }
        else if (typeof(Func<IQuicChannel, QuicSslEngine>) == clazz)
        {
            field.SetValue(builder, (Func<IQuicChannel, QuicSslEngine>) (channel => channel as QuicSslEngine));
        }
        else if (typeof(bool) == clazz)
        {
            field.SetValue(builder, true);
        }
        else if (typeof(int) == clazz)
        {
            field.SetValue(builder, -1);
        }
        else if (typeof(byte[]) == clazz)
        {
            field.SetValue(builder, new byte[16]);
        }
        else if (typeof(IExecutor) == clazz)
        {
            field.SetValue(builder, ImmediateExecutor.INSTANCE);
        }
        else
        {
            throw new ArgumentException("Unknown field type " + clazz);
        }
    }

    // CLR-only: AssertJ's usingRecursiveComparison().isEqualTo(expected) for the builders.
    private static void AssertRecursiveEquals(object expected, object actual)
    {
        AssertRecursiveEquals(expected, actual, "", new HashSet<(object, object)>());
    }

    private static void AssertRecursiveEquals(object expected, object actual, string path,
                                              HashSet<(object, object)> visited)
    {
        if (expected == null || actual == null)
        {
            Assert.True(expected == null && actual == null, "Field '" + path + "' differs: " + expected + " != " + actual);
            return;
        }

        Type type = expected.GetType();
        Assert.True(type == actual.GetType(), "Field '" + path + "' has different types: " + type + " != " +
                actual.GetType());

        if (ReferenceEquals(expected, actual) || type.IsPrimitive || type.IsEnum || expected is string ||
            expected is Delegate)
        {
            Assert.True(Equals(expected, actual), "Field '" + path + "' differs: " + expected + " != " + actual);
            return;
        }

        if (expected is IEnumerable expectedItems && type.IsArray)
        {
            List<object> left = new List<object>();
            List<object> right = new List<object>();

            foreach (object item in expectedItems)
            {
                left.Add(item);
            }

            foreach (object item in (IEnumerable) actual)
            {
                right.Add(item);
            }

            Assert.True(left.Count == right.Count, "Field '" + path + "' has a different length");

            for (int i = 0; i < left.Count; i++)
            {
                AssertRecursiveEquals(left[i], right[i], path + "[" + i + "]", visited);
            }

            return;
        }

        if (!visited.Add((expected, actual)))
        {
            return;
        }

        for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
        {
            foreach (FieldInfo field in t.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                AssertRecursiveEquals(field.GetValue(expected), field.GetValue(actual),
                        path.Length == 0 ? field.Name : path + "." + field.Name, visited);
            }
        }
    }

    private sealed class TestQuicCodecBuilder : QuicCodecBuilder<TestQuicCodecBuilder>
    {

        internal TestQuicCodecBuilder() : base(true)
        {
        }

        internal TestQuicCodecBuilder(TestQuicCodecBuilder builder) : base(builder)
        {
        }

        public override TestQuicCodecBuilder Clone()
        {
            // no-op
            return null;
        }

        internal override IChannelHandler Build(
                MsQuicConfig config,
                Func<IQuicChannel, QuicSslEngine> sslContextProvider,
                IExecutor sslTaskExecutor,
                int localConnIdLength,
                IFlushStrategy flushStrategy)
        {
            // no-op
            return null;
        }
    }
}
