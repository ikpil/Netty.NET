/*
 * Copyright 2014 The Netty Project
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

namespace Netty.NET.Handler.Codec.Compression;

/**
 * DivSufSort suffix array generator.<br>
 *
 * Based on <a href="https://code.google.com/p/libdivsufsort/">libdivsufsort</a> 1.2.3 patched to support Bzip2.<br>
 * This is a simple conversion of the original C with two minor bugfixes applied (see "BUGFIX"
 * comments within the class). Documentation within the class is largely absent.
 */
// CLR: a mechanical port: the package-private final class is an internal sealed class, the private static nested classes
// StackEntry, PartitionResult and TRBudget are private nested classes with internal fields, the methods are PascalCase
// (BUCKET_B/BUCKET_BSTAR keep their names), and the locals, fields (SA, T, n) and statements keep the Java names and
// order. The int arithmetic wraps as in Java (the projects are unchecked), ~, >> and >>>= are the same operators, and
// the operator precedence of every expression (for example n >> 16 & 0xff + 16 in trLog) is the same in both
// languages. Byte-sign notes: T is the block, a byte[] that Java reads signed and C# unsigned. Every ordering
// comparison and index masks the value (T[x] & 0xff) and the others compare two bytes for equality, so both languages
// sort identically. constructBWT and bwt store T[...] into the int[] SA (the bwtBlock): Java stores -128..127, C#
// 0..255; the only reader, Bzip2MTFAndRLE2StageEncoder, masks it (bwtBlock[i] & 0xff), so the transform output is the
// same (a byte-sign spot without an observable difference).
internal sealed class Bzip2DivSufSort
{

    private const int STACK_SIZE = 64;
    private const int BUCKET_A_SIZE = 256;
    private const int BUCKET_B_SIZE = 65536;
    private const int SS_BLOCKSIZE = 1024;
    private const int INSERTIONSORT_THRESHOLD = 8;

    private static readonly int[] LOG_2_TABLE = {
        -1, 0, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4,
         5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5,
         6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6,
         6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6,
         7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
         7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
         7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
         7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7
    };

    private readonly int[] SA;
    private readonly byte[] T;
    private readonly int n;

    /**
     * @param block The input array
     * @param bwtBlock The output array
     * @param blockLength The length of the input data
     */
    internal Bzip2DivSufSort(byte[] block, int[] bwtBlock, int blockLength)
    {
        T = block;
        SA = bwtBlock;
        n = blockLength;
    }

    private static void SwapElements(int[] array1, int idx1, int[] array2, int idx2)
    {
        int temp = array1[idx1];
        array1[idx1] = array2[idx2];
        array2[idx2] = temp;
    }

    private int SsCompare(int p1, int p2, int depth)
    {
        int[] SA = this.SA;
        byte[] T = this.T;

        // pointers within T
        int U1n = SA[p1 + 1] + 2;
        int  U2n = SA[p2 + 1] + 2;

        int U1 = depth + SA[p1];
        int U2 = depth + SA[p2];

        while (U1 < U1n && U2 < U2n && T[U1] == T[U2])
        {
            ++U1;
            ++U2;
        }

        return U1 < U1n ?
                   U2 < U2n ? (T[U1] & 0xff) - (T[U2] & 0xff) : 1
                 : U2 < U2n ? -1 : 0;
    }

    private int SsCompareLast(int pa, int p1, int p2, int depth, int size)
    {
        int[] SA = this.SA;
        byte[] T = this.T;

        int U1 = depth + SA[p1];
        int U2 = depth + SA[p2];
        int U1n = size;
        int U2n = SA[p2 + 1] + 2;

        while (U1 < U1n && U2 < U2n && T[U1] == T[U2])
        {
            ++U1;
            ++U2;
        }

        if (U1 < U1n)
        {
            return U2 < U2n ? (T[U1] & 0xff) - (T[U2] & 0xff) : 1;
        }
        if (U2 == U2n)
        {
            return 1;
        }

        U1 %= size;
        U1n = SA[pa] + 2;
        while (U1 < U1n && U2 < U2n && T[U1] == T[U2])
        {
            ++U1;
            ++U2;
        }

        return U1 < U1n ?
                   U2 < U2n ? (T[U1] & 0xff) - (T[U2] & 0xff) : 1
                 : U2 < U2n ? -1 : 0;
    }

    private void SsInsertionSort(int pa, int first, int last, int depth)
    {
        int[] SA = this.SA;

        int i, j; // pointer within SA
        int t;
        int r;

        for (i = last - 2; first <= i; --i)
        {
            for (t = SA[i], j = i + 1; 0 < (r = SsCompare(pa + t, pa + SA[j], depth));)
            {
                do
                {
                    SA[j - 1] = SA[j];
                } while (++j < last && SA[j] < 0);
                if (last <= j)
                {
                    break;
                }
            }
            if (r == 0)
            {
                SA[j] = ~SA[j];
            }
            SA[j - 1] = t;
        }
    }

    private void SsFixdown(int td, int pa, int sa, int i, int size)
    {
        int[] SA = this.SA;
        byte[] T = this.T;

        int j, k;
        int v;
        int c, d, e;

        for (v = SA[sa + i], c = T[td + SA[pa + v]] & 0xff; (j = 2 * i + 1) < size; SA[sa + i] = SA[sa + k], i = k)
        {
            d = T[td + SA[pa + SA[sa + (k = j++)]]] & 0xff;
            if (d < (e = T[td + SA[pa + SA[sa + j]]] & 0xff))
            {
                k = j;
                d = e;
            }
            if (d <= c)
            {
                break;
            }
        }
        SA[sa + i] = v;
    }

    private void SsHeapSort(int td, int pa, int sa, int size)
    {
        int[] SA = this.SA;
        byte[] T = this.T;

        int i, m;
        int t;

        m = size;
        if (size % 2 == 0)
        {
            m--;
            if ((T[td + SA[pa + SA[sa + m / 2]]] & 0xff) < (T[td + SA[pa + SA[sa + m]]] & 0xff))
            {
                SwapElements(SA, sa + m, SA, sa + m / 2);
            }
        }

        for (i = m / 2 - 1; 0 <= i; --i)
        {
            SsFixdown(td, pa, sa, i, m);
        }

        if (size % 2 == 0)
        {
            SwapElements(SA, sa, SA, sa + m);
            SsFixdown(td, pa, sa, 0, m);
        }

        for (i = m - 1; 0 < i; --i)
        {
            t = SA[sa];
            SA[sa] = SA[sa + i];
            SsFixdown(td, pa, sa, 0, i);
            SA[sa + i] = t;
        }
    }

    private int SsMedian3(int td, int pa, int v1, int v2, int v3)
    {
        int[] SA = this.SA;
        byte[] T = this.T;

        int T_v1 = T[td + SA[pa + SA[v1]]] & 0xff;
        int T_v2 = T[td + SA[pa + SA[v2]]] & 0xff;
        int T_v3 = T[td + SA[pa + SA[v3]]] & 0xff;

        if (T_v1 > T_v2)
        {
            int temp = v1;
            v1 = v2;
            v2 = temp;
            int T_vtemp = T_v1;
            T_v1 = T_v2;
            T_v2 = T_vtemp;
        }
        if (T_v2 > T_v3)
        {
            if (T_v1 > T_v3)
            {
                return v1;
            }
            return v3;
        }
        return v2;
    }

    private int SsMedian5(int td, int pa, int v1, int v2, int v3, int v4, int v5)
    {
        int[] SA = this.SA;
        byte[] T = this.T;

        int T_v1 = T[td + SA[pa + SA[v1]]] & 0xff;
        int T_v2 = T[td + SA[pa + SA[v2]]] & 0xff;
        int T_v3 = T[td + SA[pa + SA[v3]]] & 0xff;
        int T_v4 = T[td + SA[pa + SA[v4]]] & 0xff;
        int T_v5 = T[td + SA[pa + SA[v5]]] & 0xff;
        int temp;
        int T_vtemp;

        if (T_v2 > T_v3)
        {
            temp = v2;
            v2 = v3;
            v3 = temp;
            T_vtemp = T_v2;
            T_v2 = T_v3;
            T_v3 = T_vtemp;
        }
        if (T_v4 > T_v5)
        {
            temp = v4;
            v4 = v5;
            v5 = temp;
            T_vtemp = T_v4;
            T_v4 = T_v5;
            T_v5 = T_vtemp;
        }
        if (T_v2 > T_v4)
        {
            temp = v2;
            v4 = temp;
            T_vtemp = T_v2;
            T_v4 = T_vtemp;
            temp = v3;
            v3 = v5;
            v5 = temp;
            T_vtemp = T_v3;
            T_v3 = T_v5;
            T_v5 = T_vtemp;
        }
        if (T_v1 > T_v3)
        {
            temp = v1;
            v1 = v3;
            v3 = temp;
            T_vtemp = T_v1;
            T_v1 = T_v3;
            T_v3 = T_vtemp;
        }
        if (T_v1 > T_v4)
        {
            temp = v1;
            v4 = temp;
            T_vtemp = T_v1;
            T_v4 = T_vtemp;
            v3 = v5;
            T_v3 = T_v5;
        }
        if (T_v3 > T_v4)
        {
            return v4;
        }
        return v3;
    }

