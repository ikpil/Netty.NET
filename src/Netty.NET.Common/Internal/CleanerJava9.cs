/*
* Copyright 2017 The Netty Project
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













/**
 * Provide a way to clean a ByteBuffer on Java9+.
 */
final class CleanerJava9 : Cleaner {
    private static readonly InternalLogger logger = InternalLoggerFactory.getInstance(typeof(CleanerJava9));

    private static readonly MethodHandle INVOKE_CLEANER;

    static {
        final MethodHandle method;
        final Exception error;
        if (PlatformDependent0.hasUnsafe()) {
            final ByteBuffer buffer = ByteBuffer.allocateDirect(1);
            object maybeInvokeMethod = AccessController.doPrivileged(new PrivilegedAction<object>() {
                @Override
                public object run() {
                    try {
                        // See https://bugs.openjdk.java.net/browse/JDK-8171377
                        Class<? extends Unsafe> unsafeClass = PlatformDependent0.UNSAFE.getClass();
                        MethodHandles.Lookup lookup = MethodHandles.lookup();
                        MethodHandle invokeCleaner = lookup.findVirtual(
                                unsafeClass, "invokeCleaner", methodType(typeof(void), typeof(ByteBuffer)));
                        invokeCleaner = invokeCleaner.bindTo(PlatformDependent0.UNSAFE);
                        invokeCleaner.invokeExact(buffer);
                        return invokeCleaner;
                    } catch (Exception e) {
                        return e;
                    }
                }
            });

            if (maybeInvokeMethod instanceof Exception) {
                method = null;
                error = (Exception) maybeInvokeMethod;
            } else {
                method = (MethodHandle) maybeInvokeMethod;
                error = null;
            }
        } else {
            method = null;
            error = new UnsupportedOperationException("sun.misc.Unsafe unavailable");
        }
        if (error == null) {
            logger.debug("java.nio.ByteBuffer.cleaner(): available");
        } else {
            logger.debug("java.nio.ByteBuffer.cleaner(): unavailable", error);
        }
        INVOKE_CLEANER = method;
    }

    static bool isSupported() {
        return INVOKE_CLEANER != null;
    }

    @Override
    public CleanableDirectBuffer allocate(int capacity) {
        return new CleanableDirectBufferImpl(ByteBuffer.allocateDirect(capacity));
    }

    @Deprecated
    @Override
    public void freeDirectBuffer(ByteBuffer buffer) {
        freeDirectBufferStatic(buffer);
    }

    private static void freeDirectBufferStatic(ByteBuffer buffer) {
        // Try to minimize overhead when there is no SecurityManager present.
        // See https://bugs.openjdk.java.net/browse/JDK-8191053.
        if (System.getSecurityManager() == null) {
            try {
                INVOKE_CLEANER.invokeExact(buffer);
            } catch (Exception cause) {
                PlatformDependent0.throwException(cause);
            }
        } else {
            freeDirectBufferPrivileged(buffer);
        }
    }

    private static void freeDirectBufferPrivileged(final ByteBuffer buffer) {
        Exception error = AccessController.doPrivileged(new PrivilegedAction<Exception>() {
            @Override
            public Exception run() {
                try {
                    INVOKE_CLEANER.invokeExact(buffer);
                } catch (Exception e) {
                    return e;
                }
                return null;
            }
        });
        if (error != null) {
            PlatformDependent0.throwException(error);
        }
    }

    private static readonly class CleanableDirectBufferImpl : CleanableDirectBuffer {
        private readonly ByteBuffer buffer;

        private CleanableDirectBufferImpl(ByteBuffer buffer) {
            this.buffer = buffer;
        }

        @Override
        public ByteBuffer buffer() {
            return buffer;
        }

        @Override
        public void clean() {
            freeDirectBufferStatic(buffer);
        }
    }
}
