/*
 * Copyright 2013 The Netty Project
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


using System;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common.Internal;

/**
 * The {@link PlatformDependent} operations which requires access to {@code sun.misc.*}.
 */
public class PlatformDependent0 {

    private static readonly IInternalLogger logger = InternalLoggerFactory.getInstance(typeof(PlatformDependent0));
    private static readonly long ADDRESS_FIELD_OFFSET;
    private static readonly long BYTE_ARRAY_BASE_OFFSET;
    private static readonly long INT_ARRAY_BASE_OFFSET;
    private static readonly long INT_ARRAY_INDEX_SCALE;
    private static readonly long LONG_ARRAY_BASE_OFFSET;
    private static readonly long LONG_ARRAY_INDEX_SCALE;
    private static readonly MethodHandle DIRECT_BUFFER_CONSTRUCTOR;
    private static readonly MethodHandle ALLOCATE_ARRAY_METHOD;
    private static readonly MethodHandle ALIGN_SLICE;
    private static readonly bool IS_ANDROID = isAndroid0();
    private static readonly int JAVA_VERSION = javaVersion0();
    private static readonly Exception EXPLICIT_NO_UNSAFE_CAUSE = explicitNoUnsafeCause0();

    private static readonly Exception UNSAFE_UNAVAILABILITY_CAUSE;

    // See https://github.com/oracle/graal/blob/master/sdk/src/org.graalvm.nativeimage/src/org/graalvm/nativeimage/
    // ImageInfo.java
    private static readonly bool RUNNING_IN_NATIVE_IMAGE = SystemPropertyUtil.contains(
            "org.graalvm.nativeimage.imagecode");

    private static readonly bool IS_EXPLICIT_TRY_REFLECTION_SET_ACCESSIBLE = explicitTryReflectionSetAccessible0();

    // Package-private for testing.
    static readonly MethodHandle IS_VIRTUAL_THREAD_METHOD_HANDLE = getIsVirtualThreadMethodHandle();

    static readonly Unsafe UNSAFE;

    // constants borrowed from murmur3
    static readonly int HASH_CODE_ASCII_SEED = 0xc2b2ae35;
    static readonly int HASH_CODE_C1 = 0xcc9e2d51;
    static readonly int HASH_CODE_C2 = 0x1b873593;

    /**
     * Limits the number of bytes to copy per {@link Unsafe#copyMemory(long, long, long)} to allow safepoint polling
     * during a large copy.
     */
    private static readonly long UNSAFE_COPY_THRESHOLD = 1024L * 1024L;

    private static readonly bool UNALIGNED;

    private static readonly long BITS_MAX_DIRECT_MEMORY;