    private int SsPivot(int td, int pa, int first, int last)
    {
        int middle;
        int t;

        t = last - first;
        middle = first + t / 2;

        if (t <= 512)
        {
            if (t <= 32)
            {
                return SsMedian3(td, pa, first, middle, last - 1);
            }
            t >>= 2;
            return SsMedian5(td, pa, first, first + t, middle, last - 1 - t, last - 1);
        }
        t >>= 3;
        return SsMedian3(
                td, pa,
                SsMedian3(td, pa, first, first + t, first + (t << 1)),
                SsMedian3(td, pa, middle - t, middle, middle + t),
                SsMedian3(td, pa, last - 1 - (t << 1), last - 1 - t, last - 1)
        );
    }

    private static int SsLog(int n)
    {
        return (n & 0xff00) != 0 ?
                  8 + LOG_2_TABLE[n >> 8 & 0xff]
                : LOG_2_TABLE[n & 0xff];
    }

    private int SsSubstringPartition(int pa, int first, int last, int depth)
    {
        int[] SA = this.SA;

        int a, b;
        int t;

        for (a = first - 1, b = last;;)
        {
            while (++a < b && (SA[pa + SA[a]] + depth >= SA[pa + SA[a] + 1] + 1))
            {
                SA[a] = ~SA[a];
            }
            --b;
            while (a < b && (SA[pa + SA[b]] + depth < SA[pa + SA[b] + 1] + 1))
            {
                --b;
            }

            if (b <= a)
            {
                break;
            }
            t = ~SA[b];
            SA[b] = SA[a];
            SA[a] = t;
        }
        if (first < a)
        {
            SA[first] = ~SA[first];
        }
        return a;
    }

    private class StackEntry
    {
        internal readonly int a;
        internal readonly int b;
        internal readonly int c;
        internal readonly int d;

        internal StackEntry(int a, int b, int c, int d)
        {
            this.a = a;
            this.b = b;
            this.c = c;
            this.d = d;
        }
    }

    private void SsMultiKeyIntroSort(int pa, int first, int last, int depth)
    {
        int[] SA = this.SA;
        byte[] T = this.T;

        StackEntry[] stack = new StackEntry[STACK_SIZE];

        int Td;
        int a, b, c, d, e, f;
        int s, t;
        int ssize;
        int limit;
        int v, x = 0;

        for (ssize = 0, limit = SsLog(last - first);;)
        {
            if (last - first <= INSERTIONSORT_THRESHOLD)
            {
                if (1 < last - first)
                {
                    SsInsertionSort(pa, first, last, depth);
                }
                if (ssize == 0)
                {
                    return;
                }
                StackEntry entry = stack[--ssize];
                first = entry.a;
                last = entry.b;
                depth = entry.c;
                limit = entry.d;
                continue;
            }

            Td = depth;
            if (limit-- == 0)
            {
                SsHeapSort(Td, pa, first, last - first);
            }
            if (limit < 0)
            {
                for (a = first + 1, v = T[Td + SA[pa + SA[first]]] & 0xff; a < last; ++a)
                {
                    if ((x = T[Td + SA[pa + SA[a]]] & 0xff) != v)
                    {
                        if (1 < a - first)
                        {
                            break;
                        }
                        v = x;
                        first = a;
                    }
                }
                if ((T[Td + SA[pa + SA[first]] - 1] & 0xff) < v)
                {
                    first = SsSubstringPartition(pa, first, a, depth);
                }
                if (a - first <= last - a)
                {
                    if (1 < a - first)
                    {
                        stack[ssize++] = new StackEntry(a, last, depth, -1);
                        last = a;
                        depth += 1;
                        limit = SsLog(a - first);
                    }
                    else
                    {
                        first = a;
                        limit = -1;
                    }
                }
                else
                {
                    if (1 < last - a)
                    {
                        stack[ssize++] = new StackEntry(first, a, depth + 1, SsLog(a - first));
                        first = a;
                        limit = -1;
                    }
                    else
                    {
                        last = a;
                        depth += 1;
                        limit = SsLog(a - first);
                    }
                }
                continue;
            }

            a = SsPivot(Td, pa, first, last);
            v = T[Td + SA[pa + SA[a]]] & 0xff;
            SwapElements(SA, first, SA, a);

            b = first + 1;
            while (b < last && (x = T[Td + SA[pa + SA[b]]] & 0xff) == v)
            {
                ++b;
            }
            if ((a = b) < last && x < v)
            {
                while (++b < last && (x = T[Td + SA[pa + SA[b]]] & 0xff) <= v)
                {
                    if (x == v)
                    {
                        SwapElements(SA, b, SA, a);
                        ++a;
                    }
                }
            }

            c = last - 1;
            while (b < c && (x = T[Td + SA[pa + SA[c]]] & 0xff) == v)
            {
                --c;
            }
            if (b < (d = c) && x > v)
            {
                while (b < --c && (x = T[Td + SA[pa + SA[c]]] & 0xff) >= v)
                {
                    if (x == v)
                    {
                        SwapElements(SA, c, SA, d);
                        --d;
                    }
                }
            }
            while (b < c)
            {
                SwapElements(SA, b, SA, c);
                while (++b < c && (x = T[Td + SA[pa + SA[b]]] & 0xff) <= v)
                {
                    if (x == v)
                    {
                        SwapElements(SA, b, SA, a);
                        ++a;
                    }
                }
                while (b < --c && (x = T[Td + SA[pa + SA[c]]] & 0xff) >= v)
                {
                    if (x == v)
                    {
                        SwapElements(SA, c, SA, d);
                        --d;
                    }
                }
            }

            if (a <= d)
            {
                c = b - 1;

                if ((s = a - first) > (t = b - a))
                {
                    s = t;
                }
                for (e = first, f = b - s; 0 < s; --s, ++e, ++f)
                {
                    SwapElements(SA, e, SA, f);
                }
                if ((s = d - c) > (t = last - d - 1))
                {
                    s = t;
                }
                for (e = b, f = last - s; 0 < s; --s, ++e, ++f)
                {
                    SwapElements(SA, e, SA, f);
                }

                a = first + (b - a);
                c = last - (d - c);
                b = v <= (T[Td + SA[pa + SA[a]] - 1] & 0xff) ? a : SsSubstringPartition(pa, a, c, depth);

                if (a - first <= last - c)
                {
                    if (last - c <= c - b)
                    {
                        stack[ssize++] = new StackEntry(b, c, depth + 1, SsLog(c - b));
                        stack[ssize++] = new StackEntry(c, last, depth, limit);
                        last = a;
                    }
                    else if (a - first <= c - b)
                    {
                        stack[ssize++] = new StackEntry(c, last, depth, limit);
                        stack[ssize++] = new StackEntry(b, c, depth + 1, SsLog(c - b));
                        last = a;
                    }
                    else
                    {
                        stack[ssize++] = new StackEntry(c, last, depth, limit);
                        stack[ssize++] = new StackEntry(first, a, depth, limit);
                        first = b;
                        last = c;
                        depth += 1;
                        limit = SsLog(c - b);
                    }
                }
                else
                {
                    if (a - first <= c - b)
                    {
                        stack[ssize++] = new StackEntry(b, c, depth + 1, SsLog(c - b));
                        stack[ssize++] = new StackEntry(first, a, depth, limit);
                        first = c;
                    }
                    else if (last - c <= c - b)
                    {
                        stack[ssize++] = new StackEntry(first, a, depth, limit);
                        stack[ssize++] = new StackEntry(b, c, depth + 1, SsLog(c - b));
                        first = c;
                    }
                    else
                    {
                        stack[ssize++] = new StackEntry(first, a, depth, limit);
                        stack[ssize++] = new StackEntry(c, last, depth, limit);
                        first = b;
                        last = c;
                        depth += 1;
                        limit = SsLog(c - b);
                    }
                }
            }
            else
            {
                limit += 1;
                if ((T[Td + SA[pa + SA[first]] - 1] & 0xff) < v)
                {
                    first = SsSubstringPartition(pa, first, last, depth);
                    limit = SsLog(last - first);
                }
                depth += 1;
            }
        }
    }

