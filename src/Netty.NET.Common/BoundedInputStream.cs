using System.IO;

namespace Netty.NET.Common;

/// <summary>
/// Reads at most one byte beyond the bound to detect oversized input, then
/// throws IOException on subsequent reads, matching Netty's BoundedInputStream.
/// </summary>
public sealed class BoundedInputStream : Internal.BoundedStream
{
    public BoundedInputStream(Stream inner, long limit) : base(inner, limit) { }
    public BoundedInputStream(Stream inner) : base(inner) { }
}
