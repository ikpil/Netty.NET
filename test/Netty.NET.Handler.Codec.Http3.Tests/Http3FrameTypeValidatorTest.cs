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

using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http3.Tests;

// CLR: the abstract JUnit fixture is an abstract xUnit class whose [Fact]s run for each derived class, as codec-http2's
// AbstractDecoratingHttp2ConnectionDecoderTest. Its derived classes (Http3ControlStreamFrameTypeValidatorTest,
// Http3PushStreamFrameTypeValidatorTest, Http3RequestStreamFrameTypeValidatorTest) run its two tests each. The validator is the internal IHttp3FrameTypeValidator (package-private in Java), so
// newValidator() is private protected: a protected member of a public class may not return an internal type (CS0050),
// and private protected limits it to derived classes of this assembly. assertThrows(Http3Exception.class, ...) is
// Assert.ThrowsAny<Http3Exception> (docs/codec-http-porting.md, Core test notes); the throws clause has no counterpart.
public abstract class Http3FrameTypeValidatorTest
{

    protected abstract long[] InvalidFramesTypes();

    protected abstract long[] ValidFrameTypes();

    private protected abstract IHttp3FrameTypeValidator NewValidator();

    [Fact]
    public void TestValidFrameTypes()
    {
        foreach (long validFrameType in ValidFrameTypes())
        {
            NewValidator().Validate(validFrameType, true);
        }
    }

    [Fact]
    public void TestInvalidFrameTypes()
    {
        foreach (long invalidFrameType in InvalidFramesTypes())
        {
            Assert.ThrowsAny<Http3Exception>(() => NewValidator().Validate(invalidFrameType, true));
        }
    }
}
