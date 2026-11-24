# codec-marshalling porting

Netty.NET does not port `netty-codec-marshalling` of Netty 4.2.19.Final (pinned commit
`64cc10f38ea5f5bd7eae48507817c66680d0afdc`): every source and test needs JBoss Marshalling, a Java library without a
BCL counterpart. There is no C# project. The [manifest](codec-marshalling-porting-manifest.json) records every pinned
file of `codec-marshalling/src` (40 files) with the reason it is not ported. Check it with:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-CodecPortingManifest.ps1 -Module codec-marshalling
```

The checker verifies the file inventory and source hashes, that every `library-not-ported` unit imports the recorded
library package (`org.jboss.marshalling`) or serves such a unit through its code (family), that every declared JUnit
test has its own not-ported disposition, and the facts and evidence of the metadata entries.

| Status | Files |
| --- | --- |
| `library-not-ported` | 37 |
| `jvm-only-replaced` | 3 |

The 4 declared original tests (AbstractCompatibleMarshallingDecoderTest 3, AbstractCompatibleMarshallingEncoderTest 1;
the River/Serial fixtures inherit them) are recorded as not ported.

## Owner decision (2026-11-23)

Units that need a third-party Java library without a BCL counterpart on .NET 10 are not ported (the rule set for
codec-compression, see [codec-compression-porting.md](codec-compression-porting.md)). JBoss Marshalling is "an
alternative serialization API" for the JDK serialization API that stays "fully compatible with java.io.Serializable"
([jboss-marshalling](https://github.com/jboss-remoting/jboss-marshalling)): `MarshallerFactory`, `Marshaller` and
`Unmarshaller` write and read Java object graphs with the river and serial protocols. .NET has no such library in the
BCL ([.NET API browser](https://learn.microsoft.com/en-us/dotnet/api/?view=net-10.0)), and the owner chose to record
the whole module in its manifest without a C# project.

| Java package | C# namespace | Original package summary |
| --- | --- | --- |
| `io.netty.handler.codec.marshalling` | none (no project) | Decoder and Encoder which uses JBoss Marshalling (link to jboss.org in the source). |

## Not ported

| Unit | Status | Reason |
| --- | --- | --- |
| `ChannelBufferByteInput`, `ChannelBufferByteOutput`, `LimitingByteInput` | `library-not-ported` | JBoss `ByteInput`/`ByteOutput` adapters over ByteBuf. |
| `MarshallingDecoder`, `MarshallingEncoder`, `CompatibleMarshallingDecoder`, `CompatibleMarshallingEncoder` | `library-not-ported` | Encoders/decoders over JBoss `Marshaller`/`Unmarshaller`. |
| `MarshallerProvider`, `UnmarshallerProvider`, `DefaultMarshallerProvider`, `DefaultUnmarshallerProvider`, `ThreadLocalMarshallerProvider`, `ThreadLocalUnmarshallerProvider`, `ContextBoundUnmarshallerProvider` | `library-not-ported` | Providers of JBoss marshallers. |
| 23 test fixtures (`Abstract*`, `River*`, `Serial*`) | `library-not-ported` | Tests of the units above with the JBoss river and serial protocols; fixtures without a JBoss import serve the fixture they extend (family). |
| `package-info.java` | `jvm-only-replaced` | Package documentation; the summary is in the table above. |
| `module-info.yml` | `jvm-only-replaced` | JPMS module descriptor; there is no assembly to describe. |
| `META-INF/native-image/.../reflect-config.json` | `jvm-only-replaced` | GraalVM reflection metadata of the handlers; NativeAOT reads no such file. |
