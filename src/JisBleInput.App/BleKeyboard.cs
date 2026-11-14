using JisBleInput.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using Windows.Foundation;

namespace JisBleInput.App;

internal sealed class BleKeyboard : IReportTransport, IAdvertisementPublisher, IDisposable
{
    private readonly object sync = new();
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly Dictionary<string, BluetoothLEDevice> devices = [];
    private readonly Dictionary<string, byte> leds = [];
    private readonly Dictionary<string, byte> modes = [];
    private readonly Dictionary<string, bool> suspended = [];
    private GattServiceProvider? provider;
    private GattLocalCharacteristic? input;
    private GattLocalCharacteristic? bootInput;
    private string? selected;
    private long generation;
    private bool disposed;
    private byte[] lastReport = new byte[8];
    private readonly SemaphoreSlim startGate = new(1, 1);
    private readonly List<Action> unsubscribe = [];
    private bool gattReady;
    private bool? discoverableMode;
    private AdvertisementReport lastAdvertisement = new(AdvertisementState.Created);
    private event Action<AdvertisementReport>? advertisementChanged;
    public string StartupStage { get; private set; } = "未開始";
    public AdvertisementReport LastAdvertisement => ((IAdvertisementPublisher)this).Current;
    public event Action? Changed;
    public event Action<string>? Status;
    public bool IsStarted { get; private set; }

    public IReadOnlyList<HostChoice> Hosts
    {
        get
        {
            lock (sync)
            {
                if (!IsStarted) return [];
                var hosts = new List<HostChoice>(devices.Count);
                foreach (var (id, device) in devices)
                {
                    if (TryGetConnectedHost(id, device) is { } host) hosts.Add(host);
                }
                return hosts;
            }
        }
    }

    private static HostChoice? TryGetConnectedHost(string id, BluetoothLEDevice device)
    {
        string name;
        try
        {
            if (device.ConnectionStatus != BluetoothConnectionStatus.Connected || !device.DeviceInformation.Pairing.IsPaired)
                return null;
            name = device.Name;
        }
        catch (Exception)
        {
            // A WinRT read can fail while a device is disconnecting. Keep the other devices visible.
            return null;
        }
        return new HostChoice(id, string.IsNullOrWhiteSpace(name) ? "接続済みPC" : name);
    }

    public bool HasConnectedHost
    {
        get
        {
            lock (sync)
            {
                try { return devices.Values.Any(device => device.ConnectionStatus == BluetoothConnectionStatus.Connected); }
                catch { return false; }
            }
        }
    }

    public async Task WaitForHostsDisconnectedAsync(CancellationToken cancellation)
    {
        while (HasConnectedHost) await Task.Delay(100, cancellation);
    }

    public SendTarget? Target
    {
        get { lock (sync) return selected is null ? null : new SendTarget(selected, generation); }
    }

    public bool? CapsLock
    {
        get { lock (sync) return selected is not null && leds.TryGetValue(selected, out var value) ? (value & 2) != 0 : null; }
    }

    public void Select(string? id)
    {
        lock (sync)
        {
            if (selected == id) return;
            selected = id;
            generation++;
            lastReport = new byte[8];
        }
        Changed?.Invoke();
    }

