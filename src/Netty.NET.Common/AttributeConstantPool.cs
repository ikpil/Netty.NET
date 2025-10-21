namespace Netty.NET.Common;

internal class AttributeConstantPool<T> : ConstantPool<AttributeKey<T>> where T : class
{
    protected override AttributeKey<T> newConstant(int id, string name)
    {
        return new AttributeKey<T>(id, name);
    }
}