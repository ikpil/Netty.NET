/*
 * Copyright 2015 The Netty Project
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

using System.Threading;

namespace Netty.NET.Common.Concurrent;

/**
 * Expose details for a {@link Thread}.
 */
public interface IThreadProperties
{
    /**
     * @see Thread#getState()
     */
    ThreadState State();

    /**
     * @see Thread#getPriority()
     */
    ThreadPriority Priority();

    /**
     * @see Thread#isInterrupted()
     */
    bool IsInterrupted();

    /**
     * @see Thread#isDaemon()
     */
    bool IsDaemon();

    /**
     * @see Thread#getName()
     */
    string Name();

    /**
     * @see Thread#getId()
     */
    long Id();

    /**
     * @see Thread#getStackTrace()
     */
    System.Diagnostics.StackFrame[] StackTrace();

    /**
     * @see Thread#isAlive()
     */
    bool IsAlive();
}