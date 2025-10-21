using System;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common;

internal class AvailableProcessorsHolder
{
    private readonly object _lock = new object();
    private int _availableProcessors;

    /**
     * Set the number of available processors.
     *
     * @param availableProcessors the number of available processors
     * @throws ArgumentException if the specified number of available processors is non-positive
     * @throws InvalidOperationException    if the number of available processors is already configured
     */
    public void setAvailableProcessors(int availableProcessors)
    {
        lock (_lock)
        {
            ObjectUtil.checkPositive(availableProcessors, "availableProcessors");
            if (_availableProcessors != 0)
            {
                string message = $"availableProcessors is already set to [{_availableProcessors}], rejecting [{availableProcessors}]";
                throw new InvalidOperationException(message);
            }

            _availableProcessors = availableProcessors;
        }
    }

    /**
     * Get the configured number of available processors. The default is {@link Runtime#availableProcessors()}.
     * This can be overridden by setting the system property "io.netty.availableProcessors" or by invoking
     * {@link #setAvailableProcessors(int)} before any calls to this method.
     *
     * @return the configured number of available processors
     */
    [SuppressForbidden("to obtain default number of available processors")]
    public int availableProcessors()
    {
        lock (_lock)
        {
            if (_availableProcessors == 0)
            {
                int availableProcessors = SystemPropertyUtil.getInt("io.netty.availableProcessors", Environment.ProcessorCount);
                setAvailableProcessors(availableProcessors);
            }

            return _availableProcessors;
        }
    }
}