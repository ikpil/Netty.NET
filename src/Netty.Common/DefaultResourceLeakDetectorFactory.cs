namespace Netty.NET.Common;


/**
 * Default implementation that loads custom leak detector via system property
 */
public class DefaultResourceLeakDetectorFactory : ResourceLeakDetectorFactory 
{
    private readonly Constructor<?> obsoleteCustomClassConstructor;
    private readonly Constructor<?> customClassConstructor;

    DefaultResourceLeakDetectorFactory() {
        string customLeakDetector;
        try {
            customLeakDetector = SystemPropertyUtil.get("io.netty.customResourceLeakDetector");
        } catch (Exception cause) {
            logger.error("Could not access System property: io.netty.customResourceLeakDetector", cause);
            customLeakDetector = null;
        }
        if (customLeakDetector == null) {
            obsoleteCustomClassConstructor = customClassConstructor = null;
        } else {
            obsoleteCustomClassConstructor = obsoleteCustomClassConstructor(customLeakDetector);
            customClassConstructor = customClassConstructor(customLeakDetector);
        }
    }

    private static Constructor<?> obsoleteCustomClassConstructor(string customLeakDetector) {
        try {
            final Type detectorClass = Class.forName(customLeakDetector, true,
                    PlatformDependent.getSystemClassLoader());

            if (typeof(ResourceLeakDetector<>).isAssignableFrom(detectorClass)) {
                return detectorClass.getConstructor(typeof(Class), typeof(int), typeof(long));
            } else {
                logger.error("Class {} does not inherit from ResourceLeakDetector.", customLeakDetector);
            }
        } catch (Exception t) {
            logger.error("Could not load custom resource leak detector class provided: {}",
                    customLeakDetector, t);
        }
        return null;
    }

    private static Constructor<?> customClassConstructor(string customLeakDetector) {
        try {
            final Type detectorClass = Class.forName(customLeakDetector, true,
                    PlatformDependent.getSystemClassLoader());

            if (typeof(ResourceLeakDetector).isAssignableFrom(detectorClass)) {
                return detectorClass.getConstructor(typeof(Class), typeof(int));
            } else {
                logger.error("Class {} does not inherit from ResourceLeakDetector.", customLeakDetector);
            }
        } catch (Exception t) {
            logger.error("Could not load custom resource leak detector class provided: {}",
                    customLeakDetector, t);
        }
        return null;
    }

    @SuppressWarnings("deprecation")
    @Override
    public <T> ResourceLeakDetector<T> newResourceLeakDetector(Class<T> resource, int samplingInterval,
                                                               long maxActive) {
        if (obsoleteCustomClassConstructor != null) {
            try {
                @SuppressWarnings("unchecked")
                ResourceLeakDetector<T> leakDetector =
                        (ResourceLeakDetector<T>) obsoleteCustomClassConstructor.newInstance(
                                resource, samplingInterval, maxActive);
                logger.debug("Loaded custom ResourceLeakDetector: {}",
                        obsoleteCustomClassConstructor.getDeclaringClass().getName());
                return leakDetector;
            } catch (Exception t) {
                logger.error(
                        "Could not load custom resource leak detector provided: {} with the given resource: {}",
                        obsoleteCustomClassConstructor.getDeclaringClass().getName(), resource, t);
            }
        }

        ResourceLeakDetector<T> resourceLeakDetector = new ResourceLeakDetector<T>(resource, samplingInterval,
                                                                                   maxActive);
        logger.debug("Loaded default ResourceLeakDetector: {}", resourceLeakDetector);
        return resourceLeakDetector;
    }

    @Override
    public <T> ResourceLeakDetector<T> newResourceLeakDetector(Class<T> resource, int samplingInterval) {
        if (customClassConstructor != null) {
            try {
                @SuppressWarnings("unchecked")
                ResourceLeakDetector<T> leakDetector =
                        (ResourceLeakDetector<T>) customClassConstructor.newInstance(resource, samplingInterval);
                logger.debug("Loaded custom ResourceLeakDetector: {}",
                        customClassConstructor.getDeclaringClass().getName());
                return leakDetector;
            } catch (Exception t) {
                logger.error(
                        "Could not load custom resource leak detector provided: {} with the given resource: {}",
                        customClassConstructor.getDeclaringClass().getName(), resource, t);
            }
        }

        ResourceLeakDetector<T> resourceLeakDetector = new ResourceLeakDetector<T>(resource, samplingInterval);
        logger.debug("Loaded default ResourceLeakDetector: {}", resourceLeakDetector);
        return resourceLeakDetector;
    }
}
