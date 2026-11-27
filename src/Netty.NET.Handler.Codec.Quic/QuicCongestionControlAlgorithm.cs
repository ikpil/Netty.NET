/*
 * Copyright 2020 The Netty Project
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

namespace Netty.NET.Handler.Codec.Quic;

/**
 * Available congestion control algorithms to use.
 */
// CLR: a Java enum without fields or methods is a C# enum. System.Net.Quic does not let the application choose the
// congestion control algorithm (docs/quic-design.md, No counterpart in System.Net.Quic): the type keeps its Java API
// (owner decision 2026-11-27) and the msquic codec builders (stage 2) report a choice as unsupported.
public enum QuicCongestionControlAlgorithm
{
    RENO,
    CUBIC,
    BBR
}
