namespace JisBleInput.Core;

public sealed class SingleInstanceLease : IDisposable
{
    private readonly Mutex mutex;
    public bool IsOwner { get; }

    public SingleInstanceLease(string name)
    {
        mutex = new Mutex(true, name, out var created);
        IsOwner = created;
    }

    public void Dispose()
    {
        if (IsOwner) mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
