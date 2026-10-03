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

    public AttributeKey<T> Key()
    {
        return _key;
    }

    IAttributeKey IDefaultAttribute.Key()
    {
        return Key();
    }

    public bool IsRemoved()
    {
        return _attributeMap.Get() == null;
    }

    public T SetIfAbsent(T value)
    {
        while (!CompareAndSet(null, value))
        {
            T old = Get();
            if (old != null)
            {
                return old;
            }
        }

        return null;
    }

    public T GetAndRemove()
    {
        DefaultAttributeMap attributeMap = _attributeMap.Get();
        bool removed = attributeMap != null && _attributeMap.CompareAndSet(attributeMap, null);
        T oldValue = GetAndSet(null);
        if (removed)
        {
            attributeMap.RemoveAttributeIfMatch(_key, this);
        }

        return oldValue;
    }

    public void Remove()
    {
        DefaultAttributeMap attributeMap = _attributeMap.Get();
        bool removed = attributeMap != null && _attributeMap.CompareAndSet(attributeMap, null);
        Set(null);
        if (removed)
        {
            attributeMap.RemoveAttributeIfMatch(_key, this);
        }
    }
}
