namespace JisBleInput.Core;

// Values correspond to the documented Windows GATT advertising states.
public enum AdvertisementState { Created, Stopped, Started, Aborted, StartedWithoutAllAdvertisementData }
public sealed record AdvertisementReport(AdvertisementState State, int? ErrorCode = null)
{
    public bool IsRunning => State is AdvertisementState.Started or AdvertisementState.StartedWithoutAllAdvertisementData;
}

public interface IAdvertisementPublisher
{
    AdvertisementReport Current { get; }
    bool? Discoverable { get; }
    event Action<AdvertisementReport>? Changed;
    void Start(bool discoverable);
    void Stop();
}

public sealed class AdvertisementStartException(string phase, AdvertisementReport last, string reason, Exception? inner = null)
    : Exception($"広告{phase}: {reason}（状態 {last.State}、BluetoothError {last.ErrorCode?.ToString() ?? "未取得"}）", inner)
{
    public string Phase { get; } = phase;
    public AdvertisementReport LastReport { get; } = last;
}

public static class AdvertisementStartup
{
    public static async Task<AdvertisementReport> StartAsync(IAdvertisementPublisher publisher, bool discoverable,
        TimeSpan phaseTimeout, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        // Preserve a working advertisement instead of issuing duplicate starts.
        if (publisher.Current.IsRunning && publisher.Discoverable == discoverable) return publisher.Current;
        try
        {
            if (publisher.Current.State is not AdvertisementState.Created and not AdvertisementState.Stopped)
                await WaitAsync(publisher, stopping: true, discoverable, phaseTimeout, cancellation);
            return await WaitAsync(publisher, stopping: false, discoverable, phaseTimeout, cancellation);
        }
        catch
        {
            try { publisher.Stop(); } catch { }
            throw;
        }
    }

    private static async Task<AdvertisementReport> WaitAsync(IAdvertisementPublisher publisher, bool stopping,
        bool discoverable, TimeSpan timeout, CancellationToken cancellation)
    {
        var completion = new TaskCompletionSource<AdvertisementReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sync = new object();
        var last = publisher.Current;
        var phase = stopping ? "停止" : "開始";
        void Observe(AdvertisementReport report)
        {
            lock (sync)
            {
                last = report;
                if (stopping)
                {
                    if (report.State is AdvertisementState.Stopped or AdvertisementState.Created) completion.TrySetResult(report);
                }
                // Windows reports a transient Aborted before Started on a healthy
                // start, so only a timeout with Aborted as the last state fails.
                else if (report.IsRunning) completion.TrySetResult(report);
            }
        }
        publisher.Changed += Observe;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (stopping) publisher.Stop();
            else publisher.Start(discoverable);
            // The property also covers a synchronous or missed notification.
            Observe(publisher.Current);
            var waiting = completion.Task.WaitAsync(timeout, cancellation);
            while (!waiting.IsCompleted)
            {
                await Task.WhenAny(waiting, Task.Delay(100, cancellation));
                cancellation.ThrowIfCancellationRequested();
                if (!waiting.IsCompleted) Observe(publisher.Current);
            }
            return await waiting;
        }
        catch (TimeoutException error)
        {
            lock (sync) throw new AdvertisementStartException(phase, last,
                last.State == AdvertisementState.Aborted ? "Windowsが広告を中断しました" : "状態待ちがタイムアウトしました", error);
        }
        finally { publisher.Changed -= Observe; }
    }
}
