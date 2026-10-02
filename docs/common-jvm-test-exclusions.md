# JVM-only test exclusions

Baseline: e66ce34777f9c4a0c57ac74bb97396ca2f54b43c in D:/workspace/netty.

The eight files below were untranslated C# placeholders, excluded from the test
project or containing commented-out assertions. They are removed together with
the Compile Remove rules. Their upstream entries remain not-applicable in the
manifest; removing a placeholder does not establish that a contract was tested.

| Upstream test | Decision and applicable CLR coverage |
| --- | --- |
| JfrEventSafeTest | jdk.jfr.Event, RecordingStream and @Enabled defaults test the JDK recorder. EventSource has different contracts; it is not implemented just to reproduce these recorder tests. Portable diagnostics and lifecycle behavior remain in scope. |
| VirtualThreadCheckTest | Thread.isVirtual, startVirtualThread, MethodHandle and java.lang.Thread subclasses are JVM facilities. CLR Thread is sealed; Task and ThreadPool workers do not have Java virtual-thread identity. |
| NativeLibraryLoaderTest | URLClassLoader, META-INF/native/JAR resources, JNI fixtures and nested UnsatisfiedLinkError suppression test Java loading. NativeLibraryUtilContractTest covers CLR library loading, exports, handle ownership and failures. Java resource de-duplication is not a CLR native-loading requirement. |
| Log4JLoggerFactoryTest, Log4J2LoggerFactoryTest, Log4J2LoggerTest, Slf4JLoggerFactoryTest, Slf4JLoggerTest | These assert Java provider types, Log4j ExtendedLoggerWrapper/levels or SLF4J LocationAwareLogger SPI. TraceSourceLoggerInterfaceTest and LoggingContractTest cover the useful shared logger interface, formatting and filtering with the CLR backend. No Java provider dependency is introduced. |

The temporary PortingBatch.props and its import are also removed. All remaining
C# test sources compile by default. Run dotnet test Netty.NET.sln in Debug and
with -c Release for Release; use --filter to focus execution when needed.
Historical batch verification records retain their original commands.

## Cleanup verification

Default Debug and Release runs on Windows/net10.0 each discover 1301 cases:
1287 passed, zero failed, 14 skipped. The discovered test identities match the
preceding native-completion-termination checkpoint in both configurations.
Evidence: test-selection-cleanup-full-debug.trx and
test-selection-cleanup-full-release.trx in the ignored test TestResults directory.
The eight removed files were not compiled or discovered before cleanup, so these
results do not claim execution of the excluded JVM tests. Existing skip reasons
remain documented in common-test-skips.md.

The default SDK Compile inventory contains 104 C# source files and no source
exclusion rules or batch import. Manifest regeneration retains 271 upstream
entries and finds no missing candidate files. Original comment coverage checks
find zero missing comments in the eight archived entries (14 comments) and the
111 entries whose recorded status is verified.

The latest native-submission-wrapper-cleanup checkpoint reconfirms the deletion:
the default SDK Compile inventory contains 112 C# files, no removed JVM placeholder
and no batch import or source exclusion. Windows/net10.0 default Debug and Release
each discover 1357 cases: 1343 passed, zero failed, 14 unchanged skips. All test
identities match the preceding native unordered scheduling checkpoint. Evidence:
native-submission-wrapper-cleanup-full-debug.trx and
native-submission-wrapper-cleanup-full-release.trx in ignored TestResults.

## Original comment provenance

Each Java comment below is copied from the pinned Git object, in source order.
License headers and repeated comments are preserved for each upstream file.
These are reference comments, not executable tests or claims of CLR equivalence.

### JfrEventSafeTest.java

Upstream: common/src/test/java/io/netty/util/internal/JfrEventSafeTest.java

Original line 1:

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

Original line 34:

```java
// This code should work even on java 8. Other details are tested in JfrEventTest.
```

Original line 43:

```java
// RecordingStream
```

Original line 60:

```java
// RecordingStream
```

### NativeLibraryLoaderTest.java

Upstream: common/src/test/java/io/netty/util/internal/NativeLibraryLoaderTest.java

Original line 1:

```java
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
```

### VirtualThreadCheckTest.java

Upstream: common/src/test/java/io/netty/util/internal/VirtualThreadCheckTest.java

Original line 1:

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

Original line 105:

```java
// For test
```

Original line 109:

```java
// For test
```

### Log4J2LoggerFactoryTest.java

Upstream: common/src/test/java/io/netty/util/internal/logging/Log4J2LoggerFactoryTest.java

Original line 1:

```java
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
```

### Log4J2LoggerTest.java

Upstream: common/src/test/java/io/netty/util/internal/logging/Log4J2LoggerTest.java

Original line 1:

```java
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
```

Original line 29:

```java
/**
 * {@linkplain Log4J2Logger} extends {@linkplain ExtendedLoggerWrapper} implements {@linkplain InternalLogger}.<br>
 * {@linkplain ExtendedLoggerWrapper} is Log4j2 wrapper class to support wrapped loggers,
 * so There is no need to test it's method.<br>
 * We only need to test the netty's {@linkplain InternalLogger} interface method.<br>
 * It's meaning that we only need to test the Override method in the {@linkplain Log4J2Logger}.
 */
```

### Log4JLoggerFactoryTest.java

Upstream: common/src/test/java/io/netty/util/internal/logging/Log4JLoggerFactoryTest.java

Original line 1:

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

### Slf4JLoggerFactoryTest.java

Upstream: common/src/test/java/io/netty/util/internal/logging/Slf4JLoggerFactoryTest.java

Original line 1:

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

### Slf4JLoggerTest.java

Upstream: common/src/test/java/io/netty/util/internal/logging/Slf4JLoggerTest.java

Original line 1:

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