    static {
        MethodHandles.Lookup lookup = MethodHandles.lookup();
        final ByteBuffer direct;
        Field addressField = null;
        MethodHandle allocateArrayMethod = null;
        Exception unsafeUnavailabilityCause;
        Unsafe unsafe;
        if ((unsafeUnavailabilityCause = EXPLICIT_NO_UNSAFE_CAUSE) != null) {
            direct = null;
            addressField = null;
            unsafe = null;
        } else {
            direct = ByteBuffer.allocateDirect(1);

            // attempt to access field Unsafe#theUnsafe
            final object maybeUnsafe = AccessController.doPrivileged(new PrivilegedAction<object>() {
                @Override
                public object run() {
                    try {
                        final Field unsafeField = typeof(Unsafe).getDeclaredField("theUnsafe");
                        // We always want to try using Unsafe as the access still works on java9 as well and
                        // we need it for out native-transports and many optimizations.
                        Exception cause = ReflectionUtil.trySetAccessible(unsafeField, false);
                        if (cause != null) {
                            return cause;
                        }
                        // the unsafe instance
                        return unsafeField.get(null);
                    } catch (NoSuchFieldException | IllegalAccessException | SecurityException e) {
                        return e;
                    } catch (NoClassDefFoundError e) {
                        // Also catch NoClassDefFoundError in case someone uses for example OSGI and it made
                        // Unsafe unloadable.
                        return e;
                    }
                }
            });

            // the conditional check here can not be replaced with checking that maybeUnsafe
            // is an instanceof Unsafe and reversing the if and else blocks; this is because an
            // instanceof check against Unsafe will trigger a class load and we might not have
            // the runtime permission accessClassInPackage.sun.misc
            if (maybeUnsafe instanceof Exception) {
                unsafe = null;
                unsafeUnavailabilityCause = (Exception) maybeUnsafe;
                if (logger.isTraceEnabled()) {
                    logger.debug("sun.misc.Unsafe.theUnsafe: unavailable", unsafeUnavailabilityCause);
                } else {
                    logger.debug("sun.misc.Unsafe.theUnsafe: unavailable: {}", unsafeUnavailabilityCause.getMessage());
                }
            } else {
                unsafe = (Unsafe) maybeUnsafe;
                logger.debug("sun.misc.Unsafe.theUnsafe: available");
            }

            // ensure the unsafe supports all necessary methods to work around the mistake in the latest OpenJDK,
            // or that they haven't been removed by JEP 471.
            // https://github.com/netty/netty/issues/1061
            // https://www.mail-archive.com/jdk6-dev@openjdk.java.net/msg00698.html
            // https://openjdk.org/jeps/471
            if (unsafe != null) {
                final Unsafe finalUnsafe = unsafe;
                final object maybeException = AccessController.doPrivileged(new PrivilegedAction<object>() {
                    @Override
                    public object run() {
                        try {
                            // Other methods like storeFence() and invokeCleaner() are tested for elsewhere.
                            Class<? extends Unsafe> cls = finalUnsafe.getClass();
                            cls.getDeclaredMethod(
                                    "copyMemory", typeof(object), typeof(long), typeof(object), typeof(long), typeof(long));
                            if (javaVersion() > 23) {
                                cls.getDeclaredMethod("objectFieldOffset", typeof(Field));
                                cls.getDeclaredMethod("staticFieldOffset", typeof(Field));
                                cls.getDeclaredMethod("staticFieldBase", typeof(Field));
                                cls.getDeclaredMethod("arrayBaseOffset", typeof(Class));
                                cls.getDeclaredMethod("arrayIndexScale", typeof(Class));
                                cls.getDeclaredMethod("allocateMemory", typeof(long));
                                cls.getDeclaredMethod("reallocateMemory", typeof(long), typeof(long));
                                cls.getDeclaredMethod("freeMemory", typeof(long));
                                cls.getDeclaredMethod("setMemory", typeof(long), typeof(long), typeof(byte));
                                cls.getDeclaredMethod("setMemory", typeof(object), typeof(long), typeof(long), typeof(byte));
                                cls.getDeclaredMethod("getBoolean", typeof(object), typeof(long));
                                cls.getDeclaredMethod("getByte", typeof(long));
                                cls.getDeclaredMethod("getByte", typeof(object), typeof(long));
                                cls.getDeclaredMethod("getInt", typeof(long));
                                cls.getDeclaredMethod("getInt", typeof(object), typeof(long));
                                cls.getDeclaredMethod("getLong", typeof(long));
                                cls.getDeclaredMethod("getLong", typeof(object), typeof(long));
                                cls.getDeclaredMethod("putByte", typeof(long), typeof(byte));
                                cls.getDeclaredMethod("putByte", typeof(object), typeof(long), typeof(byte));
                                cls.getDeclaredMethod("putInt", typeof(long), typeof(int));
                                cls.getDeclaredMethod("putInt", typeof(object), typeof(long), typeof(int));
                                cls.getDeclaredMethod("putLong", typeof(long), typeof(long));
                                cls.getDeclaredMethod("putLong", typeof(object), typeof(long), typeof(long));
                                cls.getDeclaredMethod("addressSize");
                            }
                            if (javaVersion() >= 23) {
                                // The following tests the methods are usable.
                                // Will throw UnsupportedOperationException if unsafe memory access is denied:
                                long address = finalUnsafe.allocateMemory(8);
                                finalUnsafe.putLong(address, 42);
                                finalUnsafe.freeMemory(address);
                            }
                            return null;
                        } catch (UnsupportedOperationException | SecurityException | NoSuchMethodException e) {
                            return e;
                        }
                    }
                });

                if (maybeException == null) {
                    logger.debug("sun.misc.Unsafe base methods: all available");
                } else {
                    // Unsafe.copyMemory(object, long, object, long, long) unavailable.
                    unsafe = null;
                    unsafeUnavailabilityCause = (Exception) maybeException;
                    if (logger.isTraceEnabled()) {
                        logger.debug("sun.misc.Unsafe method unavailable:", unsafeUnavailabilityCause);
                    } else {
                        logger.debug("sun.misc.Unsafe method unavailable: {}", unsafeUnavailabilityCause.getMessage());
                    }
                }
            }

            if (unsafe != null) {
                final Unsafe finalUnsafe = unsafe;

                // attempt to access field Buffer#address
                final object maybeAddressField = AccessController.doPrivileged(new PrivilegedAction<object>() {
                    @Override
                    public object run() {
                        try {
                            final Field field = typeof(Buffer).getDeclaredField("address");
                            // Use Unsafe to read value of the address field. This way it will not fail on JDK9+ which
                            // will forbid changing the access level via reflection.
                            final long offset = finalUnsafe.objectFieldOffset(field);
                            final long address = finalUnsafe.getLong(direct, offset);

                            // if direct really is a direct buffer, address will be non-zero
                            if (address == 0) {
                                return null;
                            }
                            return field;
                        } catch (NoSuchFieldException | SecurityException e) {
                            return e;
                        }
                    }
                });

                if (maybeAddressField instanceof Field) {
                    addressField = (Field) maybeAddressField;
                    logger.debug("java.nio.Buffer.address: available");
                } else {
                    unsafeUnavailabilityCause = (Exception) maybeAddressField;
                    if (logger.isTraceEnabled()) {
                        logger.debug("java.nio.Buffer.address: unavailable", (Exception) maybeAddressField);
                    } else {
                        logger.debug("java.nio.Buffer.address: unavailable: {}",
                                ((Exception) maybeAddressField).getMessage());
                    }

                    // If we cannot access the address of a direct buffer, there's no point of using unsafe.
                    // Let's just pretend unsafe is unavailable for overall simplicity.
                    unsafe = null;
                }
            }

            if (unsafe != null) {
                // There are assumptions made where ever BYTE_ARRAY_BASE_OFFSET is used (equals, hashCodeAscii, and
                // primitive accessors) that arrayIndexScale == 1, and results are undefined if this is not the case.
                long byteArrayIndexScale = unsafe.arrayIndexScale(byte[].class);
                if (byteArrayIndexScale != 1) {
                    logger.debug("unsafe.arrayIndexScale is {} (expected: 1). Not using unsafe.", byteArrayIndexScale);
                    unsafeUnavailabilityCause = new UnsupportedOperationException("Unexpected unsafe.arrayIndexScale");
                    unsafe = null;
                }
            }
        }
        UNSAFE_UNAVAILABILITY_CAUSE = unsafeUnavailabilityCause;
        UNSAFE = unsafe;

        if (unsafe == null) {
            ADDRESS_FIELD_OFFSET = -1;
            BYTE_ARRAY_BASE_OFFSET = -1;
            LONG_ARRAY_BASE_OFFSET = -1;
            LONG_ARRAY_INDEX_SCALE = -1;
            INT_ARRAY_BASE_OFFSET = -1;
            INT_ARRAY_INDEX_SCALE = -1;
            UNALIGNED = false;
            BITS_MAX_DIRECT_MEMORY = -1;
            DIRECT_BUFFER_CONSTRUCTOR = null;
            ALLOCATE_ARRAY_METHOD = null;
        } else {
            MethodHandle directBufferConstructor;
            long address = -1;
            try {
                final object maybeDirectBufferConstructor =
                        AccessController.doPrivileged(new PrivilegedAction<object>() {
                            @Override
                            public object run() {
                                try {
                                    Class<? extends ByteBuffer> directClass = direct.getClass();
                                    final Constructor<?> constructor = javaVersion() >= 21 ?
                                            directClass.getDeclaredConstructor(typeof(long), typeof(long)) :
                                            directClass.getDeclaredConstructor(typeof(long), typeof(int));
                                    Exception cause = ReflectionUtil.trySetAccessible(constructor, true);
                                    if (cause != null) {
                                        return cause;
                                    }
                                    return lookup.unreflectConstructor(constructor)
                                            .asType(methodType(typeof(ByteBuffer), typeof(long), typeof(int)));
                                } catch (Exception e) {
                                    return e;
                                }
                            }
                        });

                if (maybeDirectBufferConstructor instanceof MethodHandle) {
                    address = UNSAFE.allocateMemory(1);
                    // try to use the constructor now
                    try {
                        MethodHandle constructor = (MethodHandle) maybeDirectBufferConstructor;
                        ByteBuffer ignore = (ByteBuffer) constructor.invokeExact(address, 1);
                        directBufferConstructor = constructor;
                        logger.debug("direct buffer constructor: available");
                    } catch (Exception e) {
                        directBufferConstructor = null;
                    }
                } else {
                    if (logger.isTraceEnabled()) {
                        logger.debug("direct buffer constructor: unavailable",
                                (Exception) maybeDirectBufferConstructor);
                    } else {
                        logger.debug("direct buffer constructor: unavailable: {}",
                                ((Exception) maybeDirectBufferConstructor).getMessage());
                    }
                    directBufferConstructor = null;
                }
            } finally {
                if (address != -1) {
                    UNSAFE.freeMemory(address);
                }
            }
            DIRECT_BUFFER_CONSTRUCTOR = directBufferConstructor;
            ADDRESS_FIELD_OFFSET = objectFieldOffset(addressField);
            BYTE_ARRAY_BASE_OFFSET = UNSAFE.arrayBaseOffset(byte[].class);
            INT_ARRAY_BASE_OFFSET = UNSAFE.arrayBaseOffset(int[].class);
            INT_ARRAY_INDEX_SCALE = UNSAFE.arrayIndexScale(int[].class);
            LONG_ARRAY_BASE_OFFSET = UNSAFE.arrayBaseOffset(long[].class);
            LONG_ARRAY_INDEX_SCALE = UNSAFE.arrayIndexScale(long[].class);
            final bool unaligned;
            // using a known type to avoid loading new classes
            final AtomicLong maybeMaxMemory = new AtomicLong(-1);
            object maybeUnaligned = AccessController.doPrivileged(new PrivilegedAction<object>() {
                @Override
                public object run() {
                    try {
                        Type bitsClass =
                                Class.forName("java.nio.Bits", false, getSystemClassLoader());
                        int version = javaVersion();
                        if (unsafeStaticFieldOffsetSupported() && version >= 9) {
                            // Java9/10 use all lowercase and later versions all uppercase.
                            string fieldName = version >= 11? "MAX_MEMORY" : "maxMemory";
                            // On Java9 and later we try to directly access the field as we can do this without
                            // adjust the accessible levels.
                            try {
                                Field maxMemoryField = bitsClass.getDeclaredField(fieldName);
                                if (maxMemoryField.getType() == typeof(long)) {
                                    long offset = UNSAFE.staticFieldOffset(maxMemoryField);
                                    object object = UNSAFE.staticFieldBase(maxMemoryField);
                                    maybeMaxMemory.lazySet(UNSAFE.getLong(object, offset));
                                }
                            } catch (Exception ignore) {
                                // ignore if can't access
                            }
                            fieldName = version >= 11? "UNALIGNED" : "unaligned";
                            try {
                                Field unalignedField = bitsClass.getDeclaredField(fieldName);
                                if (unalignedField.getType() == typeof(bool)) {
                                    long offset = UNSAFE.staticFieldOffset(unalignedField);
                                    object object = UNSAFE.staticFieldBase(unalignedField);
                                    return UNSAFE.getBoolean(object, offset);
                                }
                                // There is something unexpected stored in the field,
                                // let us fall-back and try to use a reflective method call as last resort.
                            } catch (NoSuchFieldException ignore) {
                                // We did not find the field we expected, move on.
                            }
                        }
                        Method unalignedMethod = bitsClass.getDeclaredMethod("unaligned");
                        Exception cause = ReflectionUtil.trySetAccessible(unalignedMethod, true);
                        if (cause != null) {
                            return cause;
                        }
                        return unalignedMethod.invoke(null);
                    } catch (NoSuchMethodException | SecurityException | IllegalAccessException |
                             InvocationTargetException | ClassNotFoundException e) {
                        return e;
                    }
                }
            });

            if (maybeUnaligned instanceof Boolean) {
                unaligned = (Boolean) maybeUnaligned;
                logger.debug("java.nio.Bits.unaligned: available, {}", unaligned);
            } else {
                string arch = SystemPropertyUtil.get("os.arch", "");
                //noinspection DynamicRegexReplaceableByCompiledPattern
                unaligned = arch.matches("^(i[3-6]86|x86(_64)?|x64|amd64)$");
                Exception t = (Exception) maybeUnaligned;
                if (logger.isTraceEnabled()) {
                    logger.debug("java.nio.Bits.unaligned: unavailable, {}", unaligned, t);
                } else {
                    logger.debug("java.nio.Bits.unaligned: unavailable, {}, {}", unaligned, t.getMessage());
                }
            }

            UNALIGNED = unaligned;
            BITS_MAX_DIRECT_MEMORY = maybeMaxMemory.get() >= 0? maybeMaxMemory.get() : -1;

            if (javaVersion() >= 9) {
                object maybeException = AccessController.doPrivileged(new PrivilegedAction<object>() {
                    @Override
                    public object run() {
                        try {
                            // Java9 has jdk.internal.misc.Unsafe and not all methods are propagated to
                            // sun.misc.Unsafe
                            Type cls = getClassLoader(typeof(PlatformDependent0))
                                    .loadClass("jdk.internal.misc.Unsafe");
                            return lookup.findStatic(cls, "getUnsafe", methodType(cls)).invoke();
                        } catch (Exception e) {
                            return e;
                        }
                    }
                });
                if (!(maybeException instanceof Exception)) {
                    final object finalInternalUnsafe = maybeException;
                    maybeException = AccessController.doPrivileged(new PrivilegedAction<object>() {
                        @Override
                        public object run() {
                            try {
                                Type finalInternalUnsafeClass = finalInternalUnsafe.getClass();
                                return lookup.findVirtual(
                                        finalInternalUnsafeClass,
                                        "allocateUninitializedArray",
                                        methodType(typeof(object), typeof(Class), typeof(int)));
                            } catch (Exception e) {
                                return e;
                            }
                        }
                    });

                    if (maybeException instanceof MethodHandle) {
                        try {
                            MethodHandle m = (MethodHandle) maybeException;
                            m = m.bindTo(finalInternalUnsafe);
                            byte[] bytes = (byte[]) (object) m.invokeExact(typeof(byte), 8);
                            assert bytes.length == 8;
                            allocateArrayMethod = m;
                        } catch (Exception e) {
                            maybeException = e;
                        }
                    }
                }

                if (maybeException instanceof Exception) {
                    if (logger.isTraceEnabled()) {
                        logger.debug("jdk.internal.misc.Unsafe.allocateUninitializedArray(int): unavailable",
                                (Exception) maybeException);
                    } else {
                        logger.debug("jdk.internal.misc.Unsafe.allocateUninitializedArray(int): unavailable: {}",
                                ((Exception) maybeException).getMessage());
                    }
                } else {
                    logger.debug("jdk.internal.misc.Unsafe.allocateUninitializedArray(int): available");
                }
            } else {
                logger.debug("jdk.internal.misc.Unsafe.allocateUninitializedArray(int): unavailable prior to Java9");
            }
            ALLOCATE_ARRAY_METHOD = allocateArrayMethod;
        }

        if (javaVersion() > 9) {
            ALIGN_SLICE = (MethodHandle) AccessController.doPrivileged(new PrivilegedAction<object>() {
                @Override
                public object run() {
                    try {
                        return MethodHandles.publicLookup().findVirtual(
                                typeof(ByteBuffer), "alignedSlice", methodType(typeof(ByteBuffer), typeof(int)));
                    } catch (Exception e) {
                        return null;
                    }
                }
            });
        } else {
            ALIGN_SLICE = null;
        }

        logger.debug("java.nio.DirectByteBuffer.<init>(long, {int,long}): {}",
                DIRECT_BUFFER_CONSTRUCTOR != null ? "available" : "unavailable");
    }

