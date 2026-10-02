using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common;

internal sealed class TraceRecord : Exception
{
    internal const int CLOSE_MARK_POS = -2;
    // Override fillInStackTrace() so we not populate the backtrace via a native call and so leak the
    // Classloader.
    // See https://github.com/netty/netty/pull/10691
    // CLR captures no stack for the bottom sentinel and retains no native exception backtrace.
    internal static readonly TraceRecord BOTTOM = new(false);
    private readonly StackTrace trace;
    private readonly string hintString;
    private readonly TraceRecord nextRecord;
    private readonly int position;
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal TraceRecord(TraceRecord next, object hint)
    {
        trace = new StackTrace(0, true);
        // This needs to be generated even if toString() is never called as it may change later on.
        hintString = hint is IResourceLeakHint leakHint ? leakHint.toHintString() : hint.ToString();
        nextRecord = next;
        position = next.position + 1;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal TraceRecord(TraceRecord next)
    {
        trace = new StackTrace(0, true);
        nextRecord = next;
        position = next.position + 1;
    }
    // Used to terminate the stack
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal TraceRecord(bool closeMarker)
    {
        trace = closeMarker ? new StackTrace(0, true) : null;
        position = closeMarker ? CLOSE_MARK_POS : -1;
    }
    public override string StackTrace => trace?.ToString();
    internal int pos() => position;
    internal TraceRecord next() => nextRecord;
    public override string ToString()
    {
        var buffer = new StringBuilder(2048);
        if (hintString != null) buffer.Append("\tHint: ").Append(hintString).Append(StringUtil.NEWLINE);
        // Append the stack trace.
        StackFrame[] frames = trace?.GetFrames() ?? Array.Empty<StackFrame>();
        // Skip the first three elements.
        // CLR inlining can remove those implementation frames. Skip by identity,
        // rather than dropping a fixed number of frames that may belong to callers.
        int firstCaller = 0;
        while (firstCaller < frames.Length && isImplementationFrame(frames[firstCaller])) firstCaller++;
        for (int i = firstCaller; i < frames.Length; i++)
        {
            MethodBaseInfo(frames[i], out string className, out string methodName);
            // Strip the noisy stack trace elements.
            string[] exclusions = ResourceLeakDetector.excludedMethods.get();
            bool excluded = false;
            for (int k = 0; k < exclusions.Length; k += 2)
            {
                // Suppress a warning about out of bounds access
                // since the length of excludedMethods is always even, see addExclusions()
                if (exclusions[k] == className && exclusions[k + 1] == methodName) { excluded = true; break; }
            }
            if (!excluded) buffer.Append('\t').Append(new StackTrace(frames[i]).ToString().TrimEnd('\r', '\n')).Append(StringUtil.NEWLINE);
        }
        return buffer.ToString();
    }
    private static void MethodBaseInfo(StackFrame frame, out string className, out string methodName)
    {
        var method = frame.GetMethod();
        className = ResourceLeakDetector.exclusionTypeName(method?.DeclaringType);
        methodName = method?.Name;
    }
    private static bool isImplementationFrame(StackFrame frame)
    {
        var method = frame.GetMethod();
        Type type = method?.DeclaringType;
        if (type == typeof(TraceRecord)) return true;
        if (type?.IsGenericType != true) return false;
        Type definition = type.GetGenericTypeDefinition();
        return definition == typeof(DefaultResourceLeak<>) ||
            definition == typeof(ResourceLeakDetector<>) && method.Name == "track0";
    }
}
