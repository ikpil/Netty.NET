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
 * Implements
 * <a href="https://en.wikipedia.org/wiki/Knuth%E2%80%93Morris%E2%80%93Pratt_algorithm">Knuth-Morris-Pratt</a>
 * string search algorithm.
 * Use static {@link AbstractSearchProcessorFactory#newKmpSearchProcessorFactory}
 * to create an instance of this factory.
 * Use {@link KmpSearchProcessorFactory#newSearchProcessor} to get an instance of {@link io.netty.util.ByteProcessor}
 * implementation for performing the actual search.
 * @see AbstractSearchProcessorFactory
 */
public class KmpSearchProcessorFactory : AbstractSearchProcessorFactory
{
    private readonly byte[] _needle;
    private readonly int[] _jumpTable;

    internal KmpSearchProcessorFactory(ReadOnlySpan<byte> needle)
    {
        if (needle.IsEmpty) throw new ArgumentException("Search pattern must not be empty.", nameof(needle));
        _needle = needle.ToArray();
        _jumpTable = new int[checked(needle.Length + 1)];
        int j = 0;
        for (int i = 1; i < needle.Length; i++)
        {
            while (j > 0 && needle[j] != needle[i]) j = _jumpTable[j];
            if (needle[j] == needle[i]) j++;
            _jumpTable[i + 1] = j;
        }
    }

    /**
     * Returns a new {@link Processor}.
     */
    public override Processor NewSearchProcessor() => new(_needle, _jumpTable);

    public sealed class Processor : ISearchProcessor
    {
        private readonly byte[] _needle;
        private readonly int[] _jumpTable;
        private int _currentPosition;

        internal Processor(byte[] needle, int[] jumpTable)
        { _needle = needle; _jumpTable = jumpTable; }

        public bool Process(byte value)
        {
            while (_currentPosition > 0 && _needle[_currentPosition] != value)
                _currentPosition = _jumpTable[_currentPosition];
            if (_needle[_currentPosition] == value) _currentPosition++;
            if (_currentPosition == _needle.Length)
            {
                _currentPosition = _jumpTable[_currentPosition];
                return false;
            }
            return true;
        }

        public void Reset() => _currentPosition = 0;
    }
}
