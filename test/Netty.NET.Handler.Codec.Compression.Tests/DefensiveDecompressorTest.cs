/*
 * Copyright 2026 The Netty Project
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
using Netty.NET.Buffer;

namespace Netty.NET.Handler.Codec.Compression.Tests;

// CLR: Decompressor is IDecompressor and status() is Status_() (see IDecompressor); the local and field delegate
// are @delegate (a C# keyword). IllegalStateException is InvalidOperationException; the thrown exceptions have
// exactly the asserted types, so assertThrows is Assert.Throws. The anonymous Executable is a lambda.
// assertDoesNotThrow(decompressor::close) is Assert.Null(Record.Exception(...)), which "Returns the exception that
// was thrown by the code; null, otherwise" (https://raw.githubusercontent.com/xunit/assert.xunit/main/Record.cs).
public class DefensiveDecompressorTest
{

    [Fact]
    public void EnforcesNeedInputState()
    {
        TestDecompressor @delegate = new TestDecompressor();
        DefensiveDecompressor decompressor = new DefensiveDecompressor(@delegate);
        ByteBuf input = Unpooled.Buffer(1).WriteByte(1);

        Assert.Equal(IDecompressor.Status.NEED_INPUT, decompressor.Status_());
        decompressor.AddInput(input);

        Assert.Same(input, @delegate.input);
        Assert.True(@delegate.addInputCalled);
        input.Release();
        decompressor.Close();
    }

    [Fact]
    public void EnforcesNeedOutputState()
    {
        TestDecompressor @delegate = new TestDecompressor();
        @delegate.status = IDecompressor.Status.NEED_OUTPUT;
        ByteBuf output = Unpooled.Buffer(1).WriteByte(1);
        @delegate.output = output;
        DefensiveDecompressor decompressor = new DefensiveDecompressor(@delegate);

        Assert.Equal(IDecompressor.Status.NEED_OUTPUT, decompressor.Status_());
        Assert.Same(output, decompressor.TakeOutput());
        Assert.True(@delegate.takeOutputCalled);
        output.Release();
        decompressor.Close();
    }

    [Fact]
    public void RejectsCallsInWrongState()
    {
        TestDecompressor @delegate = new TestDecompressor();
        DefensiveDecompressor decompressor = new DefensiveDecompressor(@delegate);
        ByteBuf input = Unpooled.Buffer(1).WriteByte(1);

        Assert.Equal(IDecompressor.Status.NEED_INPUT, decompressor.Status_());
        Assert.Throws<InvalidOperationException>(() => decompressor.TakeOutput());

        @delegate.status = IDecompressor.Status.NEED_OUTPUT;
        Assert.Equal(IDecompressor.Status.NEED_OUTPUT, decompressor.Status_());
        Assert.Throws<InvalidOperationException>(() => decompressor.AddInput(input));

        decompressor.Close();
    }

    [Fact]
    public void MarksFailedWhenStatusThrows()
    {
        TestDecompressor @delegate = new TestDecompressor();
        @delegate.statusException = new DecompressionException("status failed");
        DefensiveDecompressor decompressor = new DefensiveDecompressor(@delegate);

        Assert.Same(@delegate.statusException, Assert.Throws<DecompressionException>(() => decompressor.Status_()));
        Assert.Throws<InvalidOperationException>(() => decompressor.Status_());
        decompressor.Close();
    }

    [Fact]
    public void MarksFailedWhenOperationThrows()
    {
        TestDecompressor @delegate = new TestDecompressor();
        @delegate.addInputException = new DecompressionException("addInput failed");
        DefensiveDecompressor decompressor = new DefensiveDecompressor(@delegate);
        ByteBuf input = Unpooled.Buffer(1).WriteByte(1);

        Assert.Equal(IDecompressor.Status.NEED_INPUT, decompressor.Status_());
        Assert.Same(@delegate.addInputException, Assert.Throws<DecompressionException>(
                () =>
                {
                    decompressor.AddInput(input);
                }));
        Assert.Throws<InvalidOperationException>(() => decompressor.Status_());

        input.Release();
        decompressor.Close();
    }

    [Fact]
    public void CloseIdempotent()
    {
        TestDecompressor @delegate = new TestDecompressor();
        DefensiveDecompressor decompressor = new DefensiveDecompressor(@delegate);

        decompressor.Close();

        Assert.True(@delegate.closeCalled);
        Assert.Throws<InvalidOperationException>(() => decompressor.Status_());

        Assert.Null(Record.Exception(decompressor.Close));
    }

    private sealed class TestDecompressor : IDecompressor
    {
        internal IDecompressor.Status status = IDecompressor.Status.NEED_INPUT;
        internal DecompressionException statusException;
        internal DecompressionException addInputException;
        internal ByteBuf input;
        internal ByteBuf output;
        internal bool addInputCalled;
        internal bool takeOutputCalled;
        internal bool closeCalled;

        public IDecompressor.Status Status_()
        {
            if (statusException != null)
            {
                throw statusException;
            }

            return status;
        }

        public void AddInput(ByteBuf buf)
        {
            addInputCalled = true;
            if (addInputException != null)
            {
                throw addInputException;
            }

            input = buf;
        }

        public void EndOfInput()
        {
        }

        public ByteBuf TakeOutput()
        {
            takeOutputCalled = true;
            return output;
        }

        public void Close()
        {
            closeCalled = true;
        }
    }
}
