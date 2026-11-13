using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Groups;

namespace Netty.NET.Transport.Tests;

// Supplemental CLR identity/JSON cases; shared with the actual NativeAOT consumer, never counted as source test mappings.
internal static class ChannelIdContractScenarios
{
    internal const int Count = 9;
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    internal static void Run(int scenario)
    {
        switch (scenario)
        {
            case 0:
                var channels = new[] { new EmbeddedChannel(), new EmbeddedChannel(false, false), new EmbeddedChannel(true, true) };
                try
                {
                    foreach (var channel in channels)
                    {
                        Check(ReferenceEquals(EmbeddedChannelId.Instance, channel.Id), "A default constructor generated a distinct ID.");
                        Check(channel.Id.AsShortText() == "embedded" && channel.Id.AsLongText() == "embedded", "Embedded text changed.");
                        Check(channel.Id.GetHashCode() == 0, "Embedded hash changed.");
                    }
                }
                finally { foreach (var channel in channels) { if (!channel.IsRegistered) channel.Register(); channel.FinishAndReleaseAll(); } }
                break;
            case 1:
                IChannelId id = DefaultChannelId.NewInstance();
                var custom = new EmbeddedChannel(id);
                try { Check(ReferenceEquals(id, custom.Id), "An explicit ID was replaced."); }
                finally { custom.FinishAndReleaseAll(); }
                break;
            case 2:
                var first = new EmbeddedChannel(); var second = new EmbeddedChannel();
                var group = new DefaultChannelGroup();
                try
                {
                    Check(group.Add(first), "The first member was rejected.");
                    Check(!group.Add(second), "Equal embedded IDs became separate group members.");
                    Check(group.Count == 1 && ReferenceEquals(first, group.Find(EmbeddedChannelId.Instance)), "Group ID admission changed.");
                }
                finally { first.FinishAndReleaseAll(); second.FinishAndReleaseAll(); }
                break;
            case 3:
                var normalInstance = (EmbeddedChannelId)EmbeddedChannelId.Instance;
                var deserializedInstance = JsonSerializer.Deserialize("\"embedded\"", ChannelIdJsonContext.Default.EmbeddedChannelId);
                Check(!ReferenceEquals(normalInstance, deserializedInstance), "Serialization never materialized a distinct instance.");
                Check(normalInstance.Equals(deserializedInstance) && deserializedInstance.Equals(normalInstance), "Embedded equality changed.");
                Check(normalInstance.GetHashCode() == deserializedInstance.GetHashCode() && normalInstance.CompareTo(deserializedInstance) == 0, "Embedded hash/order changed.");
                var other = DefaultChannelId.NewInstance();
                Check(Math.Sign(normalInstance.CompareTo(other)) == Math.Sign(string.CompareOrdinal("embedded", other.AsLongText())), "Mixed ID ordering changed.");
                Check(!normalInstance.Equals(other), "Different ID implementations became equal.");
                break;
            case 4:
                var a = DefaultChannelId.NewInstance();
                using (var buf = new MemoryStream())
                {
                    JsonSerializer.Serialize(buf, a, ChannelIdJsonContext.Default.DefaultChannelId);
                    Check(buf.CanRead && buf.CanWrite, "Serialization closed the caller stream.");
                    buf.Position = 0;
                    var b = JsonSerializer.Deserialize(buf, ChannelIdJsonContext.Default.DefaultChannelId);
                    Check(!ReferenceEquals(a, b) && a.Equals(b) && b.Equals(a), "Guid identity did not round trip.");
                    Check(a.GetHashCode() == b.GetHashCode() && a.CompareTo(b) == 0 && a.AsShortText() == b.AsShortText(), "Round-trip hash/order/text changed.");
                }
                break;
            case 5:
                foreach (string json in new[] { "0", "{}", "\"bad\"", "\"00000000-0000-0000-0000-000000000000\"" })
                    Reject(() => JsonSerializer.Deserialize(json, ChannelIdJsonContext.Default.DefaultChannelId));
                break;
            case 6:
                foreach (string json in new[] { "0", "{}", "\"other\"" })
                    Reject(() => JsonSerializer.Deserialize(json, ChannelIdJsonContext.Default.EmbeddedChannelId));
                break;
            case 7:
                var ids = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < 1024; i++)
                {
                    var value = DefaultChannelId.NewInstance();
                    string longValue = value.AsLongText();
                    Check(longValue.Length == 32 && Guid.TryParseExact(longValue, "N", out _), "Native Guid format changed.");
                    Check(ids.Add(longValue), "Duplicate Guid identity.");
                }
                break;
            case 8:
                RejectNull(() => new EmbeddedChannel((IChannelId)null).FinishAndReleaseAll());
                RejectNull(() => new EmbeddedChannel(null, null, true, false).FinishAndReleaseAll());
                break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
    }
    private static void RejectNull(Action action)
    {
        try { action(); }
        catch (ArgumentNullException) { return; }
        throw new InvalidOperationException("An explicitly null channelId was accepted.");
    }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (JsonException) { return; }
        throw new InvalidOperationException("An invalid identity payload was accepted.");
    }
}
