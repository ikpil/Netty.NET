namespace Netty.NET.Common;

public class SentinelRunnable : IRunnable
{
    private readonly string _name;

    public SentinelRunnable(string name)
    {
        _name = name;
    }

    // no-op 
    public void run()
    {
    }

    public override string ToString()
    {
        return _name;
    }
}