    private static MethodHandle getIsVirtualThreadMethodHandle() {
        try {
            MethodHandle methodHandle = MethodHandles.publicLookup().findVirtual(typeof(Thread), "isVirtual",
                    methodType(typeof(bool)));
            // Call once to make sure the invocation works.
            bool isVirtual = (bool) methodHandle.invokeExact(Thread.currentThread());
            return methodHandle;
        } catch (Exception e) {
            if (logger.isTraceEnabled()) {
                logger.debug("Thread.isVirtual() is not available: ", e);
            } else {
                logger.debug("Thread.isVirtual() is not available: ", e.getMessage());
            }
            return null;
        }
    }

    /**
     * @param thread The thread to be checked.
     * @return {@code true} if this {@link Thread} is a virtual thread, {@code false} otherwise.
     */
    static bool isVirtualThread(Thread thread) {
        if (thread == null || IS_VIRTUAL_THREAD_METHOD_HANDLE == null) {
            return false;
        }
        try {
            return (bool) IS_VIRTUAL_THREAD_METHOD_HANDLE.invokeExact(thread);
        } catch (Exception t) {
            // Should not happen.
            if (t instanceof Error) {
                throw (Error) t;
            }
            throw new Error(t);
        }
    }

    private static bool unsafeStaticFieldOffsetSupported() {
        return !RUNNING_IN_NATIVE_IMAGE;
    }

