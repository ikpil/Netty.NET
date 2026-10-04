using System;

namespace Netty.NET.Common.Concurrent;

// Synchronous callback depth belongs to the physical thread, across completion and progress owners.
// A stack-only scope restores it on every exit without allocating a thread-local map or flowing AsyncLocal state.
internal readonly ref struct ExecutorNotificationScope
{
    private const int MaxInlineDepth = 8;
    [ThreadStatic] private static int _depth;

    internal static bool CanInline => _depth < MaxInlineDepth;
    public ExecutorNotificationScope() => ++_depth;
    public void Dispose() => --_depth;
}
