using System.Text.Json.Serialization;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Transport.Tests;

// CLR-only serializer metadata: the original contract concerns the round-tripped ID, not Java's serialization envelope.
[JsonSerializable(typeof(DefaultChannelId))]
[JsonSerializable(typeof(EmbeddedChannelId))]
internal partial class ChannelIdJsonContext : JsonSerializerContext;
