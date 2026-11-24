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
 * Shared API for various decompression algorithms. A decompressor reports its current status using {@link #status()}.
 * Status documentation lists which operations are permitted for which status. Callers must observe the current
 * status before invoking an operation that is only valid for that status.
 * <p>
 * All methods may throw exceptions. If an exception is thrown, no further operations are permitted, except for
 * {@link #close()}.
 * <p>
 * This API is still in progress as part of the decompression migration tracked by
 * <a href="https://github.com/netty/netty/issues/16743">#16743</a>.
 */
// CLR: the Java interface takes the I prefix and @UnstableApi is the Common [UnstableApi] attribute. The interface keeps
// the Java close() method as Close(), as Transport's IIoHandle does for AutoCloseable. Decompressors are used in
// try-with-resources, which calls close() "automatically when exiting a try-with-resources block"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/AutoCloseable.html); the C# counterpart is the
// using statement, which disposes "the acquired IDisposable instance" when control leaves its block
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/using). The interface therefore extends
// IDisposable and implements IDisposable.Dispose() as a call of Close(); an interface implements a member of a base
// interface with the explicit interface implementation syntax
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/interface). Implementations implement
// Close(), not Dispose(). status() is Status_(): a method named Status cannot be declared next to the nested Status
// enum ("The type 'type name' already contains a definition for 'identifier'",
// https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0102), and the nested type keeps its Java name, as
// HAProxyTLV.type() is Type_() in docs/codec-haproxy-porting.md. C# has no throws clauses.
[UnstableApi]
public interface IDecompressor : IDisposable
{
    /**
     * Get the current status. Like the other operations on this class, calling this method is not permitted if the
     * decompressor has failed or was closed.
     *
     * @return The current status
     */
    Status Status_();

    /**
     * Add a new input buffer. Only permitted for {@link Status#NEED_INPUT}.
     *
     * @param buf The input buffer. Buffer ownership transfers to the decompressor (also on exception).
     */
    void AddInput(ByteBuf buf);

    /**
     * Notify the decompressor that the end of input has been reached. Some implementations may flush remaining data or
     * throw an exception if the input is truncated, but most implementations do nothing. Only permitted for
     * {@link Status#NEED_INPUT}.
     */
    void EndOfInput();

    /**
     * Take a decompressed buffer from this decompressor. Only permitted for {@link Status#NEED_OUTPUT}. Buffer
     * ownership transfers to the caller.
     *
     * @return The decompressed buffer. May be empty.
     */
    ByteBuf TakeOutput();

    /**
     * Close this decompressor, cleaning up any associated resources. <b>This method is idempotent.</b>
     */
    void Close();

    // CLR: the using statement's Dispose() closes the decompressor (see the interface comment).
    void IDisposable.Dispose() => Close();

    /**
     * Current status of the decompressor. The status indicates which method may be called to make progress on
     * decompression.
     */
    [UnstableApi]
    public enum Status
    {
        /**
         * More input is required before decompression can proceed. Only calls to {@link #addInput} and
         * {@link #endOfInput()} are permitted.
         */
        NEED_INPUT,
        /**
         * Output must be consumed before more input can be received. Only calls to {@link #takeOutput()} are
         * permitted.
         */
        NEED_OUTPUT,
        /**
         * All data has been processed, and the format indicates that no more input may arrive. No further
         * decompression operations are permitted, except {@link Decompressor#close()} to release resources.
         */
        COMPLETE,
    }

    // CLR: the abstract class nested in the interface stays nested (C# interfaces may declare nested types,
    // https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/interface); its build(ByteBufAllocator)
    // takes the Buffer port's IByteBufAllocator.
    [UnstableApi]
    public abstract class AbstractDecompressorBuilder
    {

        protected AbstractDecompressorBuilder()
        {
        }

        public abstract IDecompressor Build(IByteBufAllocator allocator);
    }
}
