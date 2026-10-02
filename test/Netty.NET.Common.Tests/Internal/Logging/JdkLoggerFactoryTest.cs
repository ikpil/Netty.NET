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
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common.Tests.Internal.Logging;

// The pinned JDK factory test is adapted to the existing CLR TraceSource factory.
public class JdkLoggerFactoryTest
{
    [Fact]
    public void testCreation()
    {
        var factory = new InternalDefaultLoggerFactory();
        try
        {
            IInternalLogger logger = factory.newInstance("foo");
            Assert.IsType<InternalDefaultLogger>(logger);
            Assert.Equal("foo", logger.name());
        }
        finally { factory.Dispose(); }
    }
}
