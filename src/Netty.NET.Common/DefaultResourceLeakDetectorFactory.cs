using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common;

/**
 * Default implementation that loads custom leak detector via system property
 */
public class DefaultResourceLeakDetectorFactory : ResourceLeakDetectorFactory
{
    private readonly ConstructorInfo obsoleteCustomClassConstructor;
    private readonly ConstructorInfo customClassConstructor;

    public DefaultResourceLeakDetectorFactory()
    {
        string customLeakDetector;
        try { customLeakDetector = SystemPropertyUtil.get("io.netty.customResourceLeakDetector"); }
        catch (Exception cause)
        {
            logger.error("Could not access System property: io.netty.customResourceLeakDetector", cause);
            customLeakDetector = null;
        }
        if (customLeakDetector == null) return;
        obsoleteCustomClassConstructor = loadConstructor(customLeakDetector, [typeof(Type), typeof(int), typeof(long)]);
        customClassConstructor = loadConstructor(customLeakDetector, [typeof(Type), typeof(int)]);
    }

    private static ConstructorInfo loadConstructor(string name, Type[] parameters)
    {
        try
        {
            Type type = Type.GetType(name, throwOnError: true);
            bool inheritsDetector = false;
            for (Type current = type; current != null; current = current.BaseType)
                if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(ResourceLeakDetector<>))
                {
                    inheritsDetector = true;
                    break;
                }
            if (!inheritsDetector)
            {
                logger.error("Class {} does not inherit from ResourceLeakDetector.", name);
                return null;
            }
            // Java erases T. A CLR provider can be a one-parameter generic definition,
            // closed for each requested T, or an already closed compatible subclass.
            if (type.ContainsGenericParameters && (!type.IsGenericTypeDefinition || type.GetGenericArguments().Length != 1))
                throw new ArgumentException("A custom detector must be closed or have exactly one generic parameter.", nameof(name));
            // Generic definitions have no single initializer; initialize their closed
            // construction when a resource type is requested instead.
            if (!type.ContainsGenericParameters) RuntimeHelpers.RunClassConstructor(type.TypeHandle);
            return type.GetConstructor(parameters) ?? throw new MissingMethodException(type.FullName, ".ctor");
        }
        catch (Exception cause)
        {
            logger.error("Could not load custom resource leak detector class provided: {}", name, cause);
            return null;
        }
    }

    private static ResourceLeakDetector<T> instantiate<T>(ConstructorInfo constructor, object[] arguments) where T : class
    {
        Type type = constructor.DeclaringType;
        if (type.IsGenericTypeDefinition)
        {
            type = type.MakeGenericType(typeof(T));
            Type[] parameters = Array.ConvertAll(constructor.GetParameters(), parameter => parameter.ParameterType);
            constructor = type.GetConstructor(parameters);
        }
        // An erased Java cast may succeed across T. CLR generic classes are invariant;
        // an incompatible closed provider follows the original failure/fallback path.
        if (!typeof(ResourceLeakDetector<T>).IsAssignableFrom(type))
            throw new InvalidCastException("The custom detector is incompatible with resource type " + typeof(T).FullName);
        RuntimeHelpers.RunClassConstructor(type.TypeHandle);
        return (ResourceLeakDetector<T>)constructor.Invoke(arguments);
    }

    [Obsolete]
    public override ResourceLeakDetector<T> newResourceLeakDetector<T>(Type resource, int samplingInterval, long maxActive)
    {
        if (obsoleteCustomClassConstructor != null)
        {
            try
            {
                var detector = instantiate<T>(obsoleteCustomClassConstructor, [resource, samplingInterval, maxActive]);
                logger.debug("Loaded custom ResourceLeakDetector: {}", obsoleteCustomClassConstructor.DeclaringType.FullName);
                return detector;
            }
            catch (Exception cause)
            {
                logger.error("Could not load custom resource leak detector provided: {} with the given resource: {}",
                    obsoleteCustomClassConstructor.DeclaringType.FullName, resource, cause);
            }
        }
        var fallback = new ResourceLeakDetector<T>(resource, samplingInterval, maxActive);
        logger.debug("Loaded default ResourceLeakDetector: {}", fallback);
        return fallback;
    }

    public override ResourceLeakDetector<T> newResourceLeakDetector<T>(Type resource, int samplingInterval)
    {
        if (customClassConstructor != null)
        {
            try
            {
                var detector = instantiate<T>(customClassConstructor, [resource, samplingInterval]);
                logger.debug("Loaded custom ResourceLeakDetector: {}", customClassConstructor.DeclaringType.FullName);
                return detector;
            }
            catch (Exception cause)
            {
                logger.error("Could not load custom resource leak detector provided: {} with the given resource: {}",
                    customClassConstructor.DeclaringType.FullName, resource, cause);
            }
        }
        var fallback = new ResourceLeakDetector<T>(resource, samplingInterval);
        logger.debug("Loaded default ResourceLeakDetector: {}", fallback);
        return fallback;
    }
}
