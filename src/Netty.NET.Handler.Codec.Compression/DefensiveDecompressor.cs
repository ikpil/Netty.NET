/*
 * Copyright 2025 The Netty Project
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
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Compression;

/**
 * Most decompressor implementations play fast and loose with {@link Decompressor} API contracts. This wrapper makes
 * sure callers follow that contract.
 */
// CLR: The field delegate is @delegate (a C# keyword). The nullable Status field is a nullable enum
// (IDecompressor.Status?): the nested type of the implemented interface is written IDecompressor.Status because a
// class does not inherit the nested types of its interfaces. IllegalStateException is InvalidOperationException
// and checkNotNull's NullPointerException is ArgumentNullException. The rethrow of catch (Exception e) {...; throw
// e; } is throw;, which rethrows the same exception object. In checkState, the string concatenation keeps Java's
// text for a null status ("null").
internal sealed class DefensiveDecompressor : IDecompressor
{
    private readonly IDecompressor @delegate;
    private IDecompressor.Status? status;
    private bool closed;
    private bool failed;

    internal DefensiveDecompressor(IDecompressor @delegate)
    {
        this.@delegate = ObjectUtil.CheckNotNull(@delegate, "delegate");
    }

    public IDecompressor.Status Status_()
    {
        CheckReady();

        try
        {
            status = @delegate.Status_();
        }
        catch (Exception)
        {
            failed = true;
            throw;
        }

        return status.Value;
    }

    public void AddInput(ByteBuf buf)
    {
        try
        {
            CheckReady();
            CheckState(IDecompressor.Status.NEED_INPUT);
        }
        catch (Exception)
        {
            buf.Release();
            throw;
        }

        try
        {
            @delegate.AddInput(buf);
        }
        catch (Exception)
        {
            failed = true;
            throw;
        }

        status = null;
    }

    public void EndOfInput()
    {
        CheckReady();
        CheckState(IDecompressor.Status.NEED_INPUT);

        try
        {
            @delegate.EndOfInput();
        }
        catch (Exception)
        {
            failed = true;
            throw;
        }

        status = null;
    }

    public ByteBuf TakeOutput()
    {
        CheckReady();
        CheckState(IDecompressor.Status.NEED_OUTPUT);

        ByteBuf @out;

        try
        {
            @out = @delegate.TakeOutput();
        }
        catch (Exception)
        {
            failed = true;
            throw;
        }

        status = null;
        return @out;
    }

    public void Close()
    {
        closed = true;
        @delegate.Close();
    }

    private void CheckReady()
    {
        if (closed)
        {
            throw new InvalidOperationException("Already closed");
        }
        if (failed)
        {
            throw new InvalidOperationException("Previous call failed");
        }
    }

    private void CheckState(IDecompressor.Status expected)
    {
        if (this.status != expected)
        {
            throw new InvalidOperationException("Not in expected state " + expected + ", was " +
                    (this.status?.ToString() ?? "null"));
        }
    }
}