    private static void SsBlockSwap(int[] array1, int first1,
                                    int[] array2, int first2, int size)
    {
        int a, b;
        int i;
        for (i = size, a = first1, b = first2; 0 < i; --i, ++a, ++b)
        {
            SwapElements(array1, a, array2, b);
        }
    }

    private void SsMergeForward(int pa, int[] buf, int bufoffset,
                                int first, int middle, int last, int depth)
    {
        int[] SA = this.SA;

        int bufend;
        int i, j, k;
        int t;
        int r;

        bufend = bufoffset + (middle - first) - 1;
        SsBlockSwap(buf, bufoffset, SA, first, middle - first);

        for (t = SA[first], i = first, j = bufoffset, k = middle;;)
        {
            r = SsCompare(pa + buf[j], pa + SA[k], depth);
            if (r < 0)
            {
                do
                {
                    SA[i++] = buf[j];
                    if (bufend <= j)
                    {
                        buf[j] = t;
                        return;
                    }
                    buf[j++] = SA[i];
                } while (buf[j] < 0);
            }
            else if (r > 0)
            {
                do
                {
                    SA[i++] = SA[k];
                    SA[k++] = SA[i];
                    if (last <= k)
                    {
                        while (j < bufend)
                        {
                            SA[i++] = buf[j]; buf[j++] = SA[i];
                        }
                        SA[i] = buf[j];
                        buf[j] = t;
                        return;
                    }
                } while (SA[k] < 0);
            }
            else
            {
                SA[k] = ~SA[k];
                do
                {
                    SA[i++] = buf[j];
                    if (bufend <= j)
                    {
                        buf[j] = t;
                        return;
                    }
                    buf[j++] = SA[i];
                } while (buf[j] < 0);

                do
                {
                    SA[i++] = SA[k];
                    SA[k++] = SA[i];
                    if (last <= k)
                    {
                        while (j < bufend)
                        {
                            SA[i++] = buf[j];
                            buf[j++] = SA[i];
                        }
                        SA[i] = buf[j];
                        buf[j] = t;
                        return;
                    }
                } while (SA[k] < 0);
            }
        }
    }

    private void SsMergeBackward(int pa, int[] buf, int bufoffset,
                                 int first, int middle, int last, int depth)
    {
        int[] SA = this.SA;

        int p1, p2;
        int bufend;
        int i, j, k;
        int t;
        int r;
        int x;

        bufend = bufoffset + (last - middle);
        SsBlockSwap(buf, bufoffset, SA, middle, last - middle);

        x = 0;
        if (buf[bufend - 1] < 0)
        {
            x |=  1;
            p1 = pa + ~buf[bufend - 1];
        }
        else
        {
            p1 = pa +  buf[bufend - 1];
        }
        if (SA[middle - 1] < 0)
        {
            x |=  2;
            p2 = pa + ~SA[middle - 1];
        }
        else
        {
            p2 = pa +  SA[middle - 1];
        }
        for (t = SA[last - 1], i = last - 1, j = bufend - 1, k = middle - 1;;)
        {

            r = SsCompare(p1, p2, depth);
            if (r > 0)
            {
                if ((x & 1) != 0)
                {
                    do
                    {
                        SA[i--] = buf[j];
                        buf[j--] = SA[i];
                    } while (buf[j] < 0);
                    x ^= 1;
                }
                SA[i--] = buf[j];
                if (j <= bufoffset)
                {
                    buf[j] = t;
                    return;
                }
                buf[j--] = SA[i];

                if (buf[j] < 0)
                {
                    x |=  1;
                    p1 = pa + ~buf[j];
                }
                else
                {
                    p1 = pa +  buf[j];
                }
            }
            else if (r < 0)
            {
                if ((x & 2) != 0)
                {
                    do
                    {
                        SA[i--] = SA[k];
                        SA[k--] = SA[i];
                    } while (SA[k] < 0);
                    x ^= 2;
                }
                SA[i--] = SA[k];
                SA[k--] = SA[i];
                if (k < first)
                {
                    while (bufoffset < j)
                    {
                        SA[i--] = buf[j];
                        buf[j--] = SA[i];
                    }
                    SA[i] = buf[j];
                    buf[j] = t;
                    return;
                }

                if (SA[k] < 0)
                {
                    x |=  2;
                    p2 = pa + ~SA[k];
                }
                else
                {
                    p2 = pa +  SA[k];
                }
            }
            else
            {
                if ((x & 1) != 0)
                {
                    do
                    {
                        SA[i--] = buf[j];
                        buf[j--] = SA[i];
                    } while (buf[j] < 0);
                    x ^= 1;
                }
                SA[i--] = ~buf[j];
                if (j <= bufoffset)
                {
                    buf[j] = t;
                    return;
                }
                buf[j--] = SA[i];

                if ((x & 2) != 0)
                {
                    do
                    {
                        SA[i--] = SA[k];
                        SA[k--] = SA[i];
                    } while (SA[k] < 0);
                    x ^= 2;
                }
                SA[i--] = SA[k];
                SA[k--] = SA[i];
                if (k < first)
                {
                    while (bufoffset < j)
                    {
                        SA[i--] = buf[j];
                        buf[j--] = SA[i];
                    }
                    SA[i] = buf[j];
                    buf[j] = t;
                    return;
                }

                if (buf[j] < 0)
                {
                    x |=  1;
                    p1 = pa + ~buf[j];
                }
                else
                {
                    p1 = pa +  buf[j];
                }
                if (SA[k] < 0)
                {
                    x |=  2;
                    p2 = pa + ~SA[k];
                }
                else
                {
                    p2 = pa +  SA[k];
                }
            }
        }
    }

    private static int GetIDX(int a)
    {
        return 0 <= a ? a : ~a;
    }

    private void SsMergeCheckEqual(int pa, int depth, int a)
    {
        int[] SA = this.SA;

        if (0 <= SA[a] && SsCompare(pa + GetIDX(SA[a - 1]), pa + SA[a], depth) == 0)
        {
            SA[a] = ~SA[a];
        }
    }

    private void SsMerge(int pa, int first, int middle, int last, int[] buf,
                         int bufoffset, int bufsize, int depth)
    {
        int[] SA = this.SA;

        StackEntry[] stack = new StackEntry[STACK_SIZE];

        int i, j;
        int m, len, half;
        int ssize;
        int check, next;

        for (check = 0, ssize = 0;;)
        {

            if (last - middle <= bufsize)
            {
                if (first < middle && middle < last)
                {
                    SsMergeBackward(pa, buf, bufoffset, first, middle, last, depth);
                }

                if ((check & 1) != 0)
                {
                    SsMergeCheckEqual(pa, depth, first);
                }
                if ((check & 2) != 0)
                {
                    SsMergeCheckEqual(pa, depth, last);
                }
                if (ssize == 0)
                {
                    return;
                }
                StackEntry entry = stack[--ssize];
                first = entry.a;
                middle = entry.b;
                last = entry.c;
                check = entry.d;
                continue;
            }

            if (middle - first <= bufsize)
            {
                if (first < middle)
                {
                    SsMergeForward(pa, buf, bufoffset, first, middle, last, depth);
                }
                if ((check & 1) != 0)
                {
                    SsMergeCheckEqual(pa, depth, first);
                }
                if ((check & 2) != 0)
                {
                    SsMergeCheckEqual(pa, depth, last);
                }
                if (ssize == 0)
                {
                    return;
                }
                StackEntry entry = stack[--ssize];
                first = entry.a;
                middle = entry.b;
                last = entry.c;
                check = entry.d;
                continue;
            }

            for (m = 0, len = Math.Min(middle - first, last - middle), half = len >> 1;
                    0 < len;
                    len = half, half >>= 1)
            {

                if (SsCompare(pa + GetIDX(SA[middle + m + half]),
                        pa + GetIDX(SA[middle - m - half - 1]), depth) < 0)
                {
                    m += half + 1;
                    half -= (len & 1) ^ 1;
                }
            }

            if (0 < m)
            {
                SsBlockSwap(SA, middle - m, SA, middle, m);
                i = j = middle;
                next = 0;
                if (middle + m < last)
                {
                    if (SA[middle + m] < 0)
                    {
                        while (SA[i - 1] < 0)
                        {
                            --i;
                        }
                        SA[middle + m] = ~SA[middle + m];
                    }
                    for (j = middle; SA[j] < 0;)
                    {
                        ++j;
                    }
                    next = 1;
                }
                if (i - first <= last - j)
                {
                    stack[ssize++] = new StackEntry(j, middle + m, last, (check &  2) | (next & 1));
                    middle -= m;
                    last = i;
                    check &= 1;
                }
                else
                {
                    if (i == middle && middle == j)
                    {
                        next <<= 1;
                    }
                    stack[ssize++] = new StackEntry(first, middle - m, i, (check & 1) | (next & 2));
                    first = j;
                    middle += m;
                    check = (check & 2) | (next & 1);
                }
            }
            else
            {
                if ((check & 1) != 0)
                {
                    SsMergeCheckEqual(pa, depth, first);
                }
                SsMergeCheckEqual(pa, depth, middle);
                if ((check & 2) != 0)
                {
                    SsMergeCheckEqual(pa, depth, last);
                }
                if (ssize == 0)
                {
                    return;
                }
                StackEntry entry = stack[--ssize];
                first = entry.a;
                middle = entry.b;
                last = entry.c;
                check = entry.d;
            }
        }
    }

