/*
 * Copyright 2023 The Netty Project
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

namespace Netty.NET.Handler.Codec.Compression;

/**
 * {@link SnappyOptions} holds config for
 * Snappy compression.
 */
// CLR: CompressionOptions is the marker interface ICompressionOptions (I prefix); the public final class is sealed.
public sealed class SnappyOptions : ICompressionOptions
{
    // Will add config if Snappy supports this
}