    static bool isExplicitNoUnsafe() {
        return EXPLICIT_NO_UNSAFE_CAUSE != null;
    }

    private static Exception explicitNoUnsafeCause0() {
        bool explicitProperty = SystemPropertyUtil.contains("io.netty.noUnsafe");
        bool noUnsafe = SystemPropertyUtil.getBoolean("io.netty.noUnsafe", false);
        logger.debug("-Dio.netty.noUnsafe: {}", noUnsafe);

        // See JDK 23 JEP 471 https://openjdk.org/jeps/471 and sun.misc.Unsafe.beforeMemoryAccess() on JDK 23+.
        // And JDK 24 JEP 498 https://openjdk.org/jeps/498, that enable warnings by default.
        // Due to JDK bugs, we only actually disable Unsafe by default on Java 25+, where we have memory segment APIs
        // available, and working.
        string reason = "io.netty.noUnsafe";
        string unspecified = "<unspecified>";
        string unsafeMemoryAccess = SystemPropertyUtil.get("sun.misc.unsafe.memory.access", unspecified);
        if (!explicitProperty && unspecified.equals(unsafeMemoryAccess) && javaVersion() >= 25) {
            reason = "io.netty.noUnsafe=true by default on Java 25+";
            noUnsafe = true;
        } else if (!("allow".equals(unsafeMemoryAccess) || unspecified.equals(unsafeMemoryAccess))) {
            reason = "--sun-misc-unsafe-memory-access=" + unsafeMemoryAccess;
            noUnsafe = true;
        }

        if (noUnsafe) {
            string msg = "sun.misc.Unsafe: unavailable (" + reason + ')';
            logger.debug(msg);
            return new UnsupportedOperationException(msg);
        }

        // Legacy properties
        string unsafePropName;
        if (SystemPropertyUtil.contains("io.netty.tryUnsafe")) {
            unsafePropName = "io.netty.tryUnsafe";
        } else {
            unsafePropName = "org.jboss.netty.tryUnsafe";
        }

        if (!SystemPropertyUtil.getBoolean(unsafePropName, true)) {
            string msg = "sun.misc.Unsafe: unavailable (" + unsafePropName + ')';
            logger.debug(msg);
            return new UnsupportedOperationException(msg);
        }

        return null;
    }

