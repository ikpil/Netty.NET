/*
 * Copyright 2016 The Netty Project
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
namespace Netty.NET.Buffer.Tests;

public class WrappedCompositeByteBufTest : BigEndianCompositeByteBufTest
{
    protected sealed override ByteBuf NewBuffer(int length, int maxCapacity)
    {
        return Wrap((CompositeByteBuf)base.NewBuffer(length, maxCapacity));
    }

    // CLR: private protected keeps the original subclass hook while returning the internal
    // WrappedCompositeByteBuf type from a public fixture (CS0051,
    // https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/private-protected).
    private protected virtual WrappedCompositeByteBuf Wrap(CompositeByteBuf buffer)
    {
        return new WrappedCompositeByteBuf(buffer);
    }

    protected override CompositeByteBuf NewCompositeBuffer()
    {
        return Wrap(base.NewCompositeBuffer());
    }
}