    private void SubStringSort(int pa, int first, int last,
                               int[] buf, int bufoffset, int bufsize,
                               int depth, bool lastsuffix, int size)
    {
        int[] SA = this.SA;

        int a, b;
        int[] curbuf;
        int curbufoffset;
        int i, j, k;
        int curbufsize;

        if (lastsuffix)
        {
            ++first;
        }
        for (a = first, i = 0; a + SS_BLOCKSIZE < last; a += SS_BLOCKSIZE, ++i)
        {
            SsMultiKeyIntroSort(pa, a, a + SS_BLOCKSIZE, depth);
            curbuf = SA;
            curbufoffset = a + SS_BLOCKSIZE;
            curbufsize = last - (a + SS_BLOCKSIZE);
            if (curbufsize <= bufsize)
            {
                curbufsize = bufsize;
                curbuf = buf;
                curbufoffset = bufoffset;
            }
            for (b = a, k = SS_BLOCKSIZE, j = i; (j & 1) != 0; b -= k, k <<= 1, j >>>= 1)
            {
                SsMerge(pa, b - k, b, b + k, curbuf, curbufoffset, curbufsize, depth);
            }
        }

        SsMultiKeyIntroSort(pa, a, last, depth);

        for (k = SS_BLOCKSIZE; i != 0; k <<= 1, i >>= 1)
        {
            if ((i & 1) != 0)
            {
                SsMerge(pa, a - k, a, last, buf, bufoffset, bufsize, depth);
                a -= k;
            }
        }

        if (lastsuffix)
        {
            int r;
            for (a = first, i = SA[first - 1], r = 1;
                    a < last && (SA[a] < 0 || 0 < (r = SsCompareLast(pa, pa + i, pa + SA[a], depth, size)));
                    ++a)
            {
                SA[a - 1] = SA[a];
            }
            if (r == 0)
            {
                SA[a] = ~SA[a];
            }
            SA[a - 1] = i;
        }
    }

    /*----------------------------------------------------------------------------*/

    private int TrGetC(int isa, int isaD, int isaN, int p)
    {
        return isaD + p < isaN ?
                SA[isaD + p]
              : SA[isa + ((isaD - isa + p) % (isaN - isa))];
    }

    private void TrFixdown(int isa, int isaD, int isaN, int sa, int i, int size)
    {
        int[] SA = this.SA;

        int j, k;
        int v;
        int c, d, e;

        for (v = SA[sa + i], c = TrGetC(isa, isaD, isaN, v); (j = 2 * i + 1) < size; SA[sa + i] = SA[sa + k], i = k)
        {
            k = j++;
            d = TrGetC(isa, isaD, isaN, SA[sa + k]);
            if (d < (e = TrGetC(isa, isaD, isaN, SA[sa + j])))
            {
                k = j;
                d = e;
            }
            if (d <= c)
            {
                break;
            }
        }
        SA[sa + i] = v;
    }

    private void TrHeapSort(int isa, int isaD, int isaN, int sa, int size)
    {
        int[] SA = this.SA;

        int i, m;
        int t;

        m = size;
        if (size % 2 == 0)
        {
            m--;
            if (TrGetC(isa, isaD, isaN, SA[sa + m / 2]) < TrGetC(isa, isaD, isaN, SA[sa + m]))
            {
                SwapElements(SA, sa + m, SA, sa + m / 2);
            }
        }

        for (i = m / 2 - 1; 0 <= i; --i)
        {
            TrFixdown(isa, isaD, isaN, sa, i, m);
        }

        if (size % 2 == 0)
        {
            SwapElements(SA, sa, SA, sa + m);
            TrFixdown(isa, isaD, isaN, sa, 0, m);
        }

        for (i = m - 1; 0 < i; --i)
        {
            t = SA[sa];
            SA[sa] = SA[sa + i];
            TrFixdown(isa, isaD, isaN, sa, 0, i);
            SA[sa + i] = t;
        }
    }

    private void TrInsertionSort(int isa, int isaD, int isaN, int first, int last)
    {
        int[] SA = this.SA;

        int a, b;
        int t, r;

        for (a = first + 1; a < last; ++a)
        {
            for (t = SA[a], b = a - 1; 0 > (r = TrGetC(isa, isaD, isaN, t) - TrGetC(isa, isaD, isaN, SA[b]));)
            {
                do
                {
                    SA[b + 1] = SA[b];
                } while (first <= --b && SA[b] < 0);
                if (b < first)
                {
                    break;
                }
            }
            if (r == 0)
            {
                SA[b] = ~SA[b];
            }
            SA[b + 1] = t;
        }
    }

    // CLR: the Java int literals 0xffff0000 and 0xff000000 are negative ints; in C# an unsuffixed hexadecimal literal above
    // int.MaxValue is a uint ("the first of the following types in which its value can be represented: int, uint, long,
    // ulong", https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types),
    // so they are written unchecked((int) ...) to keep the Java int masks.
    private static int TrLog(int n)
    {
        return (n & unchecked((int) 0xffff0000)) != 0 ?
                  (n & unchecked((int) 0xff000000)) != 0 ? 24 + LOG_2_TABLE[n >> 24 & 0xff] : LOG_2_TABLE[n >> 16 & 0xff + 16]
                : (n & 0x0000ff00) != 0 ?  8 + LOG_2_TABLE[n >>  8 & 0xff] : LOG_2_TABLE[n & 0xff];
    }

    private int TrMedian3(int isa, int isaD, int isaN, int v1, int v2, int v3)
    {
        int[] SA = this.SA;

        int SA_v1 = TrGetC(isa, isaD, isaN, SA[v1]);
        int SA_v2 = TrGetC(isa, isaD, isaN, SA[v2]);
        int SA_v3 = TrGetC(isa, isaD, isaN, SA[v3]);

        if (SA_v1 > SA_v2)
        {
            int temp = v1;
            v1 = v2;
            v2 = temp;
            int SA_vtemp = SA_v1;
            SA_v1 = SA_v2;
            SA_v2 = SA_vtemp;
        }
        if (SA_v2 > SA_v3)
        {
            if (SA_v1 > SA_v3)
            {
                return v1;
            }
            return v3;
        }

        return v2;
    }