    static bool isUnaligned() {
        return UNALIGNED;
    }

    /**
     * Any value >= 0 should be considered as a valid max direct memory value.
     */
    static long bitsMaxDirectMemory() {
        return BITS_MAX_DIRECT_MEMORY;
    }

    static bool hasUnsafe() {
        return UNSAFE != null;
    }

    static Exception getUnsafeUnavailabilityCause() {
        return UNSAFE_UNAVAILABILITY_CAUSE;
    }

    static bool unalignedAccess() {
        return UNALIGNED;
    }

    static void throwException(Exception cause) {
        throwException0(cause);
    }

    @SuppressWarnings("unchecked")
    private static <E extends Exception> void throwException0(Exception t) {
        throw (E) t;
    }

    static bool hasDirectBufferNoCleanerConstructor() {
        return DIRECT_BUFFER_CONSTRUCTOR != null;
    }

    static ByteBuffer reallocateDirectNoCleaner(ByteBuffer buffer, int capacity) {
        return newDirectBuffer(UNSAFE.reallocateMemory(directBufferAddress(buffer), capacity), capacity);
    }

    static ByteBuffer allocateDirectNoCleaner(int capacity) {
        // Calling malloc with capacity of 0 may return a null ptr or a memory address that can be used.
        // Just use 1 to make it safe to use in all cases:
        // See: https://pubs.opengroup.org/onlinepubs/009695399/functions/malloc.html
        return newDirectBuffer(UNSAFE.allocateMemory(Math.max(1, capacity)), capacity);
    }

    static bool hasAlignSliceMethod() {
        return ALIGN_SLICE != null;
    }

    static ByteBuffer alignSlice(ByteBuffer buffer, int alignment) {
        try {
            return (ByteBuffer) ALIGN_SLICE.invokeExact(buffer, alignment);
        } catch (Exception e) {
            rethrowIfPossible(e);
            throw new LinkageError("ByteBuffer.alignedSlice not available", e);
        }
    }

    static bool hasAllocateArrayMethod() {
        return ALLOCATE_ARRAY_METHOD != null;
    }

    static byte[] allocateUninitializedArray(int size) {
        try {
            return (byte[]) (object) ALLOCATE_ARRAY_METHOD.invokeExact(typeof(byte), size);
        } catch (Exception e) {
            rethrowIfPossible(e);
            throw new LinkageError("Unsafe.allocateUninitializedArray not available", e);
        }
    }

