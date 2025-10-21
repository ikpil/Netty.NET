namespace Netty.NET.Common;

/**
 * Represents the level of resource leak detection.
*/
public enum ResourceLeakDetectorLevel
{
    /**
     * Disables resource leak detection.
     */
    DISABLED,

    /**
     * Enables simplistic sampling resource leak detection which reports there is a leak or not,
     * at the cost of small overhead (default).
     */
    SIMPLE,

    /**
     * Enables advanced sampling resource leak detection which reports where the leaked object was accessed
     * recently at the cost of high overhead.
     */
    ADVANCED,

    /**
     * Enables paranoid resource leak detection which reports where the leaked object was accessed recently,
     * at the cost of the highest possible overhead (for testing purposes only).
     */
    PARANOID,
}