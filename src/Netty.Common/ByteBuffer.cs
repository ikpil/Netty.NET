namespace Netty.NET.Common;

public class ByteBuffer
{
    public int capacity()
    {
        return -1;
    }

    public bool isDirect()
    {
        return false;
    }

    public void position(int pos)
    {
    }

    public ByteBuffer slice()
    {
        return new ByteBuffer();
    }
}