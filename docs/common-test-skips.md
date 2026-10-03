# Current full-suite skips

Baseline: `e66ce34777f9c4a0c57ac74bb97396ca2f54b43c` in `D:/workspace/netty`.
Both ascii-transform-final-full-debug.trx and ascii-transform-final-full-release.trx
report 1831 passed, zero failed and the same 14 skipped cases on Windows/net10.0
(1845 discovered cases). The earlier auto-scaling failure is preserved in
encoding-full-release-first.trx; it adds no skip and is not counted as a pass
in that run. A subsequent traced reproduction/repair is recorded in
common-porting.md, with the limitations of that timing evidence explicit.

| Cases | Reason | Source and executed coverage |
| --- | --- | --- |
| 8 | Recycler pooling is unavailable for a FAST_THREAD_LOCAL owner on an ordinary thread without automatic thread-local cleanup. | RecyclerTest.java OwnerType.isPooling()/assumeIsPooling() has the same condition. Four theories each have guarded/unguarded rows: testRecycleDisableDrop, testRecycleAtDifferentThread, testRecycleAtTwoThreadsMulti, testMaxCapacityWithRecycleAtDifferentThread. All eight matching FAST_THREAD_LOCAL rows pass in RecyclerFastThreadLocalTest. Other applicable owner rows also execute. |
| 2 | Ordinary-thread get/set do not invoke the removal callback automatically. | FastThreadLocalTest.java explicitly marks testOnRemoveCalledForNonFastThreadLocalGet/Set @Disabled. The CLR tests retain that disabling; fast-thread-local and wrapped get/set removal cases execute. |
| 1 | Oversized thread-local table expansion is restricted to CI=true. | FastThreadLocalTest.java testInternalThreadLocalMapExpand has @EnabledIfEnvironmentVariable(CI, true) because it deliberately provokes out-of-memory behavior. CiOnlyFact retains the condition. This test did not execute in these local runs. |
| 1 | JVM SecurityManager supplies a ThreadGroup; CLR has no SecurityManager. | DefaultThreadFactoryTest.testDefaultThreadFactoryInheritsThreadGroupFromSecurityManager is skipped. Other creator/explicit group identity and lifetime cases execute. This does not claim JVM security-manager behavior on CLR. |
| 2 | The expected unresolved type arguments result from Java type erasure. CLR constructed generic types retain those arguments. | TypeParameterMatcherTest.testErasure/testUnsolvedParameter retain their original reference scenarios as skipped JVM expectations. TypeParameterMatcherContractTest independently verifies retained constructed arguments, nested/enclosing arguments, arrays, variance, cache identity and failure conditions. |

These counts are individual discovered cases, not source file counts. Eight
JVM-only source test placeholders have been removed, with reasons and original
comments in common-jvm-test-exclusions.md and the manifest. They were never
compiled or discovered and are not additional entries in the 14-case TRX
skip count. Skipped tests are not counted as passing or as having executed.