    public async Task StartAsync(bool discoverable = true, CancellationToken cancellation = default)
    {
        await startGate.WaitAsync(cancellation);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                if (!gattReady) await InitializeGattAsync(timeout.Token);
                StartupStage = "広告開始";
                var report = await AdvertisementStartup.StartAsync(this, discoverable, TimeSpan.FromSeconds(10), timeout.Token);
                timeout.Token.ThrowIfCancellationRequested();
                lock (sync) IsStarted = report.IsRunning;
                StartupStage = report.State == AdvertisementState.Started ? "広告開始済み" : "広告開始済み（一部データ省略）";
                Changed?.Invoke();
            }
            catch (Exception error)
            {
                timeout.Cancel();
                lock (sync) { IsStarted = false; generation++; lastReport = new byte[8]; }
                // A provider left in a failed advertising state rejects later
                // starts, so a retry always rebuilds the GATT service.
                ResetGatt();
                if (cancellation.IsCancellationRequested) throw new OperationCanceledException(cancellation);
                throw new InvalidOperationException($"{StartupStage}: {error.Message} [原因 {error.GetType().Name}, HResult 0x{error.HResult:X8}]", error);
            }
        }
        finally { startGate.Release(); }
    }

    private async Task InitializeGattAsync(CancellationToken cancellation)
    {
        StartupStage = "Bluetoothアダプター取得";
        var adapter = await BluetoothAdapter.GetDefaultAsync().AsTask(cancellation)
            ?? throw new InvalidOperationException("Bluetoothアダプターがありません。");
        if (!adapter.IsLowEnergySupported || !adapter.IsPeripheralRoleSupported)
            throw new InvalidOperationException("Bluetooth LE Peripheralに対応していません。");
        StartupStage = "HIDサービス作成";
        var created = await GattServiceProvider.CreateAsync(HidKeyboardMap.Uuid(0x1812)).AsTask(cancellation);
        if (created.Error != BluetoothError.Success) throw new InvalidOperationException($"HIDサービス作成: {created.Error}");
        provider = created.ServiceProvider;
        StartupStage = "HID情報作成";
        await ReadableAsync(0x2A4A, [0x11, 0x01, 0x00, 0x02], cancellation);
        StartupStage = "HIDキー定義作成";
        await ReadableAsync(0x2A4B, HidKeyboardMap.Descriptor, cancellation);

        StartupStage = "HID制御作成";
        var control = await CharacteristicAsync(0x2A4C, GattCharacteristicProperties.WriteWithoutResponse, cancellation);
        TrackWrite(control, async (_, args) =>
        {
            using var deferral = args.GetDeferral();
            try
            {
                var request = await args.GetRequestAsync();
                if (request is null) return;
                if (request.Value.Length > 0)
                {
                    var command = FirstByte(request.Value);
                    if (command is 0 or 1)
                    {
                        lock (sync)
                        {
                            var id = args.Session.DeviceId.Id;
                            var next = command == 0;
                            if (suspended.GetValueOrDefault(id) != next && selected == id) generation++;
                            suspended[id] = next;
                        }
                        Changed?.Invoke();
                    }
                }
                if (request.Option == GattWriteOption.WriteWithResponse) request.Respond();
            }
            catch { Status?.Invoke("HID制御要求を処理できませんでした。"); }
        });
        StartupStage = "HIDプロトコル作成";
        var protocol = await CharacteristicAsync(0x2A4E, GattCharacteristicProperties.Read | GattCharacteristicProperties.WriteWithoutResponse, cancellation);
        TrackRead(protocol, async (_, args) =>
        {
            using var deferral = args.GetDeferral();
            try
            {
                var request = await args.GetRequestAsync();
                if (request is not null)
                {
                    byte mode;
                    lock (sync) mode = modes.GetValueOrDefault(args.Session.DeviceId.Id, (byte)1);
                    request.RespondWithValue(Buffer([mode]));
                }
            }
            catch { Status?.Invoke("プロトコル読取要求を処理できませんでした。"); }
        });
        TrackWrite(protocol, async (_, args) =>
        {
            using var deferral = args.GetDeferral();
            try
            {
                var request = await args.GetRequestAsync();
                if (request is null) return;
                var value = FirstByte(request.Value);
                if (value is 0 or 1)
                {
                    lock (sync)
                    {
                        var id = args.Session.DeviceId.Id;
                        if (modes.GetValueOrDefault(id, (byte)1) != value && selected == id) generation++;
                        modes[id] = value;
                    }
                    Changed?.Invoke();
                }
            }
            catch { Status?.Invoke("プロトコル変更を処理できませんでした。"); }
        });

        StartupStage = "入力レポート作成";
        input = await CreateInputAsync(0x2A4D, true, cancellation);
        StartupStage = "Boot入力作成";
        bootInput = await CreateInputAsync(0x2A22, false, cancellation);
        StartupStage = "出力レポート作成";
        await CreateOutputAsync(0x2A4D, true, cancellation);
        StartupStage = "Boot出力作成";
        await CreateOutputAsync(0x2A32, false, cancellation);
        var currentProvider = provider!;
        TypedEventHandler<GattServiceProvider, GattServiceProviderAdvertisementStatusChangedEventArgs> handler = (source, args) =>
        {
            AdvertisementReport report;
            lock (sync)
            {
                if (disposed || !ReferenceEquals(provider, currentProvider)) return;
                // Read the current property so a delayed callback from an older
                // stop/start transition cannot revive an obsolete Started state.
                var state = (AdvertisementState)(int)currentProvider.AdvertisementStatus;
                report = lastAdvertisement = new(state, state == (AdvertisementState)(int)args.Status ? (int)args.Error : null);
                IsStarted = report.IsRunning;
                if (!IsStarted) { generation++; lastReport = new byte[8]; }
            }
            advertisementChanged?.Invoke(report);
            Status?.Invoke($"Bluetooth広告: {report.State} / Error {report.ErrorCode?.ToString() ?? "未取得"}");
            Changed?.Invoke();
        };
        currentProvider.AdvertisementStatusChanged += handler;
        unsubscribe.Add(() => currentProvider.AdvertisementStatusChanged -= handler);
        gattReady = true;
    }

    AdvertisementReport IAdvertisementPublisher.Current
    {
        get
        {
            lock (sync)
            {
                if (provider is null) return new(AdvertisementState.Created);
                var state = (AdvertisementState)(int)provider.AdvertisementStatus;
                return state == lastAdvertisement.State ? lastAdvertisement : new(state);
            }
        }
    }
    bool? IAdvertisementPublisher.Discoverable => discoverableMode;
    event Action<AdvertisementReport>? IAdvertisementPublisher.Changed
    {
        add => advertisementChanged += value;
        remove => advertisementChanged -= value;
    }
    void IAdvertisementPublisher.Start(bool discoverable)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        discoverableMode = discoverable;
        provider!.StartAdvertising(new GattServiceProviderAdvertisingParameters { IsConnectable = true, IsDiscoverable = discoverable });
    }
    void IAdvertisementPublisher.Stop() => provider?.StopAdvertising();

    private void TrackRead(GattLocalCharacteristic characteristic, TypedEventHandler<GattLocalCharacteristic, GattReadRequestedEventArgs> handler)
    {
        characteristic.ReadRequested += handler;
        unsubscribe.Add(() => characteristic.ReadRequested -= handler);
    }
    private void TrackWrite(GattLocalCharacteristic characteristic, TypedEventHandler<GattLocalCharacteristic, GattWriteRequestedEventArgs> handler)
    {
        characteristic.WriteRequested += handler;
        unsubscribe.Add(() => characteristic.WriteRequested -= handler);
    }

    private async Task<GattLocalCharacteristic> CharacteristicAsync(ushort uuid, GattCharacteristicProperties properties, CancellationToken cancellation)
    {
        var result = await provider!.Service.CreateCharacteristicAsync(HidKeyboardMap.Uuid(uuid), new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = properties,
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
            WriteProtectionLevel = GattProtectionLevel.EncryptionRequired
        }).AsTask(cancellation).WaitAsync(TimeSpan.FromSeconds(5), cancellation);
        if (result.Error != BluetoothError.Success) throw new InvalidOperationException($"GATT {uuid:X4}: {result.Error}");
        return result.Characteristic;
    }

    private async Task ReadableAsync(ushort uuid, byte[] value, CancellationToken cancellation)
    {
        var characteristic = await CharacteristicAsync(uuid, GattCharacteristicProperties.Read, cancellation);
        TrackRead(characteristic, async (_, args) =>
        {
            using var deferral = args.GetDeferral();
            try { (await args.GetRequestAsync())?.RespondWithValue(Buffer(value)); }
            catch { Status?.Invoke("HID情報の読取要求を処理できませんでした。"); }
        });
    }

    private async Task<GattLocalCharacteristic> CreateInputAsync(ushort uuid, bool reference, CancellationToken cancellation)
    {
        var characteristic = await CharacteristicAsync(uuid, GattCharacteristicProperties.Read | GattCharacteristicProperties.Notify, cancellation);
        TrackRead(characteristic, async (_, args) =>
        {
            using var deferral = args.GetDeferral();
            try
            {
                var request = await args.GetRequestAsync();
                byte[] report;
                lock (sync) report = args.Session.DeviceId.Id == selected ? lastReport.ToArray() : new byte[8];
                request?.RespondWithValue(Buffer(report));
            }
            catch { Status?.Invoke("キー状態の読取要求を処理できませんでした。"); }
        });
        TypedEventHandler<GattLocalCharacteristic, object> subscription = (_, _) => _ = RefreshHostsAsync();
        characteristic.SubscribedClientsChanged += subscription;
        unsubscribe.Add(() => characteristic.SubscribedClientsChanged -= subscription);
        if (reference) await ReferenceAsync(characteristic, 1, cancellation);
        return characteristic;
    }

    private async Task CreateOutputAsync(ushort uuid, bool reference, CancellationToken cancellation)
    {
        var characteristic = await CharacteristicAsync(uuid, GattCharacteristicProperties.Read
            | GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse, cancellation);
        TrackRead(characteristic, async (_, args) =>
        {
            using var deferral = args.GetDeferral();
            try
            {
                var request = await args.GetRequestAsync();
                byte value;
                lock (sync) value = leds.GetValueOrDefault(args.Session.DeviceId.Id);
                request?.RespondWithValue(Buffer([value]));
            }
            catch { Status?.Invoke("LED状態の読取要求を処理できませんでした。"); }
        });
        TrackWrite(characteristic, async (_, args) =>
        {
            using var deferral = args.GetDeferral();
            try
            {
                var request = await args.GetRequestAsync();
                if (request is null) return;
                if (request.Value.Length > 0)
                {
                    lock (sync) leds[args.Session.DeviceId.Id] = FirstByte(request.Value);
                }
                if (request.Option == GattWriteOption.WriteWithResponse) request.Respond();
                Changed?.Invoke();
            }
            catch { Status?.Invoke("LED状態を取得できませんでした。"); }
        });
        if (reference) await ReferenceAsync(characteristic, 2, cancellation);
    }

    private static async Task ReferenceAsync(GattLocalCharacteristic characteristic, byte type, CancellationToken cancellation)
    {
        var result = await characteristic.CreateDescriptorAsync(HidKeyboardMap.Uuid(0x2908), new GattLocalDescriptorParameters
        {
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
            StaticValue = Buffer([1, type])
        }).AsTask(cancellation).WaitAsync(TimeSpan.FromSeconds(5), cancellation);
        if (result.Error != BluetoothError.Success) throw new InvalidOperationException($"Report Reference: {result.Error}");
    }

    public async Task RefreshHostsAsync()
    {
        // Subscription callbacks and the UI timer can overlap. One refresh is
        // enough; do not accumulate an unbounded queue while an adapter is slow.
        if (!await refreshGate.WaitAsync(0)) return;
        try
        {
            if (disposed) return;
            var ids = (input?.SubscribedClients ?? []).Concat(bootInput?.SubscribedClients ?? [])
                .Select(client => client.Session.DeviceId.Id).Distinct().ToHashSet();
            foreach (var id in ids)
            {
                lock (sync) { if (devices.ContainsKey(id)) continue; }
                var device = await BluetoothLEDevice.FromIdAsync(id).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                if (device is null) continue;
                lock (sync)
                {
                    if (disposed) { device.Dispose(); return; }
                    devices[id] = device;
                    device.ConnectionStatusChanged += ConnectionChanged;
                }
            }
            lock (sync)
            {
                foreach (var id in devices.Keys.Where(id => !ids.Contains(id)).ToArray()) RemoveDevice(id);
            }
            Changed?.Invoke();
        }
        catch { Status?.Invoke("接続先情報を確認できません。接続先更新を押してください。"); }
        finally { refreshGate.Release(); }
    }

    private void ConnectionChanged(BluetoothLEDevice device, object args)
    {
        lock (sync)
        {
            if (device.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                leds.Remove(device.DeviceId);
                modes.Remove(device.DeviceId);
                suspended.Remove(device.DeviceId);
                if (selected == device.DeviceId) { generation++; lastReport = new byte[8]; }
            }
        }
        Changed?.Invoke();
    }

    private bool Connected(SendTarget target)
    {
        var characteristic = modes.GetValueOrDefault(target.Id, (byte)1) == 0 ? bootInput : input;
        return !disposed && IsStarted && target.Id == selected && target.Generation == generation && !suspended.GetValueOrDefault(target.Id)
            && devices.TryGetValue(target.Id, out var device) && device.ConnectionStatus == BluetoothConnectionStatus.Connected
            && device.DeviceInformation.Pairing.IsPaired && characteristic is not null
            && characteristic.SubscribedClients.Any(client => client.Session.DeviceId.Id == target.Id);
    }

    public bool IsReady(SendTarget target)
    {
        lock (sync)
        {
            try { return Connected(target) && (!leds.TryGetValue(target.Id, out var value) || (value & 2) == 0); }
            catch { return false; }
        }
    }

    public async Task WriteAsync(byte[] report, SendTarget target, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        GattLocalCharacteristic characteristic;
        GattSubscribedClient client;
        lock (sync)
        {
            if (report.Length != 8 || !Connected(target)) throw new InvalidOperationException("送信先が切断・変更されました。");
            if (report.Any(value => value != 0) && leds.TryGetValue(target.Id, out var value) && (value & 2) != 0)
                throw new InvalidOperationException("受信側のCapsLockをオフにしてください。");
            characteristic = modes.GetValueOrDefault(target.Id, (byte)1) == 0 ? bootInput! : input!;
            client = characteristic.SubscribedClients.FirstOrDefault(candidate => candidate.Session.DeviceId.Id == target.Id)
                ?? throw new InvalidOperationException("キー入力の購読がありません。");
            lastReport = report.ToArray();
        }
        var result = await characteristic.NotifyValueAsync(Buffer(report), client).AsTask(cancellation).WaitAsync(TimeSpan.FromSeconds(5), cancellation);
        if (result.Status != GattCommunicationStatus.Success) throw new InvalidOperationException($"キー通知: {result.Status}");
    }

    private static Windows.Storage.Streams.IBuffer Buffer(byte[] bytes)
    {
        using var writer = new DataWriter();
        writer.WriteBytes(bytes);
        return writer.DetachBuffer();
    }

    private static byte FirstByte(Windows.Storage.Streams.IBuffer buffer)
    {
        if (buffer.Length == 0) return 0;
        using var reader = DataReader.FromBuffer(buffer);
        return reader.ReadByte();
    }

    private void RemoveDevice(string id)
    {
        var device = devices[id];
        device.ConnectionStatusChanged -= ConnectionChanged;
        device.Dispose();
        devices.Remove(id);
        leds.Remove(id);
        modes.Remove(id);
        suspended.Remove(id);
        if (selected == id) { generation++; lastReport = new byte[8]; }
    }

    private void ResetGatt()
    {
        var oldProvider = provider;
        lock (sync)
        {
            provider = null;
            input = null;
            bootInput = null;
            gattReady = false;
            discoverableMode = null;
            IsStarted = false;
            selected = null;
            generation++;
            lastReport = new byte[8];
            foreach (var id in devices.Keys.ToArray()) RemoveDevice(id);
        }
        foreach (var detach in unsubscribe.AsEnumerable().Reverse()) { try { detach(); } catch { } }
        unsubscribe.Clear();
        try { oldProvider?.StopAdvertising(); } catch { }
        lastAdvertisement = new(AdvertisementState.Created);
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
        }
        ResetGatt();
    }
}