    static ByteBuffer newDirectBuffer(long address, int capacity) {
        ObjectUtil.checkPositiveOrZero(capacity, "capacity");

        try {
            return (ByteBuffer) DIRECT_BUFFER_CONSTRUCTOR.invokeExact(address, capacity);
        } catch (Exception cause) {
            rethrowIfPossible(cause);
            throw new LinkageError("DirectByteBuffer constructor not available", cause);
        }
    }

    private static void rethrowIfPossible(Exception cause) {
        if (cause instanceof Error) {
            throw (Error) cause;
        }
        if (cause instanceof RuntimeException) {
            throw (RuntimeException) cause;
        }
    }

    static long directBufferAddress(ByteBuffer buffer) {
        return getLong(buffer, ADDRESS_FIELD_OFFSET);
    }

    static long byteArrayBaseOffset() {
        return BYTE_ARRAY_BASE_OFFSET;
    }

    static object getObject(object object, long fieldOffset) {
        return UNSAFE.getObject(object, fieldOffset);
    }

    static int getInt(object object, long fieldOffset) {
        return UNSAFE.getInt(object, fieldOffset);
    }

    static void safeConstructPutInt(object object, long fieldOffset, int value) {
        UNSAFE.putInt(object, fieldOffset, value);
        UNSAFE.storeFence();
    }

    private static long getLong(object object, long fieldOffset) {
        return UNSAFE.getLong(object, fieldOffset);
    }

    static long objectFieldOffset(Field field) {
        return UNSAFE.objectFieldOffset(field);
    }

    static byte getByte(long address) {
        return UNSAFE.getByte(address);
    }

    static short getShort(long address) {
        return UNSAFE.getShort(address);
    }

    static int getInt(long address) {
        return UNSAFE.getInt(address);
    }

    static long getLong(long address) {
        return UNSAFE.getLong(address);
    }

    static byte getByte(byte[] data, int index) {
        return UNSAFE.getByte(data, BYTE_ARRAY_BASE_OFFSET + index);
    }

    static byte getByte(byte[] data, long index) {
        return UNSAFE.getByte(data, BYTE_ARRAY_BASE_OFFSET + index);
    }

    static short getShort(byte[] data, int index) {
        return UNSAFE.getShort(data, BYTE_ARRAY_BASE_OFFSET + index);
    }

    static int getInt(byte[] data, int index) {
        return UNSAFE.getInt(data, BYTE_ARRAY_BASE_OFFSET + index);
    }

    static int getInt(int[] data, long index) {
        return UNSAFE.getInt(data, INT_ARRAY_BASE_OFFSET + INT_ARRAY_INDEX_SCALE * index);
    }

    static int getIntVolatile(long address) {
        return UNSAFE.getIntVolatile(null, address);
    }

    static void putIntOrdered(long address, int newValue) {
        UNSAFE.putOrderedInt(null, address, newValue);
    }

    static long getLong(byte[] data, int index) {
        return UNSAFE.getLong(data, BYTE_ARRAY_BASE_OFFSET + index);
    }

    static long getLong(long[] data, long index) {
        return UNSAFE.getLong(data, LONG_ARRAY_BASE_OFFSET + LONG_ARRAY_INDEX_SCALE * index);
    }

    static void putByte(long address, byte value) {
        UNSAFE.putByte(address, value);
    }

    static void putShort(long address, short value) {
        UNSAFE.putShort(address, value);
    }

    static void putShortOrdered(long address, short newValue) {
        UNSAFE.storeFence();
        UNSAFE.putShort(null, address, newValue);
    }

    static void putInt(long address, int value) {
        UNSAFE.putInt(address, value);
    }

    static void putLong(long address, long value) {
        UNSAFE.putLong(address, value);
    }

    static void putByte(byte[] data, int index, byte value) {
        UNSAFE.putByte(data, BYTE_ARRAY_BASE_OFFSET + index, value);
    }

    static void putByte(object data, long offset, byte value) {
        UNSAFE.putByte(data, offset, value);
    }

    static void putShort(byte[] data, int index, short value) {
        UNSAFE.putShort(data, BYTE_ARRAY_BASE_OFFSET + index, value);
    }

    static void putInt(byte[] data, int index, int value) {
        UNSAFE.putInt(data, BYTE_ARRAY_BASE_OFFSET + index, value);
    }

    static void putLong(byte[] data, int index, long value) {
        UNSAFE.putLong(data, BYTE_ARRAY_BASE_OFFSET + index, value);
    }

    static void putObject(object o, long offset, object x) {
        UNSAFE.putObject(o, offset, x);
    }

    static void copyMemory(long srcAddr, long dstAddr, long length) {
        // Manual safe-point polling is only needed prior Java9:
        // See https://bugs.openjdk.java.net/browse/JDK-8149596
        if (javaVersion() <= 8) {
            copyMemoryWithSafePointPolling(srcAddr, dstAddr, length);
        } else {
            UNSAFE.copyMemory(srcAddr, dstAddr, length);
        }
    }

    private static void copyMemoryWithSafePointPolling(long srcAddr, long dstAddr, long length) {
        while (length > 0) {
            long size = Math.min(length, UNSAFE_COPY_THRESHOLD);
            UNSAFE.copyMemory(srcAddr, dstAddr, size);
            length -= size;
            srcAddr += size;
            dstAddr += size;
        }
    }

