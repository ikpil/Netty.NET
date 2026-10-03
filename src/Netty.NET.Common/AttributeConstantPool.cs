namespace Netty.NET.Common;

internal class AttributeConstantPool<T> : ConstantPool<AttributeKey<T>> where T : class
{
    protected override AttributeKey<T> NewConstant(int id, string name)
    {
        return new AttributeKey<T>(id, name);
    }
}