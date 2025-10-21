using System;
using System.Reflection;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common;

/**
 * Default implementation that loads custom leak detector via system property
 */
public class DefaultResourceLeakDetectorFactory : ResourceLeakDetectorFactory
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.getInstance(typeof(DefaultResourceLeakDetectorFactory));

    private readonly ConstructorInfo _obsoleteCustomClassConstructor;
    private readonly ConstructorInfo _customClassConstructor;

    public DefaultResourceLeakDetectorFactory()
    {
        string customLeakDetector;
        try
        {
            customLeakDetector = SystemPropertyUtil.get("io.netty.customResourceLeakDetector");
        }
        catch (Exception cause)
        {
            logger.error("Could not access System property: io.netty.customResourceLeakDetector", cause);
            customLeakDetector = null;
        }

        if (customLeakDetector == null)
        {
            _obsoleteCustomClassConstructor = _customClassConstructor = null;
        }
        else
        {
            _obsoleteCustomClassConstructor = obsoleteCustomClassConstructor(customLeakDetector);
            _customClassConstructor = customClassConstructor(customLeakDetector);
        }
    }

    private static ConstructorInfo obsoleteCustomClassConstructor(string customLeakDetector)
    {
        try
        {
            Type detectorClass = Type.GetType(customLeakDetector, true, true);

            if (typeof(ResourceLeakDetector<>).IsAssignableFrom(detectorClass))
            {
                return detectorClass.GetConstructor([typeof(Type), typeof(int), typeof(long)]);
            }
            else
            {
                logger.error("Class {} does not inherit from ResourceLeakDetector.", customLeakDetector);
            }
        }
        catch (Exception t)
        {
            logger.error("Could not load custom resource leak detector class provided: {}",
                customLeakDetector, t);
        }

        return null;
    }

    private static ConstructorInfo customClassConstructor(string customLeakDetector)
    {
        try
        {
            Type detectorClass = Type.GetType(customLeakDetector, true, true);

            if (typeof(ResourceLeakDetector<>).IsAssignableFrom(detectorClass))
            {
                return detectorClass.GetConstructor([typeof(Type), typeof(int)]);
            }
            else
            {
                logger.error("Class {} does not inherit from ResourceLeakDetector.", customLeakDetector);
            }
        }
        catch (Exception t)
        {
            logger.error("Could not load custom resource leak detector class provided: {}",
                customLeakDetector, t);
        }

        return null;
    }

    //@SuppressWarnings("deprecation")
    public override ResourceLeakDetector<T> newResourceLeakDetector<T>(Type resource, int samplingInterval, long maxActive)
    {
        if (_obsoleteCustomClassConstructor != null)
        {
            try
            {
                //@SuppressWarnings("unchecked")
                ResourceLeakDetector<T> leakDetector =
                    (ResourceLeakDetector<T>)_obsoleteCustomClassConstructor.Invoke(
                        [resource, samplingInterval, maxActive]);
                logger.debug($"Loaded custom ResourceLeakDetector: {_obsoleteCustomClassConstructor?.DeclaringType?.Name ?? string.Empty}");
                return leakDetector;
            }
            catch (Exception t)
            {
                logger.error($"Could not load custom resource leak detector provided: {_obsoleteCustomClassConstructor?.DeclaringType?.Name ?? string.Empty} with the given resource: {resource}", t);
            }
        }

        ResourceLeakDetector<T> resourceLeakDetector = new ResourceLeakDetector<T>(resource, samplingInterval,
            maxActive);
        logger.debug("Loaded default ResourceLeakDetector: {}", resourceLeakDetector);
        return resourceLeakDetector;
    }

    public override ResourceLeakDetector<T> newResourceLeakDetector<T>(Type resource, int samplingInterval)
    {
        if (_customClassConstructor != null)
        {
            try
            {
                //@SuppressWarnings("unchecked")
                ResourceLeakDetector<T> leakDetector =
                    (ResourceLeakDetector<T>)_customClassConstructor.Invoke([resource, samplingInterval]);
                logger.debug($"Loaded custom ResourceLeakDetector: {_customClassConstructor?.DeclaringType?.Name ?? string.Empty}");
                return leakDetector;
            }
            catch (Exception t)
            {
                logger.error($"Could not load custom resource leak detector provided: {_customClassConstructor?.DeclaringType?.Name ?? string.Empty} with the given resource: {resource}", t);
            }
        }

        ResourceLeakDetector<T> resourceLeakDetector = new ResourceLeakDetector<T>(resource, samplingInterval);
        logger.debug("Loaded default ResourceLeakDetector: {}", resourceLeakDetector);
        return resourceLeakDetector;
    }
}