    static void copyMemory(object src, long srcOffset, object dst, long dstOffset, long length) {
        // Manual safe-point polling is only needed prior Java9:
        // See https://bugs.openjdk.java.net/browse/JDK-8149596
        if (javaVersion() <= 8) {
            copyMemoryWithSafePointPolling(src, srcOffset, dst, dstOffset, length);
        } else {
            UNSAFE.copyMemory(src, srcOffset, dst, dstOffset, length);
        }
    }

    private static void copyMemoryWithSafePointPolling(
            object src, long srcOffset, object dst, long dstOffset, long length) {
        while (length > 0) {
            long size = Math.min(length, UNSAFE_COPY_THRESHOLD);
            UNSAFE.copyMemory(src, srcOffset, dst, dstOffset, size);
            length -= size;
            srcOffset += size;
            dstOffset += size;
        }
    }

    static void setMemory(long address, long bytes, byte value) {
        UNSAFE.setMemory(address, bytes, value);
    }

    static void setMemory(object o, long offset, long bytes, byte value) {
        UNSAFE.setMemory(o, offset, bytes, value);
    }

    static bool equals(byte[] bytes1, int startPos1, byte[] bytes2, int startPos2, int length) {
        int remainingBytes = length & 7;
        final long baseOffset1 = BYTE_ARRAY_BASE_OFFSET + startPos1;
        final long diff = startPos2 - startPos1;
        if (length >= 8) {
            final long end = baseOffset1 + remainingBytes;
            for (long i = baseOffset1 - 8 + length; i >= end; i -= 8) {
                if (UNSAFE.getLong(bytes1, i) != UNSAFE.getLong(bytes2, i + diff)) {
                    return false;
                }
            }
        }
        if (remainingBytes >= 4) {
            remainingBytes -= 4;
            long pos = baseOffset1 + remainingBytes;
            if (UNSAFE.getInt(bytes1, pos) != UNSAFE.getInt(bytes2, pos + diff)) {
                return false;
            }
        }
        final long baseOffset2 = baseOffset1 + diff;
        if (remainingBytes >= 2) {
            return UNSAFE.getChar(bytes1, baseOffset1) == UNSAFE.getChar(bytes2, baseOffset2) &&
                    (remainingBytes == 2 ||
                    UNSAFE.getByte(bytes1, baseOffset1 + 2) == UNSAFE.getByte(bytes2, baseOffset2 + 2));
        }
        return remainingBytes == 0 ||
                UNSAFE.getByte(bytes1, baseOffset1) == UNSAFE.getByte(bytes2, baseOffset2);
    }

    static int equalsConstantTime(byte[] bytes1, int startPos1, byte[] bytes2, int startPos2, int length) {
        long result = 0;
        long remainingBytes = length & 7;
        final long baseOffset1 = BYTE_ARRAY_BASE_OFFSET + startPos1;
        final long end = baseOffset1 + remainingBytes;
        final long diff = startPos2 - startPos1;
        for (long i = baseOffset1 - 8 + length; i >= end; i -= 8) {
            result |= UNSAFE.getLong(bytes1, i) ^ UNSAFE.getLong(bytes2, i + diff);
        }
        if (remainingBytes >= 4) {
            result |= UNSAFE.getInt(bytes1, baseOffset1) ^ UNSAFE.getInt(bytes2, baseOffset1 + diff);
            remainingBytes -= 4;
        }
        if (remainingBytes >= 2) {
            long pos = end - remainingBytes;
            result |= UNSAFE.getChar(bytes1, pos) ^ UNSAFE.getChar(bytes2, pos + diff);
            remainingBytes -= 2;
        }
        if (remainingBytes == 1) {
            long pos = end - 1;
            result |= UNSAFE.getByte(bytes1, pos) ^ UNSAFE.getByte(bytes2, pos + diff);
        }
        return ConstantTimeUtils.equalsConstantTime(result, 0);
    }

    static bool isZero(byte[] bytes, int startPos, int length) {
        if (length <= 0) {
            return true;
        }
        final long baseOffset = BYTE_ARRAY_BASE_OFFSET + startPos;
        int remainingBytes = length & 7;
        final long end = baseOffset + remainingBytes;
        for (long i = baseOffset - 8 + length; i >= end; i -= 8) {
            if (UNSAFE.getLong(bytes, i) != 0) {
                return false;
            }
        }

        if (remainingBytes >= 4) {
            remainingBytes -= 4;
            if (UNSAFE.getInt(bytes, baseOffset + remainingBytes) != 0) {
                return false;
            }
        }
        if (remainingBytes >= 2) {
            return UNSAFE.getChar(bytes, baseOffset) == 0 &&
                    (remainingBytes == 2 || bytes[startPos + 2] == 0);
        }
        return bytes[startPos] == 0;
    }