    private int TrMedian5(int isa, int isaD, int isaN, int v1, int v2, int v3, int v4, int v5)
    {
        int[] SA = this.SA;

        int SA_v1 = TrGetC(isa, isaD, isaN, SA[v1]);
        int SA_v2 = TrGetC(isa, isaD, isaN, SA[v2]);
        int SA_v3 = TrGetC(isa, isaD, isaN, SA[v3]);
        int SA_v4 = TrGetC(isa, isaD, isaN, SA[v4]);
        int SA_v5 = TrGetC(isa, isaD, isaN, SA[v5]);
        int temp;
        int SA_vtemp;

        if (SA_v2 > SA_v3)
        {
            temp = v2;
            v2 = v3;
            v3 = temp;
            SA_vtemp = SA_v2;
            SA_v2 = SA_v3;
            SA_v3 = SA_vtemp;
        }
        if (SA_v4 > SA_v5)
        {
            temp = v4;
            v4 = v5;
            v5 = temp;
            SA_vtemp = SA_v4;
            SA_v4 = SA_v5;
            SA_v5 = SA_vtemp;
        }
        if (SA_v2 > SA_v4)
        {
            temp = v2;
            v4 = temp;
            SA_vtemp = SA_v2;
            SA_v4 = SA_vtemp;
            temp = v3;
            v3 = v5;
            v5 = temp;
            SA_vtemp = SA_v3;
            SA_v3 = SA_v5;
            SA_v5 = SA_vtemp;
        }
        if (SA_v1 > SA_v3)
        {
            temp = v1;
            v1 = v3;
            v3 = temp;
            SA_vtemp = SA_v1;
            SA_v1 = SA_v3;
            SA_v3 = SA_vtemp;
        }
        if (SA_v1 > SA_v4)
        {
            temp = v1;
            v4 = temp;
            SA_vtemp = SA_v1;
            SA_v4 = SA_vtemp;
            v3 = v5;
            SA_v3 = SA_v5;
        }
        if (SA_v3 > SA_v4)
        {
            return v4;
        }
        return v3;
    }

    private int TrPivot(int isa, int isaD, int isaN, int first, int last)
    {
        int middle;
        int t;

        t = last - first;
        middle = first + t / 2;

        if (t <= 512)
        {
            if (t <= 32)
            {
                return TrMedian3(isa, isaD, isaN, first, middle, last - 1);
            }
            t >>= 2;
            return TrMedian5(
                    isa, isaD, isaN,
                    first, first + t,
                    middle,
                    last - 1 - t, last - 1
            );
        }
        t >>= 3;
        return TrMedian3(
                isa, isaD, isaN,
                TrMedian3(isa, isaD, isaN, first, first + t, first + (t << 1)),
                TrMedian3(isa, isaD, isaN, middle - t, middle, middle + t),
                TrMedian3(isa, isaD, isaN, last - 1 - (t << 1), last - 1 - t, last - 1)
        );
    }

    /*---------------------------------------------------------------------------*/

    private void LsUpdateGroup(int isa, int first, int last)
    {
        int[] SA = this.SA;

        int a, b;
        int t;

        for (a = first; a < last; ++a)
        {
            if (0 <= SA[a])
            {
                b = a;
                do
                {
                    SA[isa + SA[a]] = a;
                } while (++a < last && 0 <= SA[a]);
                SA[b] = b - a;
                if (last <= a)
                {
                    break;
                }
            }
            b = a;
            do
            {
                SA[a] = ~SA[a];
            } while (SA[++a] < 0);
            t = a;
            do
            {
                SA[isa + SA[b]] = t;
            } while (++b <= a);
        }
    }

    private void LsIntroSort(int isa, int isaD, int isaN, int first, int last)
    {
        int[] SA = this.SA;

        StackEntry[] stack = new StackEntry[STACK_SIZE];

        int a, b, c, d, e, f;
        int s, t;
        int limit;
        int v, x = 0;
        int ssize;

        for (ssize = 0, limit = TrLog(last - first);;)
        {
            if (last - first <= INSERTIONSORT_THRESHOLD)
            {
                if (1 < last - first)
                {
                    TrInsertionSort(isa, isaD, isaN, first, last);
                    LsUpdateGroup(isa, first, last);
                }
                else if (last - first == 1)
                {
                    SA[first] = -1;
                }
                if (ssize == 0)
                {
                    return;
                }
                StackEntry entry = stack[--ssize];
                first = entry.a;
                last = entry.b;
                limit = entry.c;
                continue;
            }

            if (limit-- == 0)
            {
                TrHeapSort(isa, isaD, isaN, first, last - first);
                for (a = last - 1; first < a; a = b)
                {
                    for (x = TrGetC(isa, isaD, isaN, SA[a]), b = a - 1;
                            first <= b && TrGetC(isa, isaD, isaN, SA[b]) == x;
                            --b)
                    {
                        SA[b] = ~SA[b];
                    }
                }
                LsUpdateGroup(isa, first, last);
                if (ssize == 0)
                {
                    return;
                }
                StackEntry entry = stack[--ssize];
                first = entry.a;
                last = entry.b;
                limit = entry.c;
                continue;
            }

            a = TrPivot(isa, isaD, isaN, first, last);
            SwapElements(SA, first, SA, a);
            v = TrGetC(isa, isaD, isaN, SA[first]);

            b = first + 1;
            while (b < last && (x = TrGetC(isa, isaD, isaN, SA[b])) == v)
            {
                ++b;
            }
            if ((a = b) < last && x < v)
            {
                while (++b < last && (x = TrGetC(isa, isaD, isaN, SA[b])) <= v)
                {
                    if (x == v)
                    {
                        SwapElements(SA, b, SA, a);
                        ++a;
                    }
                }
            }

            c = last - 1;
            while (b < c && (x = TrGetC(isa, isaD, isaN, SA[c])) == v)
            {
                --c;
            }
            if (b < (d = c) && x > v)
            {
                while (b < --c && (x = TrGetC(isa, isaD, isaN, SA[c])) >= v)
                {
                    if (x == v)
                    {
                        SwapElements(SA, c, SA, d);
                        --d;
                    }
                }
            }
            while (b < c)
            {
                SwapElements(SA, b, SA, c);
                while (++b < c && (x = TrGetC(isa, isaD, isaN, SA[b])) <= v)
                {
                    if (x == v)
                    {
                        SwapElements(SA, b, SA, a);
                        ++a;
                    }
                }
                while (b < --c && (x = TrGetC(isa, isaD, isaN, SA[c])) >= v)
                {
                    if (x == v)
                    {
                        SwapElements(SA, c, SA, d);
                        --d;
                    }
                }
            }

            if (a <= d)
            {
                c = b - 1;

                if ((s = a - first) > (t = b - a))
                {
                    s = t;
                }
                for (e = first, f = b - s; 0 < s; --s, ++e, ++f)
                {
                    SwapElements(SA, e, SA, f);
                }
                if ((s = d - c) > (t = last - d - 1))
                {
                    s = t;
                }
                for (e = b, f = last - s; 0 < s; --s, ++e, ++f)
                {
                    SwapElements(SA, e, SA, f);
                }

                a = first + (b - a);
                b = last - (d - c);

                for (c = first, v = a - 1; c < a; ++c)
                {
                    SA[isa + SA[c]] = v;
                }
                if (b < last)
                {
                    for (c = a, v = b - 1; c < b; ++c)
                    {
                        SA[isa + SA[c]] = v;
                    }
                }
                if ((b - a) == 1)
                {
                    SA[a] = - 1;
                }

                if (a - first <= last - b)
                {
                    if (first < a)
                    {
                        stack[ssize++] = new StackEntry(b, last, limit, 0);
                        last = a;
                    }
                    else
                    {
                        first = b;
                    }
                }
                else
                {
                    if (b < last)
                    {
                        stack[ssize++] = new StackEntry(first, a, limit, 0);
                        first = b;
                    }
                    else
                    {
                        last = a;
                    }
                }
            }
            else
            {
                if (ssize == 0)
                {
                    return;
                }
                StackEntry entry = stack[--ssize];
                first = entry.a;
                last = entry.b;
                limit = entry.c;
            }
        }
    }

    private void LsSort(int isa, int n, int depth)
    {
        int[] SA = this.SA;

        int isaD;
        int first, last, i;
        int t, skip;

        for (isaD = isa + depth; -n < SA[0]; isaD += isaD - isa)
        {
            first = 0;
            skip = 0;
            do
            {
                if ((t = SA[first]) < 0)
                {
                    first -= t;
                    skip += t;
                }
                else
                {
                    if (skip != 0)
                    {
                        SA[first + skip] = skip;
                        skip = 0;
                    }
                    last = SA[isa + t] + 1;
                    LsIntroSort(isa, isaD, isa + n, first, last);
                    first = last;
                }
            } while (first < n);
            if (skip != 0)
            {
                SA[first + skip] = skip;
            }
            if (n < isaD - isa)
            {
                first = 0;
                do
                {
                    if ((t = SA[first]) < 0)
                    {
                        first -= t;
                    }
                    else
                    {
                        last = SA[isa + t] + 1;
                        for (i = first; i < last; ++i)
                        {
                            SA[isa + SA[i]] = i;
                        }
                        first = last;
                    }
                } while (first < n);
                break;
            }
        }
    }

