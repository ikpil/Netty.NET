namespace Netty.NET.Common.Functional;

public class EmptyRunnable : IRunnable
{
    public static readonly IRunnable Shared = new EmptyRunnable();
    
    private EmptyRunnable()
    {
        
    }
    
    public void run()
    {
        // nothing ..
    }

}