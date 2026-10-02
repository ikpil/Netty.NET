using System;

namespace Netty.NET.Common.Concurrent;

/// <summary>Cumulative work completed, with a nullable total when the transfer length is unknown.</summary>
public readonly record struct TransferProgress
{
    public long Completed { get; }
    public long? Total { get; }

    public TransferProgress(long completed, long? total = null)
    {
        if (completed < 0) throw new ArgumentOutOfRangeException(nameof(completed));
        // total unknown
        if (total < 0) total = null; // normalize
        if (total.HasValue && completed > total.Value) throw new ArgumentOutOfRangeException(nameof(completed));
        Completed = completed;
        Total = total;
    }
}
