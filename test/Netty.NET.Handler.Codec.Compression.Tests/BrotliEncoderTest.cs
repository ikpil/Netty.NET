/*
 * Copyright 2021 The Netty Project
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
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Compression.Tests;

// CLR: the static @BeforeAll setUp() runs from the static constructor and is private (xUnit1013 asks public methods of a
// test class to be tests, as in ByteBufChecksumTest); an exception there fails every test as TypeInitializationException,
// the counterpart of ExceptionInInitializerError, so the try/catch is not repeated. createChannel() is protected, as the
// abstract method it overrides. readInbound()/readOutbound() are ReadInbound<ByteBuf>()/ReadOutbound<ByteBuf>() and the
// Buffer port's addComponent(true, buffer) is AddComponent(buffer, true).
//
// destroyChannel() overrides AbstractEncoderTest's @AfterEach destroyChannel() without the annotation. JUnit runs a
// lifecycle method, "any method that is directly annotated or meta-annotated with @BeforeAll, @AfterAll, @BeforeEach, or
// @AfterEach" (https://docs.junit.org/5.14.3/writing-tests/definitions.html), and @AfterEach methods "are inherited
// unless they are overridden" (https://docs.junit.org/5.14.3/writing-tests/annotations.html; "Test methods and lifecycle
// methods are inherited unless they are overridden",
// https://docs.junit.org/5.14.3/writing-tests/test-classes-and-methods.html). A method annotation is not inherited by an
// overriding method (@Inherited "has no effect if the annotated interface is used to annotate anything other than a
// class", https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/annotation/Inherited.html). So the
// override is not a lifecycle method, the base method is overridden, and JUnit (5.14.3, the version Netty 4.2.19 uses)
// runs no destroyChannel() after the tests of this class. The C# base runs DestroyChannel() from
// IAsyncLifetime.DisposeAsync, which would call the override; this class therefore re-implements IAsyncLifetime and
// implements IAsyncDisposable.DisposeAsync() as a no-op. "A re-implementation of an interface is also implicitly a
// re-implementation of all of the interface's base interfaces", and "Inherited public member declarations ...
// participate in the interface mapping process for re-implemented interfaces"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/interfaces#1967-interface-re-implementation),
// so InitializeAsync is still the base's, which calls CreateChannel(). DestroyChannel() is ported unchanged and, as in
// Java, not called.
public class BrotliEncoderTest : AbstractEncoderTest, IAsyncLifetime
{

    private EmbeddedChannel ENCODER_CHANNEL;
    private EmbeddedChannel DECODER_CHANNEL;

    static BrotliEncoderTest()
    {
        SetUp();
    }

    private static void SetUp()
    {
        Brotli.EnsureAvailability();
    }

    // CLR: no @AfterEach method runs for this class in JUnit (see the class comment).
    ValueTask IAsyncDisposable.DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    protected override EmbeddedChannel CreateChannel()
    {
        // Setup Encoder and Decoder
        ENCODER_CHANNEL = new EmbeddedChannel(new BrotliEncoder());
        DECODER_CHANNEL = new EmbeddedChannel(new BrotliDecoder());

        // Return the main channel (Encoder)
        return ENCODER_CHANNEL;
    }

    public override void DestroyChannel()
    {
        ENCODER_CHANNEL.FinishAndReleaseAll();
        DECODER_CHANNEL.FinishAndReleaseAll();
    }

    protected override ByteBuf Decompress(ByteBuf compressed, int originalLength)
    {
        DECODER_CHANNEL.WriteInbound(compressed);

        ByteBuf aggregatedBuffer = Unpooled.Buffer();
        ByteBuf decompressed = DECODER_CHANNEL.ReadInbound<ByteBuf>();
        while (decompressed != null)
        {
            aggregatedBuffer.WriteBytes(decompressed);

            decompressed.Release();
            decompressed = DECODER_CHANNEL.ReadInbound<ByteBuf>();
        }

        return aggregatedBuffer;
    }

    protected override ByteBuf ReadDecompressed(int dataLength)
    {
        CompositeByteBuf decompressed = Unpooled.CompositeBuffer();
        ByteBuf msg;
        while ((msg = channel.ReadOutbound<ByteBuf>()) != null)
        {
            if (msg.IsReadable())
            {
                decompressed.AddComponent(Decompress(msg, -1), true);
            }
            else
            {
                msg.Release();
            }
        }
        return decompressed;
    }
}
