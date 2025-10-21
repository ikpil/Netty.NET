using System;
using System.Diagnostics;
using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common;

public class TraceRecord : Exception
{
    public static readonly TraceRecord BOTTOM = new BottomTraceRecord();

    private readonly string _hintString;
    private readonly TraceRecord _next;
    private readonly int _pos;

    public TraceRecord(TraceRecord next, object hint)
    {
        // This needs to be generated even if ToString() is never called as it may change later on.
        _hintString = hint is IResourceLeakHint leakHint ? leakHint.toHintString() : hint.ToString();
        _next = next;
        _pos = next._pos + 1;
    }

    public TraceRecord(TraceRecord next)
    {
        _hintString = null;
        _next = next;
        _pos = next._pos + 1;
    }
    
    // Used to terminate the stack
    protected TraceRecord()
    {
        _hintString = null;
        _next = null;
        _pos = -1;
    }

    public int pos()
    {
        return _pos;
    }

    public TraceRecord next()
    {
        return _next;
    }

    public override string ToString()
    {
        StringBuilder buf = new StringBuilder(2048);
        if (_hintString != null)
        {
            buf.Append("\tHint: ").Append(_hintString).Append(StringUtil.NEWLINE);
        }


        var trace = new StackTrace(true);
        var array = trace.GetFrames();

        // Append the stack trace.
        // Skip the first three elements.
        for (int i = 3; i < array.Length; i++)
        {
            var element = array[i];
            var method = element.GetMethod();
            var className = method?.DeclaringType?.FullName ?? "<UnknownClass>";
            var methodName = method?.Name ?? "<UnknownMethod>";
            // Strip the noisy stack trace elements.
            bool excluded = false;
            string[] exclusions = ResourceLeakDetector.excludedMethods.get();
            for (int k = 0; k < exclusions.Length; k += 2)
            {
                // Suppress a warning about out of bounds access
                // since the length of excludedMethods is always even, see addExclusions()
                if (exclusions[k] == className
                    && exclusions[k + 1] == methodName)
                {
                    excluded = true;
                    break;
                }
            }

            if (excluded)
                continue;

            buf.Append('\t');
            buf.Append(element.ToString());
            buf.Append(StringUtil.NEWLINE);
        }

        return buf.ToString();
    }
}