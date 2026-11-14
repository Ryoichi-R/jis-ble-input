using System.IO;
using System.IO.Ports;
using System.Security.Cryptography;
using JisBleInput.Core;

namespace JisBleInput.App;

internal sealed class SerialPortChannel : IXiaoChannel
{
    // Never 1200bps: the Seeed core treats a 1200bps open/close as a bootloader request.
    public const int BaudRate = 115200;
    private readonly SerialPort port;
    public SerialPortChannel(XiaoUsbPort device)
    {
        port = CreatePort(device.PortName);
        try { port.Open(); } catch { port.Dispose(); throw; }
    }
    internal static SerialPort CreatePort(string portName) =>
        new(portName, BaudRate) { DtrEnable = true, Handshake = Handshake.None, ReadTimeout = 250, WriteTimeout = 500 };
    public Task<int> ReadAsync(byte[] buffer, CancellationToken cancellation) => Task.Run(() =>
    {
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            try { return port.Read(buffer, 0, buffer.Length); }
            catch (TimeoutException) { }
        }
    }, cancellation);
    public async Task WriteAsync(byte[] buffer, CancellationToken cancellation)
    {
        // Cancel the actual OS handle as well as the awaiting task.
        using var registration = cancellation.Register(Dispose);
        await Task.Run(() => { cancellation.ThrowIfCancellationRequested(); port.Write(buffer, 0, buffer.Length); }, CancellationToken.None).ConfigureAwait(false);
    }
    public void Dispose() { try { port.Dispose(); } catch { } }
}

internal sealed class XiaoSerialKeyboard : IKeyboardConnection
{
    internal sealed class AlreadyBondedException() : InvalidOperationException(
        "登録済みのPCがあるため追加できません。変更する場合は基板の登録を解除してください。");

    private readonly object sync = new();
    private readonly Func<XiaoUsbPort, IXiaoChannel> open;
    private XiaoClient? client;
    private string? selected;
    private long generation;
    private uint linkGeneration;
    private bool started;
    private bool disposed;
    public XiaoUsbPort? Port { get; set; }
    public event Action? Changed;
    public event Action<string>? Status;
    public bool UsesLocalBluetooth => false;
    public bool IsStarted { get { lock (sync) return started; } }
    public bool HasConnectedHost => client?.Current is { } current && (current.Flags & 1) != 0;
    public bool? CapsLock => client?.Current?.CapsLock;
    public string Diagnostic => client?.Current is { } current ? $"XIAO FW {current.Firmware} / protocol 1 / 接続世代 {client.Generation} / HMACなし" : "XIAO未接続";
    private static string PeerId(XiaoStatus state) => $"xiao:{state.Board}:{state.Peer}";
    public IReadOnlyList<HostChoice> Hosts => IsStarted && client?.Current is { Ready: true } state
        ? [new HostChoice(PeerId(state), "登録済みPC（XIAO）")] : [];
    public SendTarget? Target { get { lock (sync) return selected is null ? null : new(selected, generation); } }
    public XiaoSerialKeyboard(Func<XiaoUsbPort, IXiaoChannel>? open = null) => this.open = open ?? (device => new SerialPortChannel(device));

    public void Select(string? id)
    {
        lock (sync) { if (selected == id) return; selected = id; generation++; }
        Changed?.Invoke();
    }
    public async Task StartAsync(bool newPairing, CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!IsStarted)
        {
            var device = Port ?? throw new InvalidOperationException("USB接続したXIAO Senseを選択してください。");
            if (!device.Connectable) throw new InvalidOperationException("書込モード・対象外の基板には接続しません。通常起動のXIAO Senseを選択してください。");
            client?.Dispose();
            var next = new XiaoClient(open(device));
            client = next;
            linkGeneration = 0;
            next.Changed += () =>
            {
                if (!ReferenceEquals(client, next)) return;
                lock (sync)
                {
                    if (linkGeneration != next.Generation) { generation++; linkGeneration = next.Generation; }
                }
                Changed?.Invoke();
            };
            next.Faulted += () =>
            {
                if (!ReferenceEquals(client, next)) return;
                lock (sync) { started = false; generation++; selected = null; }
                Status?.Invoke("XIAO通信が終了しました。接続と受信側のキー状態を確認してください。自動再送はしません。");
                Changed?.Invoke();
            };
            try
            {
                await next.StartAsync(cancellation);
                var state = next.Current!;
                if (!string.Equals(state.Board, device.Serial, StringComparison.OrdinalIgnoreCase)
                    || !state.FirmwareSupported || !state.DescriptorHash.SequenceEqual(SHA256.HashData(HidKeyboardMap.Descriptor)))
                    throw new InvalidDataException("基板識別・FW版・キー定義が対応していません。送信を開始しません。");
                lock (sync) { started = true; generation++; }
            }
            catch { next.Dispose(); throw; }
        }
        if (newPairing)
        {
            if (client!.Current!.Bonded) throw new AlreadyBondedException();
            await client.RequestAsync(XiaoCommand.PairingOpen, [], client.Generation, cancellation);
        }
        Status?.Invoke(newPairing ? "60秒間PCを登録できます。受信PCでJIS BLE Keyboardを追加してください。" : "XIAOに接続しました。登録済みPCの接続を待っています。");
        Changed?.Invoke();
    }

    public async Task RefreshHostsAsync()
    {
        if (IsStarted && client is { } link) await link.RequestAsync(XiaoCommand.Status, [], link.Generation, CancellationToken.None);
    }
    public bool IsReady(SendTarget target)
    {
        lock (sync) return !disposed && started && target.Id == selected && target.Generation == generation
            && client?.Current is { Ready: true } state && PeerId(state) == selected && state.CapsLock != true && client.Generation == linkGeneration;
    }
    private (XiaoClient Link, uint Generation) Validate(SendTarget target, bool release)
    {
        lock (sync)
        {
            if (disposed || !started || target.Id != selected || target.Generation != generation || client?.Current is not { Ready: true } current
                || PeerId(current) != target.Id || client.Generation != linkGeneration || !release && current.CapsLock == true)
                throw new InvalidOperationException("XIAOの送信先・接続世代・キー状態が変わりました。");
            return (client, linkGeneration);
        }
    }
    public Task BeginSendAsync(SendTarget target, CancellationToken cancellation)
    {
        var link = Validate(target, false);
        return link.Link.RequestAsync(XiaoCommand.Arm, [], link.Generation, cancellation);
    }
    public Task EndSendAsync(SendTarget target, CancellationToken cancellation)
    {
        var link = Validate(target, true);
        return link.Link.RequestAsync(XiaoCommand.Disarm, [], link.Generation, cancellation);
    }
    public Task WriteAsync(byte[] report, SendTarget target, CancellationToken cancellation)
    {
        if (report.Length != 8) throw new ArgumentException("キー状態は8バイトです。");
        var release = report.All(value => value == 0);
        var link = Validate(target, release);
        return link.Link.RequestAsync(release ? XiaoCommand.Release : XiaoCommand.Report, release ? [] : report, link.Generation, cancellation);
    }
    public Task WaitForHostsDisconnectedAsync(CancellationToken cancellation) => Task.CompletedTask;
    public async Task ClearBondAsync(CancellationToken cancellation)
    {
        var link = client ?? throw new InvalidOperationException("XIAOに接続してください。");
        await link.RequestAsync(XiaoCommand.ClearBond, [], link.Generation, cancellation);
        Select(null);
    }
    public void Dispose()
    {
        lock (sync) { disposed = true; started = false; selected = null; generation++; }
        var old = client; client = null; old?.Dispose();
    }
}
