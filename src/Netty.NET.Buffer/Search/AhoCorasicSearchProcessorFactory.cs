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
using System.Collections.Generic;

namespace Netty.NET.Buffer.Search;

/**
 * Implements <a href="https://en.wikipedia.org/wiki/Aho%E2%80%93Corasick_algorithm">Aho–Corasick</a>
 * string search algorithm.
 * Use static {@link AbstractMultiSearchProcessorFactory#newAhoCorasicSearchProcessorFactory}
 * to create an instance of this factory.
 * Use {@link AhoCorasicSearchProcessorFactory#newSearchProcessor} to get an instance of
 * {@link io.netty.util.ByteProcessor} implementation for performing the actual search.
 * @see AbstractMultiSearchProcessorFactory
 */
public class AhoCorasicSearchProcessorFactory : AbstractMultiSearchProcessorFactory
{
    private const int BitsPerSymbol = 8;
    private const int AlphabetSize = 1 << BitsPerSymbol;
    private readonly int[] _jumpTable;
    private readonly int[] _matchForNeedleId;

    internal AhoCorasicSearchProcessorFactory(byte[][] needles)
    {
        ArgumentNullException.ThrowIfNull(needles);
        foreach (byte[] needle in needles)
        {
            ArgumentNullException.ThrowIfNull(needle, nameof(needles));
            if (needle.Length == 0) throw new ArgumentException("Needle must be non empty.", nameof(needles));
        }
        (_jumpTable, _matchForNeedleId) = BuildTrie(needles);
        LinkSuffixes();
        for (int i = 0; i < _jumpTable.Length; i++)
            if (_matchForNeedleId[_jumpTable[i] >> BitsPerSymbol] >= 0)
                _jumpTable[i] = -_jumpTable[i];
    }

    private static (int[] Jumps, int[] Matches) BuildTrie(byte[][] needles)
    {
        var jumps = new List<int>(AlphabetSize);
        for (int i = 0; i < AlphabetSize; i++) jumps.Add(-1);
        var matches = new List<int> { -1 };
        for (int needleId = 0; needleId < needles.Length; needleId++)
        {
            int position = 0;
            foreach (byte value in needles[needleId])
            {
                int next = position + value;
                if (jumps[next] == -1)
                {
                    int newPosition = jumps.Count;
                    jumps.EnsureCapacity(checked(newPosition + AlphabetSize));
                    jumps[next] = newPosition;
                    for (int i = 0; i < AlphabetSize; i++) jumps.Add(-1);
                    matches.Add(-1);
                }
                position = jumps[next];
            }
            matches[position >> BitsPerSymbol] = needleId;
        }
        return (jumps.ToArray(), matches.ToArray());
    }

    private void LinkSuffixes()
    {
        var queue = new Queue<int>(); queue.Enqueue(0);
        int[] suffixLinks = new int[_matchForNeedleId.Length]; Array.Fill(suffixLinks, -1);
        while (queue.Count != 0)
        {
            int v = queue.Dequeue(), vPosition = v >> BitsPerSymbol;
            int u = suffixLinks[vPosition] == -1 ? 0 : suffixLinks[vPosition];
            if (_matchForNeedleId[vPosition] == -1)
                _matchForNeedleId[vPosition] = _matchForNeedleId[u >> BitsPerSymbol];
            for (int ch = 0; ch < AlphabetSize; ch++)
            {
                int vIndex = v | ch, uIndex = u | ch;
                int jumpV = _jumpTable[vIndex], jumpU = _jumpTable[uIndex];
                if (jumpV != -1)
                {
                    suffixLinks[jumpV >> BitsPerSymbol] = v > 0 && jumpU != -1 ? jumpU : 0;
                    queue.Enqueue(jumpV);
                }
                else _jumpTable[vIndex] = jumpU != -1 ? jumpU : 0;
            }
        }
    }

    /**
     * Returns a new {@link Processor}.
     */
    public override Processor NewSearchProcessor() => new(_jumpTable, _matchForNeedleId);

    public sealed class Processor : IMultiSearchProcessor
    {
        private readonly int[] _jumpTable;
        private readonly int[] _matchForNeedleId;
        private int _currentPosition;

        internal Processor(int[] jumpTable, int[] matchForNeedleId)
        { _jumpTable = jumpTable; _matchForNeedleId = matchForNeedleId; }

        public bool Process(byte value)
        {
            _currentPosition = _jumpTable[_currentPosition | value];
            if (_currentPosition < 0)
            {
                _currentPosition = -_currentPosition;
                return false;
            }
            return true;
        }

        public int FoundNeedleId => _matchForNeedleId[_currentPosition >> BitsPerSymbol];
        public void Reset() => _currentPosition = 0;
    }
}
