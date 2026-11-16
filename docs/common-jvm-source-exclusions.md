# JVM-only Common sources and members

Baseline: 64cc10f38ea5f5bd7eae48507817c66680d0afdc (Netty 4.2.19.Final).
Companion of [JVM-only test exclusions](common-jvm-test-exclusions.md). Every original
comment of the files and members below is archived verbatim in a `java` fence, so
`tools/Test-CommonCommentCoverage.ps1` counts it for the manifest entry that lists this file.
Recorded on 2026-11-16 by the Common closure review.

## Build and packaging artifacts (not-applicable)

| Upstream | Java purpose | Why the CLR port has no counterpart |
| --- | --- | --- |
| `internal/SuppressJava8Requirement` | `@Retention(CLASS)` marker for the Animal Sniffer build check, which verifies that classes compiled with a newer JDK only use the API signatures of an older one ([Animal Sniffer](https://www.mojohaus.org/animal-sniffer/)). No use site at the pinned commit. | The CLR build selects its API surface with the target framework; there is no JDK signature check to suppress. |
| `internal/shaded/org/jctools/*/DoNotRemove` (9) | "Placeholder for module-info maven plugin": JPMS may only `exports` a package that is declared by a compilation unit of the module ([JLS 21 §7.7.2](https://docs.oracle.com/javase/specs/jls/se21/html/jls-7.html)); the JCTools classes are shaded in at build time. | A .NET assembly has no package-export descriptor; visibility outside the assembly is decided by `public`/`internal` ([C# access modifiers](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/access-modifiers)). The JCTools queues themselves are replaced by BCL/native queues (PlatformDependent entry; common-clr-design.md#native-mpsc-handoffs-and-removed-provider-scaffolding). |
| `internal/shaded/org/jctools/*/package-info` (9) | "Placeholder package." declarations for the same shaded packages ([JLS 21 §7.4.1](https://docs.oracle.com/javase/specs/jls/se21/html/jls-7.html)). | No shaded JCTools namespace exists in the CLR port, so there is nothing to document. |
| `internal/svm/*Substitution` (4), `internal/svm/package-info` | GraalVM Substrate VM `@TargetClass`/`@Alias`/`@RecomputeFieldValue` recompute cached `Unsafe` offsets (`ADDRESS_FIELD_OFFSET`, `BYTE_ARRAY_BASE_OFFSET`, `RefCnt$UnsafeRefCnt.VALUE_OFFSET`, `REF_ELEMENT_SHIFT`) for the native image. | The CLR port caches no `Unsafe` offsets: the PlatformDependent offsets are retired, RefCnt is replaced by caller-owned `int` fields updated with `Interlocked`/`Volatile` (RefCnt entry) and JCTools is not shaded. .NET Native AOT compiles IL to native code at publish time and documents no source-substitution step ([Native AOT deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)). No NativeAOT validation is claimed here. |

### common/src/main/java/io/netty/util/internal/SuppressJava8Requirement.java

```java
/*
 * Copyright 2024 The Netty Project
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

/**
 * Annotation to suppress the Java 8 source code requirement checks for a method.
 */
```

### common/src/main/java/io/netty/util/internal/shaded/org/jctools/counters/DoNotRemove.java

```java
/*
 * Copyright 2024 The Netty Project
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

// Placeholder for module-info maven plugin to add the package to the module-info descriptor
```

### common/src/main/java/io/netty/util/internal/shaded/org/jctools/counters/package-info.java

```java
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

/**
 * Placeholder package.
 */
```

### common/src/main/java/io/netty/util/internal/shaded/org/jctools/maps/DoNotRemove.java

```java
/*
 * Copyright 2024 The Netty Project
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

// Placeholder for module-info maven plugin to add the package to the module-info descriptor
```

### common/src/main/java/io/netty/util/internal/shaded/org/jctools/maps/package-info.java

```java
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

/**
 * Placeholder package.
 */
```

### common/src/main/java/io/netty/util/internal/shaded/org/jctools/queues/DoNotRemove.java

```java
/*
 * Copyright 2024 The Netty Project
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

// Placeholder for module-info maven plugin to add the package to the module-info descriptor
```

### common/src/main/java/io/netty/util/internal/shaded/org/jctools/queues/package-info.java

```java
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

/**
 * Placeholder package.
 */
```

### common/src/main/java/io/netty/util/internal/shaded/org/jctools/queues/atomic/DoNotRemove.java

```java
/*
 * Copyright 2024 The Netty Project
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

// Placeholder for module-info maven plugin to add the package to the module-info descriptor
```

### common/src/main/java/io/netty/util/internal/shaded/org/jctools/queues/atomic/package-info.java

```java
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

/**
 * Placeholder package.
 */
```

### common/src/main/java/io/netty/util/internal/shaded/org/jctools/queues/atomic/unpadded/DoNotRemove.java

```java
/*
 * Copyright 2024 The Netty Project
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

// Placeholder for module-info maven plugin to add the package to the module-info descriptor
```

### common/src/main/java/io/netty/util/internal/shaded/org/jctools/queues/atomic/unpadded/package-info.java

```java
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

/**
 * Placeholder package.
 */
```

### common/src/main/java/io/netty/util/internal/shaded/org/jctools/queues/unpadded/DoNotRemove.java

```java
/*
 * Copyright 2024 The Netty Project
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

// Placeholder for module-info maven plugin to add the package to the module-info descriptor
```

### common/src/main/java/io/netty/util/internal/shaded/org/jctools/queues/unpadded/package-info.java

```java
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

/**
 * Placeholder package.
 */
```

### common/src/main/java/io/netty/util/internal/shaded/org/jctools/queues/varhandle/DoNotRemove.java

```java
/*
 * Copyright 2026 The Netty Project
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

// Placeholder for module-info maven plugin to add the package to the module-info descriptor
```

### common/src/main/java/io/netty/util/internal/shaded/org/jctools/queues/varhandle/package-info.java

```java
/*
 * Copyright 2026 The Netty Project
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

/**
 * Placeholder package.
 */
```

### common/src/main/java/io/netty/util/internal/shaded/org/jctools/queues/varhandle/unpadded/DoNotRemove.java

```java
/*
 * Copyright 2026 The Netty Project
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

// Placeholder for module-info maven plugin to add the package to the module-info descriptor
```

### common/src/main/java/io/netty/util/internal/shaded/org/jctools/queues/varhandle/unpadded/package-info.java

```java
/*
 * Copyright 2026 The Netty Project
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

/**
 * Placeholder package.
 */
```

### common/src/main/java/io/netty/util/internal/shaded/org/jctools/util/DoNotRemove.java

```java
/*
 * Copyright 2024 The Netty Project
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

// Placeholder for module-info maven plugin to add the package to the module-info descriptor
```

### common/src/main/java/io/netty/util/internal/shaded/org/jctools/util/package-info.java

```java
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

/**
 * Placeholder package.
 */
```

### common/src/main/java/io/netty/util/internal/svm/PlatformDependent0Substitution.java

```java
/*
 * Copyright 2019 The Netty Project
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
```

### common/src/main/java/io/netty/util/internal/svm/PlatformDependentSubstitution.java

```java
/*
 * Copyright 2019 The Netty Project
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

/**
     * The class PlatformDependent caches the byte array base offset by reading the
     * field from PlatformDependent0. The automatic recomputation of Substrate VM
     * correctly recomputes the field in PlatformDependent0, but since the caching
     * in PlatformDependent happens during image building, the non-recomputed value
     * is cached.
     */
```

### common/src/main/java/io/netty/util/internal/svm/RefCntSubstitution.java

```java
/*
 * Copyright 2025 The Netty Project
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
```

### common/src/main/java/io/netty/util/internal/svm/UnsafeRefArrayAccessSubstitution.java

```java
/*
 * Copyright 2019 The Netty Project
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
```

### common/src/main/java/io/netty/util/internal/svm/package-info.java

```java
/*
 * Copyright 2019 The Netty Project
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

/**
 * SVM substitutions for classes that will cause trouble while compiling
 * into native image.
 */
```

## Deprecated sources not ported (deprecated-not-ported)

Owner decision (2026-11-16): `@Deprecated` Java types are not ported; each entry records
the deprecation text and the replacement. The C# `CommonsLogger`/`CommonsLoggerFactory`
translations and their two translated tests were deleted for this reason.

| Upstream | `@deprecated` text | Replacement |
| --- | --- | --- |
| `ResourceLeakException` | "This class will be removed in the future version." | None named. No use site at the pinned commit; leaks are reported through `ResourceLeakDetector`/`ResourceLeakTracker` (ported). |
| `internal/SuppressJava6Requirement` | `@Deprecated` without Javadoc text; the type documents "Annotation to suppress the Java 6 source code requirement checks for a method." | None named; the Java 8 variant `SuppressJava8Requirement` is itself a build-check marker (not-applicable above). No use site. |
| `internal/logging/CommonsLogger` | "Please use {@link Log4J2Logger} or {@link Log4JLogger} or {@link Slf4JLogger}." | The named JVM providers are not-applicable; the CLR provider is `InternalDefaultLogger` (TraceSource), the recorded JdkLogger replacement. |
| `internal/logging/CommonsLoggerFactory` | "Please use {@link Log4J2LoggerFactory} or {@link Log4JLoggerFactory} or {@link Slf4JLoggerFactory}." | `InternalDefaultLoggerFactory`, the default of `InternalLoggerFactory.GetDefaultFactory()`. |
| tests `CommonsLoggerTest`, `CommonsLoggerFactoryTest` | Tests of the two deprecated types. | Logger interface/formatting behaviour stays covered by `AbstractInternalLoggerTest`, `InternalLoggerFactoryTest` and `JdkLoggerFactoryTest`. |

### common/src/main/java/io/netty/util/ResourceLeakException.java

```java
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

/**
 * @deprecated This class will be removed in the future version.
 */
```

### common/src/main/java/io/netty/util/internal/SuppressJava6Requirement.java

```java
/*
 * Copyright 2018 The Netty Project
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

/**
 * Annotation to suppress the Java 6 source code requirement checks for a method.
 */
```

### common/src/main/java/io/netty/util/internal/logging/CommonsLogger.java

```java
/*
 * Copyright 2012 The Netty Project
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

/**
 * Copyright (c) 2004-2011 QOS.ch
 * All rights reserved.
 *
 * Permission is hereby granted, free  of charge, to any person obtaining
 * a  copy  of this  software  and  associated  documentation files  (the
 * "Software"), to  deal in  the Software without  restriction, including
 * without limitation  the rights to  use, copy, modify,  merge, publish,
 * distribute,  sublicense, and/or sell  copies of  the Software,  and to
 * permit persons to whom the Software  is furnished to do so, subject to
 * the following conditions:
 *
 * The  above  copyright  notice  and  this permission  notice  shall  be
 * included in all copies or substantial portions of the Software.
 *
 * THE  SOFTWARE IS  PROVIDED  "AS  IS", WITHOUT  WARRANTY  OF ANY  KIND,
 * EXPRESS OR  IMPLIED, INCLUDING  BUT NOT LIMITED  TO THE  WARRANTIES OF
 * MERCHANTABILITY,    FITNESS    FOR    A   PARTICULAR    PURPOSE    AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
 * LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
 * OF CONTRACT, TORT OR OTHERWISE,  ARISING FROM, OUT OF OR IN CONNECTION
 * WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 *
 */

/**
 * <a href="https://commons.apache.org/logging/">Apache Commons Logging</a>
 * logger.
 *
 * @deprecated Please use {@link Log4J2Logger} or {@link Log4JLogger} or
 * {@link Slf4JLogger}.
 */

/**
     * Delegates to the {@link Log#isTraceEnabled} method of the underlying
     * {@link Log} instance.
     */

/**
     * Delegates to the {@link Log#trace(Object)} method of the underlying
     * {@link Log} instance.
     *
     * @param msg - the message object to be logged
     */

/**
     * Delegates to the {@link Log#trace(Object)} method of the underlying
     * {@link Log} instance.
     *
     * <p>
     * However, this form avoids superfluous object creation when the logger is disabled
     * for level TRACE.
     * </p>
     *
     * @param format
     *          the format string
     * @param arg
     *          the argument
     */

/**
     * Delegates to the {@link Log#trace(Object)} method of the underlying
     * {@link Log} instance.
     *
     * <p>
     * However, this form avoids superfluous object creation when the logger is disabled
     * for level TRACE.
     * </p>
     *
     * @param format
     *          the format string
     * @param argA
     *          the first argument
     * @param argB
     *          the second argument
     */

/**
     * Delegates to the {@link Log#trace(Object)} method of the underlying
     * {@link Log} instance.
     *
     * <p>
     * However, this form avoids superfluous object creation when the logger is disabled
     * for level TRACE.
     * </p>
     *
     * @param format the format string
     * @param arguments a list of 3 or more arguments
     */

/**
     * Delegates to the {@link Log#trace(Object, Throwable)} method of
     * the underlying {@link Log} instance.
     *
     * @param msg
     *          the message accompanying the exception
     * @param t
     *          the exception (throwable) to log
     */

/**
     * Delegates to the {@link Log#isDebugEnabled} method of the underlying
     * {@link Log} instance.
     */

//

/**
     * Delegates to the {@link Log#debug(Object)} method of the underlying
     * {@link Log} instance.
     *
     * @param msg - the message object to be logged
     */

/**
     * Delegates to the {@link Log#debug(Object)} method of the underlying
     * {@link Log} instance.
     *
     * <p>
     * However, this form avoids superfluous object creation when the logger is disabled
     * for level DEBUG.
     * </p>
     *
     * @param format
     *          the format string
     * @param arg
     *          the argument
     */

/**
     * Delegates to the {@link Log#debug(Object)} method of the underlying
     * {@link Log} instance.
     *
     * <p>
     * However, this form avoids superfluous object creation when the logger is disabled
     * for level DEBUG.
     * </p>
     *
     * @param format
     *          the format string
     * @param argA
     *          the first argument
     * @param argB
     *          the second argument
     */

/**
     * Delegates to the {@link Log#debug(Object)} method of the underlying
     * {@link Log} instance.
     *
     * <p>
     * However, this form avoids superfluous object creation when the logger is disabled
     * for level DEBUG.
     * </p>
     *
     * @param format the format string
     * @param arguments a list of 3 or more arguments
     */

/**
     * Delegates to the {@link Log#debug(Object, Throwable)} method of
     * the underlying {@link Log} instance.
     *
     * @param msg
     *          the message accompanying the exception
     * @param t
     *          the exception (throwable) to log
     */

/**
     * Delegates to the {@link Log#isInfoEnabled} method of the underlying
     * {@link Log} instance.
     */

/**
     * Delegates to the {@link Log#debug(Object)} method of the underlying
     * {@link Log} instance.
     *
     * @param msg - the message object to be logged
     */

/**
     * Delegates to the {@link Log#info(Object)} method of the underlying
     * {@link Log} instance.
     *
     * <p>
     * However, this form avoids superfluous object creation when the logger is disabled
     * for level INFO.
     * </p>
     *
     * @param format
     *          the format string
     * @param arg
     *          the argument
     */

/**
     * Delegates to the {@link Log#info(Object)} method of the underlying
     * {@link Log} instance.
     *
     * <p>
     * However, this form avoids superfluous object creation when the logger is disabled
     * for level INFO.
     * </p>
     *
     * @param format
     *          the format string
     * @param argA
     *          the first argument
     * @param argB
     *          the second argument
     */

/**
     * Delegates to the {@link Log#info(Object)} method of the underlying
     * {@link Log} instance.
     *
     * <p>
     * However, this form avoids superfluous object creation when the logger is disabled
     * for level INFO.
     * </p>
     *
     * @param format the format string
     * @param arguments a list of 3 or more arguments
     */

/**
     * Delegates to the {@link Log#info(Object, Throwable)} method of
     * the underlying {@link Log} instance.
     *
     * @param msg
     *          the message accompanying the exception
     * @param t
     *          the exception (throwable) to log
     */

/**
     * Delegates to the {@link Log#isWarnEnabled} method of the underlying
     * {@link Log} instance.
     */

/**
     * Delegates to the {@link Log#warn(Object)} method of the underlying
     * {@link Log} instance.
     *
     * @param msg - the message object to be logged
     */

/**
     * Delegates to the {@link Log#warn(Object)} method of the underlying
     * {@link Log} instance.
     *
     * <p>
     * However, this form avoids superfluous object creation when the logger is disabled
     * for level WARN.
     * </p>
     *
     * @param format
     *          the format string
     * @param arg
     *          the argument
     */

/**
     * Delegates to the {@link Log#warn(Object)} method of the underlying
     * {@link Log} instance.
     *
     * <p>
     * However, this form avoids superfluous object creation when the logger is disabled
     * for level WARN.
     * </p>
     *
     * @param format
     *          the format string
     * @param argA
     *          the first argument
     * @param argB
     *          the second argument
     */

/**
     * Delegates to the {@link Log#warn(Object)} method of the underlying
     * {@link Log} instance.
     *
     * <p>
     * However, this form avoids superfluous object creation when the logger is disabled
     * for level WARN.
     * </p>
     *
     * @param format the format string
     * @param arguments a list of 3 or more arguments
     */

/**
     * Delegates to the {@link Log#warn(Object, Throwable)} method of
     * the underlying {@link Log} instance.
     *
     * @param msg
     *          the message accompanying the exception
     * @param t
     *          the exception (throwable) to log
     */

/**
     * Delegates to the {@link Log#isErrorEnabled} method of the underlying
     * {@link Log} instance.
     */

/**
     * Delegates to the {@link Log#error(Object)} method of the underlying
     * {@link Log} instance.
     *
     * @param msg - the message object to be logged
     */

/**
     * Delegates to the {@link Log#error(Object)} method of the underlying
     * {@link Log} instance.
     *
     * <p>
     * However, this form avoids superfluous object creation when the logger is disabled
     * for level ERROR.
     * </p>
     *
     * @param format
     *          the format string
     * @param arg
     *          the argument
     */

/**
     * Delegates to the {@link Log#error(Object)} method of the underlying
     * {@link Log} instance.
     *
     * <p>
     * However, this form avoids superfluous object creation when the logger is disabled
     * for level ERROR.
     * </p>
     *
     * @param format
     *          the format string
     * @param argA
     *          the first argument
     * @param argB
     *          the second argument
     */

/**
     * Delegates to the {@link Log#error(Object)} method of the underlying
     * {@link Log} instance.
     *
     * <p>
     * However, this form avoids superfluous object creation when the logger is disabled
     * for level ERROR.
     * </p>
     *
     * @param format the format string
     * @param arguments a list of 3 or more arguments
     */

/**
     * Delegates to the {@link Log#error(Object, Throwable)} method of
     * the underlying {@link Log} instance.
     *
     * @param msg
     *          the message accompanying the exception
     * @param t
     *          the exception (throwable) to log
     */
```

### common/src/main/java/io/netty/util/internal/logging/CommonsLoggerFactory.java

```java
/*
 * Copyright 2012 The Netty Project
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

/**
 * Logger factory which creates an
 * <a href="https://commons.apache.org/logging/">Apache Commons Logging</a>
 * logger.
 *
 * @deprecated Please use {@link Log4J2LoggerFactory} or {@link Log4JLoggerFactory} or
 * {@link Slf4JLoggerFactory}.
 */

/**
     * @deprecated Use {@link #INSTANCE} instead.
     */
```

### common/src/test/java/io/netty/util/internal/logging/CommonsLoggerTest.java

```java
/*
 * Copyright 2012 The Netty Project
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
```

### common/src/test/java/io/netty/util/internal/logging/CommonsLoggerFactoryTest.java

```java
/*
 * Copyright 2012 The Netty Project
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
```

## JVM-only members of ported sources

These members of otherwise ported files have no C# declaration; their comments live here.

### common/src/main/java/io/netty/util/internal/PlatformDependent.java

The `NOOP` cleaner (lines 151 and 168), `freeDirectBuffer` (651), `hasDirectBufferAddress`
(662), `useVarHandleForMultiByteAccess` (697) and `canUnalignedAccess` (709) belong to the
JVM direct-buffer/Cleaner and VarHandle families retired in favour of
`NativeMemoryAllocator`/`NativeMemoryOwner` and `MemoryMarshal`/`BinaryPrimitives`
([common-native-memory.md](common-native-memory.md),
common-clr-design.md#clr-endian-views-and-jvm-access-strategies).

```java
// NOOP

// NOOP

/**
     * Try to deallocate the specified direct {@link ByteBuffer}. Please note this method does nothing if
     * the current platform does not support this operation or the specified buffer is not a direct buffer.
     *
     * @deprecated Use the {@link CleanableDirectBuffer#clean()} from {@link #allocateDirect(int)} instead.
     */

/**
     * Check if it is possible to call {@link #directBufferAddress(ByteBuffer)} on the given buffer.
     * @param buffer The specific buffer instance to check for.
     * @return {@code true} if {@link #directBufferAddress(ByteBuffer)} can be called on the given buffer,
     * otherwise {@code false}.
     */

/**
     * {@code true} if {@code VarHandle} should be used for multi-byte access.
     *
     * The multi-byte access strategy is determined as follows:
     * 1) If the platform supports unaligned access natively, use {@code Unsafe} as the fastest option.
     * 2) Otherwise, if {@code VarHandle} is available, use it as a fallback.
     * 3) Otherwise, fall back to manual byte-by-byte access.
     */

/**
     * {@code true} if multi-byte access at arbitrary offsets is possible, either natively through {@code Unsafe}
     * or via {@code VarHandle} where the JVM handles alignment and byte ordering internally.
     */
```

### common/src/main/java/io/netty/util/internal/logging/InternalLoggerFactory.java

`useSlf4JLoggerFactory`, `useLog4J2LoggerFactory` and `useLog4JLoggerFactory` probe JVM
providers, which are not-applicable; `newDefaultFactory` keeps only the JDK-logging step,
whose CLR replacement is `InternalDefaultLoggerFactory` (TraceSource).

```java
// We catch Exception and not ReflectiveOperationException as we still support java 6

// We catch Exception and not ReflectiveOperationException as we still support java 6

// We catch Exception and not ReflectiveOperationException as we still support java 6
```
