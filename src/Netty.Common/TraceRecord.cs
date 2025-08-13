using System;
using System.Text;

namespace Netty.NET.Common;

public class TraceRecord : Exception 
{

    private static readonly TraceRecord BOTTOM = new TraceRecord() {
        private static readonly long serialVersionUID = 7396077602074694571L;

        // Override fillInStackTrace() so we not populate the backtrace via a native call and so leak the
        // Classloader.
        // See https://github.com/netty/netty/pull/10691
        @Override
        public Exception fillInStackTrace() {
            return this;
        }
    };

    private readonly string hintString;
    private readonly TraceRecord next;
    private readonly int pos;

    TraceRecord(TraceRecord next, object hint) {
        // This needs to be generated even if toString() is never called as it may change later on.
        hintString = hint instanceof ResourceLeakHint ? ((ResourceLeakHint) hint).toHintString() : hint.toString();
        this.next = next;
        this.pos = next.pos + 1;
    }

    TraceRecord(TraceRecord next) {
       hintString = null;
       this.next = next;
       this.pos = next.pos + 1;
    }

    // Used to terminate the stack
    private TraceRecord() {
        hintString = null;
        next = null;
        pos = -1;
    }

    @Override
    public string toString() {
        StringBuilder buf = new StringBuilder(2048);
        if (hintString != null) {
            buf.append("\tHint: ").append(hintString).append(NEWLINE);
        }

        // Append the stack trace.
        StackTraceElement[] array = getStackTrace();
        // Skip the first three elements.
        out: for (int i = 3; i < array.length; i++) {
            StackTraceElement element = array[i];
            // Strip the noisy stack trace elements.
            string[] exclusions = excludedMethods.get();
            for (int k = 0; k < exclusions.length; k += 2) {
                // Suppress a warning about out of bounds access
                // since the length of excludedMethods is always even, see addExclusions()
                if (exclusions[k].equals(element.getClassName())
                        && exclusions[k + 1].equals(element.getMethodName())) {
                    continue out;
                }
            }

            buf.append('\t');
            buf.append(element.toString());
            buf.append(NEWLINE);
        }
        return buf.toString();
    }
}