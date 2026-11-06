using JisBleInput.Core;

namespace JisBleInput.App;

internal interface IKeyboardConnection : IReportTransport, IDisposable
{
    event Action? Changed;
    event Action<string>? Status;
    IReadOnlyList<HostChoice> Hosts { get; }
    SendTarget? Target { get; }
    bool? CapsLock { get; }
    bool IsStarted { get; }
    bool UsesLocalBluetooth { get; }
    bool HasConnectedHost { get; }
    string Diagnostic { get; }
    void Select(string? id);
    Task StartAsync(bool newPairing, CancellationToken cancellation);
    Task RefreshHostsAsync();
    Task BeginSendAsync(SendTarget target, CancellationToken cancellation);
    Task EndSendAsync(SendTarget target, CancellationToken cancellation);
    Task WaitForHostsDisconnectedAsync(CancellationToken cancellation);
}

internal sealed class SelectedConnectionTransport(Func<IKeyboardConnection> current) : IReportTransport
{
    public bool IsReady(SendTarget target) => current().IsReady(target);
    public Task WriteAsync(byte[] report, SendTarget target, CancellationToken cancellation) => current().WriteAsync(report, target, cancellation);
}

// Preserve the existing Bluetooth implementation and its notification order.
internal sealed class LocalBleConnection : IKeyboardConnection
{
    private readonly BleKeyboard keyboard = new();
    public event Action? Changed { add => keyboard.Changed += value; remove => keyboard.Changed -= value; }
    public event Action<string>? Status { add => keyboard.Status += value; remove => keyboard.Status -= value; }
    public IReadOnlyList<HostChoice> Hosts => keyboard.Hosts;
    public SendTarget? Target => keyboard.Target;
    public bool? CapsLock => keyboard.CapsLock;
    public bool IsStarted => keyboard.IsStarted;
    public bool UsesLocalBluetooth => true;
    public bool HasConnectedHost => keyboard.HasConnectedHost;
    public string Diagnostic => $"{keyboard.StartupStage} / 状態 {keyboard.LastAdvertisement.State} / BluetoothError {keyboard.LastAdvertisement.ErrorCode?.ToString() ?? "未取得"}";
    public void Select(string? id) => keyboard.Select(id);
    public Task StartAsync(bool newPairing, CancellationToken cancellation) => keyboard.StartAsync(true, cancellation);
    public Task RefreshHostsAsync() => keyboard.RefreshHostsAsync();
    public Task BeginSendAsync(SendTarget target, CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    public Task EndSendAsync(SendTarget target, CancellationToken cancellation) => Task.CompletedTask;
    public Task WaitForHostsDisconnectedAsync(CancellationToken cancellation) => keyboard.WaitForHostsDisconnectedAsync(cancellation);
    public bool IsReady(SendTarget target) => keyboard.IsReady(target);
    public Task WriteAsync(byte[] report, SendTarget target, CancellationToken cancellation) => keyboard.WriteAsync(report, target, cancellation);
    public void Dispose() => keyboard.Dispose();
}
