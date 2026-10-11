/*
 * Copyright 2020 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System;

namespace Netty.NET.Buffer.Search;

/**
 * Implements <a href="https://en.wikipedia.org/wiki/Bitap_algorithm">Bitap</a> string search algorithm.
 * Use static {@link AbstractSearchProcessorFactory#newBitapSearchProcessorFactory}
 * to create an instance of this factory.
 * Use {@link BitapSearchProcessorFactory#newSearchProcessor} to get an instance of {@link io.netty.util.ByteProcessor}
 * implementation for performing the actual search.
 * @see AbstractSearchProcessorFactory
 */
public class BitapSearchProcessorFactory : AbstractSearchProcessorFactory
{
    private readonly ulong[] _bitMasks = new ulong[256];
    private readonly ulong _successBit;

    internal BitapSearchProcessorFactory(ReadOnlySpan<byte> needle)
    {
        if (needle.IsEmpty || needle.Length > 64)
            throw new ArgumentException("Search pattern length must be between 1 and 64 bytes.", nameof(needle));
        ulong bit = 1;
        foreach (byte value in needle)
        {
            _bitMasks[value] |= bit;
            bit <<= 1;
        }
        _successBit = 1UL << (needle.Length - 1);
    }

    /**
     * Returns a new {@link Processor}.
     */
    public override Processor NewSearchProcessor() => new(_bitMasks, _successBit);

    public sealed class Processor : ISearchProcessor
    {
        private readonly ulong[] _bitMasks;
        private readonly ulong _successBit;
        private ulong _currentMask;

        internal Processor(ulong[] bitMasks, ulong successBit)
        { _bitMasks = bitMasks; _successBit = successBit; }

        public bool Process(byte value)
        {
            _currentMask = ((_currentMask << 1) | 1UL) & _bitMasks[value];
            return (_currentMask & _successBit) == 0;
        }

        public void Reset() => _currentMask = 0;
    }
}
