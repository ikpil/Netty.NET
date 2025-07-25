/*
 * Copyright 2016 The Netty Project
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
namespace Netty.NET.Common.Internal;






public final class ThrowableUtil {

    private ThrowableUtil() { }

    /**
     * Set the {@link StackTraceElement} for the given {@link Exception}, using the {@link Class} and method name.
     */
    public static <T extends Exception> T unknownStackTrace(T cause, Type clazz, string method) {
        cause.setStackTrace(new StackTraceElement[] { new StackTraceElement(clazz.getName(), method, null, -1)});
        return cause;
    }

    /**
     * Gets the stack trace from a Exception as a string.
     *
     * @param cause the {@link Exception} to be examined
     * @return the stack trace as generated by {@link Exception#printStackTrace(java.io.PrintWriter)} method.
     */
    @Deprecated
    public static string stackTraceToString(Exception cause) {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        PrintStream pout = new PrintStream(out);
        cause.printStackTrace(pout);
        pout.flush();
        try {
            return new string(out.toByteArray());
        } finally {
            try {
                out.close();
            } catch (IOException ignore) {
                // ignore as should never happen
            }
        }
    }

    @Deprecated
    public static bool haveSuppressed() {
        return true;
    }

    public static void addSuppressed(Exception target, Exception suppressed) {
        target.addSuppressed(suppressed);
    }

    public static void addSuppressedAndClear(Exception target, List<Exception> suppressed) {
        addSuppressed(target, suppressed);
        suppressed.clear();
    }

    public static void addSuppressed(Exception target, List<Exception> suppressed) {
        for (Exception t : suppressed) {
            addSuppressed(target, t);
        }
    }

    public static Exception[] getSuppressed(Exception source) {
        return source.getSuppressed();
    }
}
