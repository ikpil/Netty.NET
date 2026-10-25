/*
 * Copyright 2012 The Netty Project
 *
 * The Netty Project licenses this file to the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

namespace Netty.NET.Buffer;

internal sealed class PooledHeapByteBuf : PooledByteBuf
{
    internal PooledHeapByteBuf(PoolHeapArena arena, int initialCapacity, int maxCapacity, PoolThreadCache cache = null)
        : base(arena, initialCapacity, maxCapacity, cache) { }

    public override bool IsDirect => false;

    public override ByteBuf Copy(int index, int length)
    {
        CheckIndex(index, length);
        ByteBuf copy = Allocator.HeapBuffer(length, MaxCapacity);
        // CLR: Span preserves the original array-write extension point without
        // exposing the pooled backing array. Release the new owner on failure.
        try { return copy.WriteBytes(GetMemoryCore(index, length).Span); }
        catch { copy.Release(); throw; }
    }
}