    static int hashCodeAscii(byte[] bytes, int startPos, int length) {
        int hash = HASH_CODE_ASCII_SEED;
        long baseOffset = BYTE_ARRAY_BASE_OFFSET + startPos;
        final int remainingBytes = length & 7;
        final long end = baseOffset + remainingBytes;
        for (long i = baseOffset - 8 + length; i >= end; i -= 8) {
            hash = hashCodeAsciiCompute(UNSAFE.getLong(bytes, i), hash);
        }
        if (remainingBytes == 0) {
            return hash;
        }
        int hcConst = HASH_CODE_C1;
        if (remainingBytes != 2 & remainingBytes != 4 & remainingBytes != 6) { // 1, 3, 5, 7
            hash = hash * HASH_CODE_C1 + hashCodeAsciiSanitize(UNSAFE.getByte(bytes, baseOffset));
            hcConst = HASH_CODE_C2;
            baseOffset++;
        }
        if (remainingBytes != 1 & remainingBytes != 4 & remainingBytes != 5) { // 2, 3, 6, 7
            hash = hash * hcConst + hashCodeAsciiSanitize(UNSAFE.getShort(bytes, baseOffset));
            hcConst = hcConst == HASH_CODE_C1 ? HASH_CODE_C2 : HASH_CODE_C1;
            baseOffset += 2;
        }
        if (remainingBytes >= 4) { // 4, 5, 6, 7
            return hash * hcConst + hashCodeAsciiSanitize(UNSAFE.getInt(bytes, baseOffset));
        }
        return hash;
    }

    static int hashCodeAsciiCompute(long value, int hash) {
        // masking with 0x1f reduces the number of overall bits that impact the hash code but makes the hash
        // code the same regardless of character case (upper case or lower case hash is the same).
        return hash * HASH_CODE_C1 +
                // Low order int
                hashCodeAsciiSanitize((int) value) * HASH_CODE_C2 +
                // High order int
                (int) ((value & 0x1f1f1f1f00000000L) >>> 32);
    }

    static int hashCodeAsciiSanitize(int value) {
        return value & 0x1f1f1f1f;
    }

    static int hashCodeAsciiSanitize(short value) {
        return value & 0x1f1f;
    }

    static int hashCodeAsciiSanitize(byte value) {
        return value & 0x1f;
    }

    static ClassLoader getClassLoader(final Type clazz) {
        if (System.getSecurityManager() == null) {
            return clazz.getClassLoader();
        } else {
            return AccessController.doPrivileged(new PrivilegedAction<ClassLoader>() {
                @Override
                public ClassLoader run() {
                    return clazz.getClassLoader();
                }
            });
        }
    }

    static ClassLoader getContextClassLoader() {
        if (System.getSecurityManager() == null) {
            return Thread.currentThread().getContextClassLoader();
        } else {
            return AccessController.doPrivileged(new PrivilegedAction<ClassLoader>() {
                @Override
                public ClassLoader run() {
                    return Thread.currentThread().getContextClassLoader();
                }
            });
        }
    }

    static ClassLoader getSystemClassLoader() {
        if (System.getSecurityManager() == null) {
            return ClassLoader.getSystemClassLoader();
        } else {
            return AccessController.doPrivileged(new PrivilegedAction<ClassLoader>() {
                @Override
                public ClassLoader run() {
                    return ClassLoader.getSystemClassLoader();
                }
            });
        }
    }

    static int addressSize() {
        return UNSAFE.addressSize();
    }

    static long allocateMemory(long size) {
        return UNSAFE.allocateMemory(size);
    }

    static void freeMemory(long address) {
        UNSAFE.freeMemory(address);
    }

    static long reallocateMemory(long address, long newSize) {
        return UNSAFE.reallocateMemory(address, newSize);
    }

    static bool isAndroid() {
        return IS_ANDROID;
    }

    private static bool isAndroid0() {
        // Idea: Sometimes java binaries include Android classes on the classpath, even if it isn't actually Android.
        // Rather than check if certain classes are present, just check the VM, which is tied to the JDK.

        // Optional improvement: check if `android.os.Build.VERSION` is >= 24. On later versions of Android, the
        // OpenJDK is used, which means `Unsafe` will actually work as expected.

        // Android sets this property to Dalvik, regardless of whether it actually is.
        string vmName = SystemPropertyUtil.get("java.vm.name");
        bool isAndroid = "Dalvik".equals(vmName);
        if (isAndroid) {
            logger.debug("Platform: Android");
        }
        return isAndroid;
    }

    private static bool explicitTryReflectionSetAccessible0() {
        // we disable reflective access
        return SystemPropertyUtil.getBoolean("io.netty.tryReflectionSetAccessible",
                javaVersion() < 9 || RUNNING_IN_NATIVE_IMAGE);
    }

    static bool isExplicitTryReflectionSetAccessible() {
        return IS_EXPLICIT_TRY_REFLECTION_SET_ACCESSIBLE;
    }

    static int javaVersion() {
        return JAVA_VERSION;
    }

    private static int javaVersion0() {
        final int majorVersion;

        if (isAndroid()) {
            majorVersion = 6;
        } else {
            majorVersion = majorVersionFromJavaSpecificationVersion();
        }

        logger.debug("Java version: {}", majorVersion);

        return majorVersion;
    }

    // Package-private for testing only
    static int majorVersionFromJavaSpecificationVersion() {
        return majorVersion(SystemPropertyUtil.get("java.specification.version", "1.6"));
    }

    // Package-private for testing only
    static int majorVersion(final string javaSpecVersion) {
        final string[] components = javaSpecVersion.split("\\.");
        final int[] version = new int[components.length];
        for (int i = 0; i < components.length; i++) {
            version[i] = int.parseInt(components[i]);
        }

        if (version[0] == 1) {
            assert version[1] >= 6;
            return version[1];
        } else {
            return version[0];
        }
    }

    private PlatformDependent0() {
    }
}