    /*---------------------------------------------------------------------------*/

    private class PartitionResult
    {
        internal readonly int first;
        internal readonly int last;

        internal PartitionResult(int first, int last)
        {
            this.first = first;
            this.last = last;
        }
    }

    private PartitionResult TrPartition(int isa, int isaD, int isaN,
                                        int first, int last, int v)
    {
        int[] SA = this.SA;

        int a, b, c, d, e, f;
        int t, s;
        int x = 0;

        b = first;
        while (b < last && (x = TrGetC(isa, isaD, isaN, SA[b])) == v)
        {
            ++b;
        }
        if ((a = b) < last && x < v)
        {
            while (++b < last && (x = TrGetC(isa, isaD, isaN, SA[b])) <= v)
            {
                if (x == v)
                {
                    SwapElements(SA, b, SA, a);
                    ++a;
                }
            }
        }

        c = last - 1;
        while (b < c && (x = TrGetC(isa, isaD, isaN, SA[c])) == v)
        {
            --c;
        }
        if (b < (d = c) && x > v)
        {
            while (b < --c && (x = TrGetC(isa, isaD, isaN, SA[c])) >= v)
            {
                if (x == v)
                {
                    SwapElements(SA, c, SA, d);
                    --d;
                }
            }
        }
        while (b < c)
        {
            SwapElements(SA, b, SA, c);
            while (++b < c && (x = TrGetC(isa, isaD, isaN, SA[b])) <= v)
            {
                if (x == v)
                {
                    SwapElements(SA, b, SA, a);
                    ++a;
                }
            }
            while (b < --c && (x = TrGetC(isa, isaD, isaN, SA[c])) >= v)
            {
                if (x == v)
                {
                    SwapElements(SA, c, SA, d);
                    --d;
                }
            }
        }

        if (a <= d)
        {
            c = b - 1;
            if ((s = a - first) > (t = b - a))
            {
                s = t;
            }
            for (e = first, f = b - s; 0 < s; --s, ++e, ++f)
            {
                SwapElements(SA, e, SA, f);
            }
            if ((s = d - c) > (t = last - d - 1))
            {
                s = t;
            }
            for (e = b, f = last - s; 0 < s; --s, ++e, ++f)
            {
                SwapElements(SA, e, SA, f);
            }
            first += b - a;
            last -= d - c;
        }
        return new PartitionResult(first, last);
    }

    private void TrCopy(int isa, int isaN, int first,
                        int a, int b, int last, int depth)
    {
        int[] SA = this.SA;

        int c, d, e;
        int s, v;

        v = b - 1;

        for (c = first, d = a - 1; c <= d; ++c)
        {
            if ((s = SA[c] - depth) < 0)
            {
                s += isaN - isa;
            }
            if (SA[isa + s] == v)
            {
                SA[++d] = s;
                SA[isa + s] = d;
            }
        }
        for (c = last - 1, e = d + 1, d = b; e < d; --c)
        {
            if ((s = SA[c] - depth) < 0)
            {
                s += isaN - isa;
            }
            if (SA[isa + s] == v)
            {
                SA[--d] = s;
                SA[isa + s] = d;
            }
        }
    }

