namespace Netty.NET.Common.Concurrent;

//@SuppressWarnings("serial")
public sealed class DefaultAttribute<T> : AtomicReference<T>, IAttribute<T>, IDefaultAttribute where T : class
{
    private AtomicReference<DefaultAttributeMap> _attributeMap;
    private readonly AttributeKey<T> _key;

    public DefaultAttribute(DefaultAttributeMap attributeMap, AttributeKey<T> key)
    {
        _attributeMap = new AtomicReference<DefaultAttributeMap>(attributeMap);
        _key = key;
    }

    public AttributeKey<T> key()
    {
        return _key;
    }

    IAttributeKey IDefaultAttribute.key()
    {
        return key();
    }

    public bool isRemoved()
    {
        return _attributeMap.get() == null;
    }

    public T setIfAbsent(T value)
    {
        while (!compareAndSet(null, value))
        {
            T old = get();
            if (old != null)
            {
                return old;
            }
        }

        return null;
    }

    public T getAndRemove()
    {
        DefaultAttributeMap attributeMap = _attributeMap.get();
        bool removed = attributeMap != null && _attributeMap.compareAndSet(attributeMap, null);
        T oldValue = getAndSet(null);
        if (removed)
        {
            attributeMap.removeAttributeIfMatch(_key, this);
        }

        return oldValue;
    }

    public void remove()
    {
        DefaultAttributeMap attributeMap = _attributeMap.get();
        bool removed = attributeMap != null && _attributeMap.compareAndSet(attributeMap, null);
        set(null);
        if (removed)
        {
            attributeMap.removeAttributeIfMatch(_key, this);
        }
    }
}