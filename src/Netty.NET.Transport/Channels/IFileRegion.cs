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

using System.IO;
using Netty.NET.Common;

namespace Netty.NET.Transport.Channels;

/**
 * A region of a file that is sent via a {@link Channel} which supports
 * <a href="https://en.wikipedia.org/wiki/Zero-copy">zero-copy file transfer</a>.
 *
 * <h3>Check your operating system and JDK / JRE</h3>
 *
 * If your operating system (or JDK / JRE) does not support zero-copy file
 * transfer, sending a file with {@link FileRegion} might fail or yield worse
 * performance.  For example, sending a large file doesn't work well in Windows.
 *
 * <h3>Not all transports support it</h3>
 */
// CLR: Stream is the portable fallback target, not a guarantee of OS zero-copy.
// A future socket backend may specialize file transfer. Callers retain the region until the operation finishes.
public interface IFileRegion : IReferenceCounted
{
    /**
     * Returns the offset in the file where the transfer began.
     */
    long Position();
    /**
     * Returns the bytes which was transferred already.
     *
     * @deprecated Use {@link #transferred()} instead.
     */
    /**
     * Returns the bytes which was transferred already.
     * <p>
     * Note: some asynchronous transports (such as the {@code io_uring} transport when falling
     * back to a chunked send for non-{@link DefaultFileRegion} implementations) advance this
     * counter when bytes have been queued for submission, which may be before they reach the
     * peer. If the channel is closed or the write fails after queuing, the reported value may
     * overstate the number of bytes actually delivered.
     */
    // CLR: the misspelled deprecated alias is folded into the correctly spelled method.
    long Transferred();
    /**
     * Returns the number of bytes to transfer.
     */
    long Count();
    /**
     * Transfers the content of this file region to the specified channel.
     *
     * @param target    the destination of the transfer
     * @param position  the relative offset of the file where the transfer
     *                  begins from.  For example, <tt>0</tt> will make the
     *                  transfer start from {@link #position()}th byte and
     *                  <tt>{@link #count()} - 1</tt> will make the last
     *                  byte of the region transferred.
     */
    long TransferTo(Stream target, long position);
    new IFileRegion Retain();
    new IFileRegion Retain(int increment);
    new IFileRegion Touch();
    new IFileRegion Touch(object hint);
}