    private void TrIntroSort(int isa, int isaD, int isaN, int first,
                             int last, TRBudget budget, int size)
    {
        int[] SA = this.SA;

        StackEntry[] stack = new StackEntry[STACK_SIZE];

        int a, b, c, d, e, f;
        int s, t;
        int v, x = 0;
        int limit, next;
        int ssize;

        for (ssize = 0, limit = TrLog(last - first);;)
        {
            if (limit < 0)
            {
                if (limit == -1)
                {
                    if (!budget.Update(size, last - first))
                    {
                        break;
                    }
                    PartitionResult result = TrPartition(isa, isaD - 1, isaN, first, last, last - 1);
                    a = result.first;
                    b = result.last;
                    if (first < a || b < last)
                    {
                        if (a < last)
                        {
                            for (c = first, v = a - 1; c < a; ++c)
                            {
                                SA[isa + SA[c]] = v;
                            }
                        }
                        if (b < last)
                        {
                            for (c = a, v = b - 1; c < b; ++c)
                            {
                                SA[isa + SA[c]] = v;
                            }
                        }

                        stack[ssize++] = new StackEntry(0, a, b, 0);
                        stack[ssize++] = new StackEntry(isaD - 1, first, last, -2);
                        if (a - first <= last - b)
                        {
                            if (1 < a - first)
                            {
                                stack[ssize++] = new StackEntry(isaD, b, last, TrLog(last - b));
                                last = a;
                                limit = TrLog(a - first);
                            }
                            else if (1 < last - b)
                            {
                                first = b;
                                limit = TrLog(last - b);
                            }
                            else
                            {
                                if (ssize == 0)
                                {
                                    return;
                                }
                                StackEntry entry = stack[--ssize];
                                isaD = entry.a;
                                first = entry.b;
                                last = entry.c;
                                limit = entry.d;
                            }
                        }
                        else
                        {
                            if (1 < last - b)
                            {
                                stack[ssize++] = new StackEntry(isaD, first, a, TrLog(a - first));
                                first = b;
                                limit = TrLog(last - b);
                            }
                            else if (1 < a - first)
                            {
                                last = a;
                                limit = TrLog(a - first);
                            }
                            else
                            {
                                if (ssize == 0)
                                {
                                    return;
                                }
                                StackEntry entry = stack[--ssize];
                                isaD = entry.a;
                                first = entry.b;
                                last = entry.c;
                                limit = entry.d;
                            }
                        }
                    }
                    else
                    {
                        for (c = first; c < last; ++c)
                        {
                            SA[isa + SA[c]] = c;
                        }
                        if (ssize == 0)
                        {
                            return;
                        }
                        StackEntry entry = stack[--ssize];
                        isaD = entry.a;
                        first = entry.b;
                        last = entry.c;
                        limit = entry.d;
                    }
                }
                else if (limit == -2)
                {
                    a = stack[--ssize].b;
                    b = stack[ssize].c;
                    TrCopy(isa, isaN, first, a, b, last, isaD - isa);
                    if (ssize == 0)
                    {
                        return;
                    }
                    StackEntry entry = stack[--ssize];
                    isaD = entry.a;
                    first = entry.b;
                    last = entry.c;
                    limit = entry.d;
                }
                else
                {
                    if (0 <= SA[first])
                    {
                        a = first;
                        do
                        {
                            SA[isa + SA[a]] = a;
                        } while (++a < last && 0 <= SA[a]);
                        first = a;
                    }
                    if (first < last)
                    {
                        a = first;
                        do
                        {
                            SA[a] = ~SA[a];
                        } while (SA[++a] < 0);
                        next = SA[isa + SA[a]] != SA[isaD + SA[a]] ? TrLog(a - first + 1) : -1;
                        if (++a < last)
                        {
                            for (b = first, v = a - 1; b < a; ++b)
                            {
                                SA[isa + SA[b]] = v;
                            }
                        }

                        if (a - first <= last - a)
                        {
                            stack[ssize++] = new StackEntry(isaD, a, last, -3);
                            isaD += 1;
                            last = a;
                            limit = next;
                        }
                        else
                        {
                            if (1 < last - a)
                            {
                                stack[ssize++] = new StackEntry(isaD + 1, first, a, next);
                                first = a;
                                limit = -3;
                            }
                            else
                            {
                                isaD += 1;
                                last = a;
                                limit = next;
                            }
                        }
                    }
                    else
                    {
                        if (ssize == 0)
                        {
                            return;
                        }
                        StackEntry entry = stack[--ssize];
                        isaD = entry.a;
                        first = entry.b;
                        last = entry.c;
                        limit = entry.d;
                    }
                }
                continue;
            }

            if (last - first <= INSERTIONSORT_THRESHOLD)
            {
                if (!budget.Update(size, last - first))
                {
                    break;
                }
                TrInsertionSort(isa, isaD, isaN, first, last);
                limit = -3;
                continue;
            }

            if (limit-- == 0)
            {
                if (!budget.Update(size, last - first))
                {
                    break;
                }
                TrHeapSort(isa, isaD, isaN, first, last - first);
                for (a = last - 1; first < a; a = b)
                {
                    for (x = TrGetC(isa, isaD, isaN, SA[a]), b = a - 1;
                            first <= b && TrGetC(isa, isaD, isaN, SA[b]) == x;
                            --b)
                    {
                        SA[b] = ~SA[b];
                    }
                }
                limit = -3;
                continue;
            }

            a = TrPivot(isa, isaD, isaN, first, last);

            SwapElements(SA, first, SA, a);
            v = TrGetC(isa, isaD, isaN, SA[first]);

            b = first + 1;
            while (b < last && (x = TrGetC(isa, isaD, isaN, SA[b])) == v)
            {
                ++b;
            }
            if ((a = b) < last && x < v)
            {
                while (++b < last && (x = TrGetC(isa, isaD, isaN, SA[b])) <= v)
                {
                    if (x == v)
                    {
                        SwapElements(SA, b, SA, a);
                        ++a;
                    }
                }
            }

            c = last - 1;
            while (b < c && (x = TrGetC(isa, isaD, isaN, SA[c])) == v)
            {
                --c;
            }
            if (b < (d = c) && x > v)
            {
                while (b < --c && (x = TrGetC(isa, isaD, isaN, SA[c])) >= v)
                {
                    if (x == v)
                    {
                        SwapElements(SA, c, SA, d);
                        --d;
                    }
                }
            }
            while (b < c)
            {
                SwapElements(SA, b, SA, c);
                while (++b < c && (x = TrGetC(isa, isaD, isaN, SA[b])) <= v)
                {
                    if (x == v)
                    {
                        SwapElements(SA, b, SA, a);
                        ++a;
                    }
                }
                while (b < --c && (x = TrGetC(isa, isaD, isaN, SA[c])) >= v)
                {
                    if (x == v)
                    {
                        SwapElements(SA, c, SA, d);
                        --d;
                    }
                }
            }

            if (a <= d)
            {
                c = b - 1;

                if ((s = a - first) > (t = b - a))
                {
                    s = t;
                }
                for (e = first, f = b - s; 0 < s; --s, ++e, ++f)
                {
                    SwapElements(SA, e, SA, f);
                }
                if ((s = d - c) > (t = last - d - 1))
                {
                    s = t;
                }
                for (e = b, f = last - s; 0 < s; --s, ++e, ++f)
                {
                    SwapElements(SA, e, SA, f);
                }

                a = first + (b - a);
                b = last - (d - c);
                next = SA[isa + SA[a]] != v ? TrLog(b - a) : -1;

                for (c = first, v = a - 1; c < a; ++c)
                {
                    SA[isa + SA[c]] = v;
                }
                if (b < last)
                {
                    for (c = a, v = b - 1; c < b; ++c)
                    {
                        SA[isa + SA[c]] = v;
                    }
                }

                if (a - first <= last - b)
                {
                    if (last - b <= b - a)
                    {
                        if (1 < a - first)
                        {
                            stack[ssize++] = new StackEntry(isaD + 1, a, b, next);
                            stack[ssize++] = new StackEntry(isaD, b, last, limit);
                            last = a;
                        }
                        else if (1 < last - b)
                        {
                            stack[ssize++] = new StackEntry(isaD + 1, a, b, next);
                            first = b;
                        }
                        else if (1 < b - a)
                        {
                            isaD += 1;
                            first = a;
                            last = b;
                            limit = next;
                        }
                        else
                        {
                            if (ssize == 0)
                            {
                                return;
                            }
                            StackEntry entry = stack[--ssize];
                            isaD = entry.a;
                            first = entry.b;
                            last = entry.c;
                            limit = entry.d;
                        }
                    }
                    else if (a - first <= b - a)
                    {
                        if (1 < a - first)
                        {
                            stack[ssize++] = new StackEntry(isaD, b, last, limit);
                            stack[ssize++] = new StackEntry(isaD + 1, a, b, next);
                            last = a;
                        }
                        else if (1 < b - a)
                        {
                            stack[ssize++] = new StackEntry(isaD, b, last, limit);
                            isaD += 1;
                            first = a;
                            last = b;
                            limit = next;
                        }
                        else
                        {
                            first = b;
                        }
                    }
                    else
                    {
                        if (1 < b - a)
                        {
                            stack[ssize++] = new StackEntry(isaD, b, last, limit);
                            stack[ssize++] = new StackEntry(isaD, first, a, limit);
                            isaD += 1;
                            first = a;
                            last = b;
                            limit = next;
                        }
                        else
                        {
                            stack[ssize++] = new StackEntry(isaD, b, last, limit);
                            last = a;
                        }
                    }
                }
                else
                {
                    if (a - first <= b - a)
                    {
                        if (1 < last - b)
                        {
                            stack[ssize++] = new StackEntry(isaD + 1, a, b, next);
                            stack[ssize++] = new StackEntry(isaD, first, a, limit);
                            first = b;
                        }
                        else if (1 < a - first)
                        {
                            stack[ssize++] = new StackEntry(isaD + 1, a, b, next);
                            last = a;
                        }
                        else if (1 < b - a)
                        {
                            isaD += 1;
                            first = a;
                            last = b;
                            limit = next;
                        }
                        else
                        {
                            stack[ssize++] = new StackEntry(isaD, first, last, limit);
                        }
                    }
                    else if (last - b <= b - a)
                    {
                        if (1 < last - b)
                        {
                            stack[ssize++] = new StackEntry(isaD, first, a, limit);
                            stack[ssize++] = new StackEntry(isaD + 1, a, b, next);
                            first = b;
                        }
                        else if (1 < b - a)
                        {
                            stack[ssize++] = new StackEntry(isaD, first, a, limit);
                            isaD += 1;
                            first = a;
                            last = b;
                            limit = next;
                        }
                        else
                        {
                            last = a;
                        }
                    }
                    else
                    {
                        if (1 < b - a)
                        {
                            stack[ssize++] = new StackEntry(isaD, first, a, limit);
                            stack[ssize++] = new StackEntry(isaD, b, last, limit);
                            isaD += 1;
                            first = a;
                            last = b;
                            limit = next;
                        }
                        else
                        {
                            stack[ssize++] = new StackEntry(isaD, first, a, limit);
                            first = b;
                        }
                    }
                }
            }
            else
            {
                if (!budget.Update(size, last - first))
                {
                    break; // BUGFIX : Added to prevent an infinite loop in the original code
                }
                limit += 1;
                isaD += 1;
            }
        }

        for (s = 0; s < ssize; ++s)
        {
            if (stack[s].d == -3)
            {
                LsUpdateGroup(isa, stack[s].b, stack[s].c);
            }
        }
    }

    private class TRBudget
    {
        internal int budget;
        internal int chance;

        internal TRBudget(int budget, int chance)
        {
            this.budget = budget;
            this.chance = chance;
        }

        internal bool Update(int size, int n)
        {
            budget -= n;
            if (budget <= 0)
            {
                if (--chance == 0)
                {
                    return false;
                }
                budget += size;
            }
            return true;
        }
    }

    private void TrSort(int isa, int n, int depth)
    {
        int[] SA = this.SA;

        int first = 0, last;
        int t;

        if (-n < SA[0])
        {
            TRBudget budget = new TRBudget(n, TrLog(n) * 2 / 3 + 1);
            do
            {
                if ((t = SA[first]) < 0)
                {
                    first -= t;
                }
                else
                {
                    last = SA[isa + t] + 1;
                    if (1 < last - first)
                    {
                        TrIntroSort(isa, isa + depth, isa + n, first, last, budget, n);
                        if (budget.chance == 0)
                        {
                            /* Switch to Larsson-Sadakane sorting algorithm */
                            if (0 < first)
                            {
                                SA[0] = -first;
                            }
                            LsSort(isa, n, depth);
                            break;
                        }
                    }
                    first = last;
                }
            } while (first < n);
        }
    }

    /*---------------------------------------------------------------------------*/

    private static int BUCKET_B(int c0, int c1)
    {
        return (c1 << 8) | c0;
    }

    private static int BUCKET_BSTAR(int c0, int c1)
    {
        return (c0 << 8) | c1;
    }

