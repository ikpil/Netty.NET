namespace Netty.NET.Common;

internal class SignalConstantPool : ConstantPool<Signal>
{
    protected override Signal newConstant(int id, string name)
    {
        return new Signal(id, name);
    }
}