    private int SortTypeBstar(int[] bucketA, int[] bucketB)
    {
        byte[] T = this.T;
        int[] SA = this.SA;
        int n = this.n;
        int[] tempbuf = new int[256];

        int[] buf;
        int PAb, ISAb, bufoffset;
        int i, j, k, t, m, bufsize;
        int c0, c1;
        int flag;

        for (i = 1, flag = 1; i < n; ++i)
        {
            if (T[i - 1] != T[i])
            {
                if ((T[i - 1] & 0xff) > (T[i] & 0xff))
                {
                    flag = 0;
                }
                break;
            }
        }
        i = n - 1;
        m = n;

        int ti, ti1, t0;
        if ((ti = T[i] & 0xff) < (t0 = T[0] & 0xff) || (T[i] == T[0] && flag != 0))
        {
            if (flag == 0)
            {
                ++bucketB[BUCKET_BSTAR(ti, t0)];
                SA[--m] = i;
            }
            else
            {
                ++bucketB[BUCKET_B(ti, t0)];
            }
            for (--i; 0 <= i && (ti = T[i] & 0xff) <= (ti1 = T[i + 1] & 0xff); --i)
            {
                ++bucketB[BUCKET_B(ti, ti1)];
            }
        }

        while (0 <= i)
        {
            do
            {
                ++bucketA[T[i] & 0xff];
            } while (0 <= --i && (T[i] & 0xff) >= (T[i + 1] & 0xff));
            if (0 <= i)
            {
                ++bucketB[BUCKET_BSTAR(T[i] & 0xff, T[i + 1] & 0xff)];
                SA[--m] = i;
                for (--i; 0 <= i && (ti = T[i] & 0xff) <= (ti1 = T[i + 1] & 0xff); --i)
                {
                    ++bucketB[BUCKET_B(ti, ti1)];
                }
            }
        }
        m = n - m;
        if (m == 0)
        {
            for (i = 0; i < n; ++i)
            {
                SA[i] = i;
            }
            return 0;
        }

        for (c0 = 0, i = -1, j = 0; c0 < 256; ++c0)
        {
            t = i + bucketA[c0];
            bucketA[c0] = i + j;
            i = t + bucketB[BUCKET_B(c0, c0)];
            for (c1 = c0 + 1; c1 < 256; ++c1)
            {
                j += bucketB[BUCKET_BSTAR(c0, c1)];
                bucketB[(c0 << 8) | c1] = j;
                i += bucketB[BUCKET_B(c0, c1)];
            }
        }

        PAb = n - m;
        ISAb = m;
        for (i = m - 2; 0 <= i; --i)
        {
            t = SA[PAb + i];
            c0 = T[t] & 0xff;
            c1 = T[t + 1] & 0xff;
            SA[--bucketB[BUCKET_BSTAR(c0, c1)]] = i;
        }
        t = SA[PAb + m - 1];
        c0 = T[t] & 0xff;
        c1 = T[t + 1] & 0xff;
        SA[--bucketB[BUCKET_BSTAR(c0, c1)]] = m - 1;

        buf = SA;
        bufoffset = m;
        bufsize = n - 2 * m;
        if (bufsize <= 256)
        {
            buf = tempbuf;
            bufoffset = 0;
            bufsize = 256;
        }

        for (c0 = 255, j = m; 0 < j; --c0)
        {
            for (c1 = 255; c0 < c1; j = i, --c1)
            {
                i = bucketB[BUCKET_BSTAR(c0, c1)];
                if (1 < j - i)
                {
                    SubStringSort(PAb, i, j, buf, bufoffset, bufsize, 2, SA[i] == m - 1, n);
                }
            }
        }

        for (i = m - 1; 0 <= i; --i)
        {
            if (0 <= SA[i])
            {
                j = i;
                do
                {
                    SA[ISAb + SA[i]] = i;
                } while (0 <= --i && 0 <= SA[i]);
                SA[i + 1] = i - j;
                if (i <= 0)
                {
                    break;
                }
            }
            j = i;
            do
            {
                SA[ISAb + (SA[i] = ~SA[i])] = j;
            } while (SA[--i] < 0);
            SA[ISAb + SA[i]] = j;
        }

        TrSort(ISAb, m, 1);

        i = n - 1;
        j = m;
        if ((T[i] & 0xff) < (T[0] & 0xff) || (T[i] == T[0] && flag != 0))
        {
            if (flag == 0)
            {
                SA[SA[ISAb + --j]] = i;
            }
            for (--i; 0 <= i && (T[i] & 0xff) <= (T[i + 1] & 0xff);)
            {
                --i;
            }
        }
        while (0 <= i)
        {
            for (--i; 0 <= i && (T[i] & 0xff) >= (T[i + 1] & 0xff);)
            {
                --i;
            }
            if (0 <= i)
            {
                SA[SA[ISAb + --j]] = i;
                for (--i; 0 <= i && (T[i] & 0xff) <= (T[i + 1] & 0xff);)
                {
                    --i;
                }
            }
        }

        for (c0 = 255, i = n - 1, k = m - 1; 0 <= c0; --c0)
        {
            for (c1 = 255; c0 < c1; --c1)
            {
                t = i - bucketB[BUCKET_B(c0, c1)];
                bucketB[BUCKET_B(c0, c1)] = i + 1;

                for (i = t, j = bucketB[BUCKET_BSTAR(c0, c1)]; j <= k; --i, --k)
                {
                    SA[i] = SA[k];
                }
            }
            t = i - bucketB[BUCKET_B(c0, c0)];
            bucketB[BUCKET_B(c0, c0)] = i + 1;
            if (c0 < 255)
            {
                bucketB[BUCKET_BSTAR(c0, c0 + 1)] = t + 1;
            }
            i = bucketA[c0];
        }
        return m;
    }

    private int ConstructBWT(int[] bucketA, int[] bucketB)
    {
        byte[] T = this.T;
        int[] SA = this.SA;
        int n = this.n;

        int i, j, t = 0;
        int s, s1;
        int c0, c1, c2 = 0;
        int orig = -1;

        for (c1 = 254; 0 <= c1; --c1)
        {
            for (i = bucketB[BUCKET_BSTAR(c1, c1 + 1)], j = bucketA[c1 + 1], t = 0, c2 = -1;
                    i <= j;
                    --j)
            {
                if (0 <= (s1 = s = SA[j]))
                {
                    if (--s < 0)
                    {
                        s = n - 1;
                    }
                    if ((c0 = T[s] & 0xff) <= c1)
                    {
                        SA[j] = ~s1;
                        if (0 < s && (T[s - 1] & 0xff) > c0)
                        {
                            s = ~s;
                        }
                        if (c2 == c0)
                        {
                            SA[--t] = s;
                        }
                        else
                        {
                            if (0 <= c2)
                            {
                                bucketB[BUCKET_B(c2, c1)] = t;
                            }
                            SA[t = bucketB[BUCKET_B(c2 = c0, c1)] - 1] = s;
                        }
                    }
                }
                else
                {
                    SA[j] = ~s;
                }
            }
        }

        for (i = 0; i < n; ++i)
        {
            if (0 <= (s1 = s = SA[i]))
            {
                if (--s < 0)
                {
                    s = n - 1;
                }
                if ((c0 = T[s] & 0xff) >= (T[s + 1] & 0xff))
                {
                    if (0 < s && (T[s - 1] & 0xff) < c0)
                    {
                        s = ~s;
                    }
                    if (c0 == c2)
                    {
                        SA[++t] = s;
                    }
                    else
                    {
                        if (c2 != -1)
                        {
                            bucketA[c2] = t;    // BUGFIX: Original code can write to bucketA[-1]
                        }
                        SA[t = bucketA[c2 = c0] + 1] = s;
                    }
                }
            }
            else
            {
                s1 = ~s1;
            }

            if (s1 == 0)
            {
                SA[i] = T[n - 1];
                orig = i;
            }
            else
            {
                SA[i] = T[s1 - 1];
            }
        }
        return orig;
    }

    /**
     * Performs a Burrows Wheeler Transform on the input array.
     * @return the index of the first character of the input array within the output array
     */
    public int Bwt()
    {
        int[] SA = this.SA;
        byte[] T = this.T;
        int n = this.n;

        int[] bucketA = new int[BUCKET_A_SIZE];
        int[] bucketB = new int[BUCKET_B_SIZE];

        if (n == 0)
        {
            return 0;
        }
        if (n == 1)
        {
            SA[0] = T[0];
            return 0;
        }

        int m = SortTypeBstar(bucketA, bucketB);
        if (0 < m)
        {
            return ConstructBWT(bucketA, bucketB);
        }
        return 0;
    }
}
