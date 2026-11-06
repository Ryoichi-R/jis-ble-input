using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using JisBleInput.Core;

namespace JisBleInput.App;

internal sealed class MainWindow : Window
{
    private IKeyboardConnection bluetooth = new LocalBleConnection();
    private readonly StagedSender sender;
    private readonly InputSettings inputSettings = new();
    private readonly ConnectionPreferencesStore connectionStore;
    private readonly InputPreferencesStore inputStore;
    private readonly TransportPreferencesStore transportStore;
    private TransportPreferences transportPreferences = new();
    private readonly bool useConnectionHooks;
    private CancellationTokenSource? transferCancellation;
    private readonly ComboBox connectionMethod = new() { Name = "ConnectionMethod", ItemsSource = new[] { "PC内蔵Bluetooth", "XIAO（USB接続）" }, MinHeight = 30 };
    private readonly ComboBox usbPorts = new() { Name = "XiaoPorts", DisplayMemberPath = "Display", MinHeight = 30 };
    private readonly Button clearXiaoBond = Button("基板の登録を解除");
    private bool updatingTransport;
    private bool preparingTransfer;
    private ConnectionPreferences connectionPreferences = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly TextBox editor = new()
    {
        AcceptsReturn = true,
        AcceptsTab = false,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Name = "Editor",
        FontSize = 16,
        Padding = new Thickness(8),
        MinHeight = 80,
        Text = "日本語ABC橋。"
    };
    private readonly ComboBox hosts = new() { Name = "Hosts", MinWidth = 0, MinHeight = 30, DisplayMemberPath = "Display", SelectedValuePath = "Id", ToolTip = "送信先PC" };
    private readonly ComboBox inputMode = new()
    {
        ItemsSource = new[] { "日本語・混在文", "英数字・URL" },
        SelectedIndex = 0,
        Name = "InputMode",
        MinWidth = 0,
        MinHeight = 30,
        ToolTip = "入力方法",
        Margin = new Thickness(0, 0, 8, 0)
    };
    private readonly ComboBox inputSpeed = new() { Name = "InputSpeed", MinWidth = 0, MinHeight = 30, ToolTip = "入力速度" };
    private readonly ComboBox receiverLayout = new()
    {
        ItemsSource = new[] { "日本語 JIS（106/109）", "英語 US（101/104）" },
        SelectedIndex = 0,
        Name = "ReceiverLayout",
        MinWidth = 0,
        MinHeight = 30
    };
    private readonly TextBlock modeHelp = Label("");
    private readonly Button start = Button("PCを追加");
    private readonly Button retryBluetooth = Button("再接続");
    private readonly CheckBox autoReconnect = new() { Content = "起動時に接続待ちを開始する", Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button refresh = Button("接続先を更新");
    private readonly Button prepareIme = Button("受信側のIMEを準備");
    private readonly Button send = Button("送信");
    private readonly Button pause = Button("一時停止");
    private readonly Button resume = Button("続きから送信");
    private readonly Button stop = Button("停止");
    private readonly Button settings = Button("設定");
    private readonly TextBlock connectionStatus = Named(Label("未接続"), "ConnectionStatus");
    private readonly TextBlock hostPlaceholder = new()
    {
        Name = "HostPlaceholder",
        Text = "送信先PC",
        IsHitTestVisible = false,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 0, 24, 0),
        Foreground = Brushes.DimGray
    };
    private readonly TextBlock connection = Label("未公開。受信PCのBluetooth設定で、このSurfaceとペアリングします。");
    private readonly TextBlock startupDetails = Label("接続診断: 未開始");
    private readonly TextBlock message = Label("");
    private readonly TextBlock progress = Label("キー送信はまだ行っていません。");
    private readonly TextBlock caps = Label("接続先を選択してください。");
    private readonly TextBlock draftInfo = Label("");
    private readonly ProgressBar progressBar = new() { Minimum = 0, Maximum = 100, Height = 8, Margin = new Thickness(0, 8, 0, 0) };
    private readonly DispatcherTimer statusTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private int refreshTicks;
    private SendTarget? activeTarget;
    private Task? operation;
    private bool working;
    private bool starting;
    private bool composition;
    private bool updatingHosts;
    private bool updatingSettings;
    private bool attemptedStart;
    private bool closing;
    private bool allowClose;
    private bool exitRequested;
    private readonly TrayIcon tray;
    private Window? settingsWindow;
    private StackPanel settingsContent = null!;
    internal bool TrayIconVisible => tray.IsVisible;

    public MainWindow(bool autoStartBluetooth = true, string? preferencesDirectory = null,
        IReportTransport? reportTransport = null, IKeyboardConnection? keyboardConnection = null)
    {
        if (reportTransport is not null && autoStartBluetooth)
            throw new ArgumentException("代替transportを使う場合はBluetoothの自動開始を無効にしてください。");
        var preferencesRoot = preferencesDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JisBleInput");
        transportStore = new TransportPreferencesStore(Path.Combine(preferencesRoot, "transport-preferences.json"));
        try { transportPreferences = transportStore.Load(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { message.Text = "送信方式の設定を読み込めませんでした。方式を選び直してください。"; }
        bluetooth.Dispose();
        bluetooth = keyboardConnection ?? (transportPreferences.Method == ConnectionMethod.XiaoUsb ? new XiaoSerialKeyboard() : new LocalBleConnection());
        useConnectionHooks = reportTransport is null;
        sender = new StagedSender(reportTransport ?? new SelectedConnectionTransport(() => bluetooth));
        connectionMethod.SelectedIndex = bluetooth.UsesLocalBluetooth ? 0 : 1;
        connectionStore = new ConnectionPreferencesStore(Path.Combine(preferencesRoot, "connection-preferences.json"));
        try { connectionPreferences = connectionStore.Load(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            connectionPreferences = new(AutoReconnect: false);
            message.Text = "前回の接続設定を読み込めませんでした。接続先を選び直してください。";
        }
        autoReconnect.IsChecked = connectionPreferences.AutoReconnect;
        inputStore = new InputPreferencesStore(Path.Combine(preferencesRoot, "input-preferences.json"));
        try { inputSettings.Restore(inputStore.Load()); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            message.Text = "前回の入力方法・速度を読み込めませんでした。初期設定で開きます。";
        }
        Title = $"JIS BLE Input {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}";
        Icon = AppIcon.CreateWindowIcon();
        Width = 440;
        Height = 480;
        MinWidth = 360;
        MinHeight = 380;
        FontFamily = new System.Windows.Media.FontFamily("Yu Gothic UI");
        FontSize = 13;
        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var top = new StackPanel();
        var targetRow = new Grid();
        targetRow.ColumnDefinitions.Add(new ColumnDefinition());
        targetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var hostBox = new Grid();
        hostBox.Children.Add(hosts);
        hostBox.Children.Add(hostPlaceholder);
        targetRow.Children.Add(hostBox);
        settings.Name = "SettingsButton";
        settings.Margin = new Thickness(8, 0, 0, 0);
        settings.ToolTip = "接続・IME準備・診断";
        Grid.SetColumn(settings, 1);
        targetRow.Children.Add(settings);
        top.Children.Add(targetRow);
        var modeRow = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        modeRow.ColumnDefinitions.Add(new ColumnDefinition());
        modeRow.ColumnDefinitions.Add(new ColumnDefinition());
        modeRow.Children.Add(inputMode);
        Grid.SetColumn(inputSpeed, 1);
        modeRow.Children.Add(inputSpeed);
        top.Children.Add(modeRow);
        root.Children.Add(top);
        Grid.SetRow(editor, 1);
        editor.Margin = new Thickness(0, 10, 0, 8);
        root.Children.Add(editor);
        var bottom = new StackPanel();
        Grid.SetRow(bottom, 2);
        var counts = new DockPanel();
        draftInfo.Name = "DraftInfo";
        draftInfo.FontSize = 11;
        draftInfo.Margin = new Thickness(0);
        draftInfo.Foreground = Brushes.DimGray;
        DockPanel.SetDock(draftInfo, Dock.Right);
        counts.Children.Add(draftInfo);
        connectionStatus.Margin = new Thickness(0);
        connectionStatus.FontSize = 11;
        counts.Children.Add(connectionStatus);
        bottom.Children.Add(counts);
        var buttons = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        send.Name = "SendButton";
        resume.Name = "ResumeButton";
        pause.Name = "PauseButton";
        stop.Name = "StopButton";
        send.ToolTip = "文章全体を送信（Ctrl+Enter）";
        resume.ToolTip = "続きから送信（Ctrl+Enter）";
        editor.ToolTip = "Enter：改行 / Ctrl+Enter：送信 / Tab：次の項目";
        var primaryStyle = new Style(typeof(Button));
        primaryStyle.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(Color.FromRgb(15, 118, 110))));
        primaryStyle.Setters.Add(new Setter(ForegroundProperty, Brushes.White));
        var disabledPrimary = new Trigger { Property = IsEnabledProperty, Value = false };
        disabledPrimary.Setters.Add(new Setter(ForegroundProperty, Brushes.DimGray));
        primaryStyle.Triggers.Add(disabledPrimary);
        send.Style = resume.Style = primaryStyle;
        send.Margin = resume.Margin = new Thickness(0);
        send.MinHeight = resume.MinHeight = 36;
        pause.Margin = stop.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(pause, 1);
        Grid.SetColumn(stop, 2);
        buttons.Children.Add(send);
        buttons.Children.Add(pause);
        buttons.Children.Add(resume);
        buttons.Children.Add(stop);
        bottom.Children.Add(buttons);
        progressBar.Height = 3;
        progressBar.Margin = new Thickness(0, 6, 0, 0);
        bottom.Children.Add(progressBar);
        progress.FontSize = 11;
        progress.TextWrapping = TextWrapping.NoWrap;
        progress.TextTrimming = TextTrimming.CharacterEllipsis;
        bottom.Children.Add(progress);
        message.Name = "TransferMessage";
        message.FontSize = 12;
        message.MaxHeight = 44;
        message.TextTrimming = TextTrimming.CharacterEllipsis;
        bottom.Children.Add(message);
        root.Children.Add(bottom);
        Content = root;
        System.Windows.Automation.AutomationProperties.SetName(hosts, "送信先PC");
        System.Windows.Automation.AutomationProperties.SetName(inputMode, "入力方法");
        System.Windows.Automation.AutomationProperties.SetName(inputSpeed, "入力速度");
        settingsContent = BuildSettingsContent();
        settings.Click += (_, _) => OpenSettings();
        PreviewKeyDown += async (_, args) =>
        {
            if (args.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                args.Handled = true;
                if (resume.IsEnabled && resume.Visibility == Visibility.Visible)
                    resume.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                else if (send.IsEnabled) await SendAsync(false);
            }
        };

        AttachConnectionEvents();
        sender.Progress += (_, _) => Dispatch(UpdateProgress);
        sender.StateChanged += state => Dispatch(() =>
        {
            if (closing || sender.State != state) return;
            if (state == SendState.Pausing)
                message.Text = "今の文字を確定してから一時停止します。";
            if (state == SendState.Paused)
            {
                message.Text = "受信側の入力欄とIMEを確認して続行してください。";
            }
            if (state == SendState.Sending)
                message.Text = "";
            UpdateProgress();
            UpdateButtons();
        });
        editor.TextChanged += (_, _) => UpdateProgress();
        inputMode.SelectionChanged += (_, _) =>
        {
            if (updatingSettings || inputMode.SelectedIndex < 0) return;
            inputSettings.SetMode(inputMode.SelectedIndex == 1 ? TextInputMode.Ascii : TextInputMode.Japanese);
            SaveInputPreferences();
            UpdateMode();
        };
        inputSpeed.SelectionChanged += (_, _) =>
        {
            if (updatingSettings || inputSpeed.SelectedIndex < 0) return;
            inputSettings.SetSpeed(inputSpeed.SelectedIndex);
            SaveInputPreferences();
            UpdateMode();
        };
        receiverLayout.SelectionChanged += (_, _) =>
        {
            if (updatingSettings || receiverLayout.SelectedIndex < 0) return;
            inputSettings.SetLayout(receiverLayout.SelectedIndex == 1 ? ReceiverKeyboardLayout.EnglishUs : ReceiverKeyboardLayout.JapaneseJis);
            SaveInputPreferences();
            UpdateMode();
        };
        statusTimer.Tick += (_, _) =>
        {
            if (closing) return;
            UpdateProgress();
            if (activeTarget is not null && !bluetooth.IsReady(activeTarget)) UpdateConnection();
            // Keep the 1.6.5 Bluetooth refresh cadence; XIAO polls only between transfers.
            if (++refreshTicks % 6 == 0 && bluetooth.IsStarted)
            {
                if (bluetooth.UsesLocalBluetooth) _ = bluetooth.RefreshHostsAsync();
                else if (!working) _ = RefreshConnectionsAsync();
            }
        };
        TextCompositionManager.AddPreviewTextInputStartHandler(editor, (_, _) => { composition = true; UpdateButtons(); });
        TextCompositionManager.AddPreviewTextInputHandler(editor, (_, args) =>
        {
            composition = false;
            UpdateButtons();
        });
        hosts.SelectionChanged += (_, _) =>
        {
            if (updatingHosts) return;
            bluetooth.Select(hosts.SelectedValue as string);
            if (hosts.SelectedValue is string id && bluetooth.Hosts.Any(host => host.Id == id))
            {
                if (bluetooth.UsesLocalBluetooth)
                {
                    connectionPreferences = connectionPreferences with { PeerHash = ConnectionPreferences.HashPeer(id) };
                    SaveConnectionPreferences();
                }
                else
                {
                    transportPreferences = transportPreferences with { PeerHash = ConnectionPreferences.HashPeer(id) };
                    SaveTransportPreferences();
                }
            }
            UpdateButtons();
        };
        autoReconnect.Checked += (_, _) => SetAutoReconnect(true);
        autoReconnect.Unchecked += (_, _) => SetAutoReconnect(false);
        connectionMethod.SelectionChanged += async (_, _) => { if (!updatingTransport) await ChangeConnectionMethodAsync(); };
        usbPorts.SelectionChanged += (_, _) =>
        {
            if (updatingTransport || bluetooth is not XiaoSerialKeyboard xiao || working || xiao.IsStarted) return;
            var selectedPort = usbPorts.SelectedItem as XiaoUsbPort;
            if (selectedPort is { Connectable: false })
            {
                xiao.Port = null;
                connection.Text = XiaoPortHint(selectedPort);
                return;
            }
            xiao.Port = selectedPort;
            transportPreferences = transportPreferences with { BoardHash = xiao.Port?.Hash };
            SaveTransportPreferences();
        };
        clearXiaoBond.Click += async (_, _) =>
        {
            if (working || bluetooth is not XiaoSerialKeyboard xiao || !xiao.IsStarted) return;
            if (MessageBox.Show(this, "選択中のXIAO基板に保存されたPC登録1件を解除します。相手PCでもこの基板の登録を削除してから再登録してください。", "基板の登録解除", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
            working = true; starting = true; UpdateButtons();
            try { operation = xiao.ClearBondAsync(lifetime.Token); await operation; }
            catch (Exception error) { message.Text = error.Message; }
            finally { working = false; starting = false; operation = null; UpdateConnection(); }
        };
        start.Click += async (_, _) => await StartBluetoothAsync(newPairing: true);
        retryBluetooth.Click += async (_, _) => await StartBluetoothAsync(newPairing: false);
        Loaded += async (_, _) =>
        {
            if (autoStartBluetooth && connectionPreferences.ShouldAutoStart && !attemptedStart && !closing)
            {
                if (!bluetooth.UsesLocalBluetooth) DiscoverXiaoPorts();
                await StartBluetoothAsync(newPairing: false);
            }
        };
        refresh.Click += async (_, _) => await RefreshConnectionsAsync();
        prepareIme.Click += async (_, _) => await SendAsync(true);
        send.Click += async (_, _) => await SendAsync(false);
        pause.Click += (_, _) => { sender.RequestPause(); UpdateButtons(); };
        resume.Click += (_, _) =>
        {
            try { sender.Resume(); }
            catch (InvalidOperationException error) { message.Text = error.Message; }
            UpdateButtons();
        };
        stop.Click += async (_, _) =>
        {
            CancelTransfer();
            if (operation is not null)
            {
                try { await operation; } catch { }
            }
            activeTarget = null;
            // XIAO keeps the DISARM/release result written by SendToAsync; Bluetooth keeps 1.6.5 text.
            message.Text = bluetooth.UsesLocalBluetooth
                ? "停止しました。一部入力・意図しない確定の可能性があります。受信側を手動確認してください。自動削除・再送はしません。"
                : message.Text + " 停止操作を完了しました。受信側を手動確認してください。自動削除・再送はしません。";
            UpdateButtons();
        };
        tray = new TrayIcon(() => Dispatch(RestoreFromTray), () => Dispatch(RequestExit));
        Closing += OnClosing;
        Closed += (_, _) => { statusTimer.Stop(); tray.Dispose(); };
        statusTimer.Start();
        UpdateMode();
        UpdateProgress();
        UpdateButtons();
    }

    private void AttachConnectionEvents()
    {
        var current = bluetooth;
        current.Changed += () => { if (ReferenceEquals(bluetooth, current)) Dispatch(UpdateConnection); };
        current.Status += text => { if (ReferenceEquals(bluetooth, current)) Dispatch(() => connection.Text = text); };
    }

    private void CancelTransfer()
    {
        transferCancellation?.Cancel();
        sender.Cancel();
    }

    private void SaveTransportPreferences()
    {
        try { transportStore.Save(transportPreferences); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { message.Text = "送信方式を保存できませんでした。次回は選び直してください。"; }
    }

    internal static string XiaoPortHint(XiaoUsbPort port) => port.Kind == XiaoPortKind.Bootloader
        ? "XIAOが書込モードです。書込中でなければRESETを1回押して通常起動に戻し、接続先を更新してください。"
        : "対象外のSeeed基板です（通常版・Plus等）。XIAO nRF52840 Senseを接続してください。";

    private void DiscoverXiaoPorts()
    {
        if (bluetooth is not XiaoSerialKeyboard xiao || xiao.IsStarted) return;
        updatingTransport = true;
        try
        {
            var ports = XiaoUsbPort.Discover();
            usbPorts.ItemsSource = ports;
            var remembered = ports.FirstOrDefault(port => port.Connectable && port.Hash == transportPreferences.BoardHash);
            usbPorts.SelectedItem = remembered;
            xiao.Port = remembered;
            if (ports.Count == 0) connection.Text = "XIAO Senseが見つかりません。データ通信対応USBケーブルで接続し、接続先を更新してください。";
            else if (!ports.Any(port => port.Connectable)) connection.Text = XiaoPortHint(ports[0]);
            else if (remembered is null) connection.Text = "USB接続した基板を選択し、「再接続」または「PCを追加」を押してください。";
        }
        catch (Exception error) { message.Text = $"USB一覧を取得できません: {error.Message}"; }
        finally { updatingTransport = false; }
    }

    private async Task RefreshConnectionsAsync()
    {
        try
        {
            if (bluetooth is XiaoSerialKeyboard { IsStarted: false }) DiscoverXiaoPorts();
            else if (bluetooth.IsStarted) await bluetooth.RefreshHostsAsync();
            UpdateConnection();
        }
        catch (Exception error) { message.Text = $"接続状態を取得できません: {error.Message}"; UpdateButtons(); }
    }

    private async Task ChangeConnectionMethodAsync()
    {
        var nextMethod = connectionMethod.SelectedIndex == 1 ? ConnectionMethod.XiaoUsb : ConnectionMethod.LocalBluetooth;
        if (working || closing || (nextMethod == ConnectionMethod.LocalBluetooth) == bluetooth.UsesLocalBluetooth)
        {
            updatingTransport = true; connectionMethod.SelectedIndex = bluetooth.UsesLocalBluetooth ? 0 : 1; updatingTransport = false;
            return;
        }
        working = true; UpdateButtons();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        operation = completed.Task;
        try
        {
            if (bluetooth.UsesLocalBluetooth && bluetooth.HasConnectedHost)
            {
                var old = bluetooth;
                var result = await ExitDisconnect.ReleaseAsync(true, new BluetoothRadioSwitch(), old.WaitForHostsDisconnectedAsync,
                    old.Dispose, TimeSpan.FromSeconds(5));
                if (result is ExitDisconnectResult.RadioRestoreFailed or ExitDisconnectResult.ServiceReleaseFailed)
                    throw new InvalidOperationException("Bluetoothの終了・復帰が未確認です。Windows設定で確認してから再試行してください。");
            }
            bluetooth.Dispose();
            bluetooth = nextMethod == ConnectionMethod.XiaoUsb ? new XiaoSerialKeyboard() : new LocalBleConnection();
            AttachConnectionEvents();
            transportPreferences = transportPreferences with { Method = nextMethod };
            SaveTransportPreferences();
            connection.Text = nextMethod == ConnectionMethod.XiaoUsb ? "XIAOをUSB接続し基板を選択してください。" : "内蔵Bluetooth方式です。再接続を押してください。";
            if (nextMethod == ConnectionMethod.XiaoUsb) DiscoverXiaoPorts();
        }
        catch (Exception error)
        {
            // ExitDisconnect can already have disposed the old GATT service.
            // Recreate a stopped facade so a later retry can recover.
            bluetooth.Dispose();
            bluetooth = transportPreferences.Method == ConnectionMethod.XiaoUsb ? new XiaoSerialKeyboard() : new LocalBleConnection();
            AttachConnectionEvents();
            message.Text = error.Message;
        }
        finally
        {
            updatingTransport = true; connectionMethod.SelectedIndex = bluetooth.UsesLocalBluetooth ? 0 : 1; updatingTransport = false;
            working = false; operation = null; completed.TrySetResult(); UpdateConnection();
        }
    }

    private void SetAutoReconnect(bool enabled)
    {
        connectionPreferences = connectionPreferences with { AutoReconnect = enabled };
        SaveConnectionPreferences();
    }

    private StackPanel BuildSettingsContent()
    {
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = "接続", FontWeight = FontWeights.SemiBold });
        panel.Children.Add(Choice("送信方式", connectionMethod));
        panel.Children.Add(Choice("USB接続したXIAO Sense", usbPorts));
        panel.Children.Add(clearXiaoBond);
        var connectionButtons = new WrapPanel();
        connectionButtons.Children.Add(start);
        connectionButtons.Children.Add(retryBluetooth);
        connectionButtons.Children.Add(refresh);
        panel.Children.Add(connectionButtons);
        panel.Children.Add(autoReconnect);
        panel.Children.Add(connection);
        panel.Children.Add(Choice("受信PCの配列（英数字モード）", receiverLayout));
        prepareIme.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(prepareIme);
        panel.Children.Add(caps);
        var diagnostic = new StackPanel();
        diagnostic.Children.Add(startupDetails);
        diagnostic.Children.Add(modeHelp);
        panel.Children.Add(new Expander { Header = "診断・現在の設定", Content = diagnostic, Margin = new Thickness(0, 12, 0, 0) });
        return panel;
    }

    internal void OpenSettings()
    {
        if (closing) return;
        if (!working && bluetooth is XiaoSerialKeyboard { IsStarted: false }) DiscoverXiaoPorts();
        if (settingsWindow is null)
        {
            settingsWindow = new Window
            {
                Title = "JIS BLE Input — 設定",
                Owner = this,
                Icon = Icon,
                Width = 420,
                Height = 430,
                MinWidth = 360,
                MinHeight = 300,
                ShowInTaskbar = false,
                FontFamily = FontFamily,
                FontSize = FontSize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new ScrollViewer { Content = settingsContent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
            };
            settingsWindow.Closing += (_, args) =>
            {
                if (closing || allowClose) return;
                args.Cancel = true;
                Dispatch(() => { if (!closing) settingsWindow.Hide(); });
            };
            settingsWindow.PreviewKeyDown += (_, args) =>
            {
                if (args.Key != Key.Escape) return;
                args.Handled = true;
                settingsWindow.Hide();
            };
        }
        settingsWindow.Show();
        if (settingsWindow.WindowState == WindowState.Minimized) settingsWindow.WindowState = WindowState.Normal;
        settingsWindow.Activate();
    }

    private void SaveInputPreferences()
    {
        try { inputStore.Save(inputSettings.ToPreferences()); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            message.Text = "入力方法・速度を保存できませんでした。次回は初期設定で開きます。";
        }
    }

    private void SaveConnectionPreferences()
    {
        try { connectionStore.Save(connectionPreferences); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            message.Text = "接続設定を保存できませんでした。次回は接続先を手動で選んでください。";
        }
    }

    private async Task StartBluetoothAsync(bool newPairing)
    {
        if (closing || working) return;
        attemptedStart = true;
        starting = true;
        working = true;
        UpdateButtons();
        connection.Text = bluetooth.UsesLocalBluetooth ? "Bluetoothの接続待ちを準備しています…" : "XIAOとのUSB接続を準備しています…";
        startupDetails.Text = "接続診断: Bluetooth/GATT/広告の開始を確認中";
        try
        {
            // Use the full advertisement that worked before 1.5.0. Hiding the
            // service at startup can also remove its UUID from advertisements.
            operation = bluetooth.StartAsync(newPairing, lifetime.Token);
            await operation;
            startupDetails.Text = $"接続診断: {bluetooth.Diagnostic}";
            connection.Text = !bluetooth.UsesLocalBluetooth
                ? newPairing ? "60秒間PCを登録できます。受信PCでJIS BLE Keyboardを追加してください。" : "XIAOに接続しました。登録済みPCの接続を待っています。"
                : bluetooth.Diagnostic.Contains("StartedWithoutAllAdvertisementData")
                ? "広告要求は開始しましたが、一部の広告データが省略されています。PC接続の成立は接続先一覧で確認してください。"
                : newPairing ? "新しいPCのペアリングを受け付けています。受信PCのBluetooth設定から追加してください。"
                : "広告を開始し、PCからの接続を待っています。前回のPCの接続・購読が戻ると送信先を復帰します。";
            await bluetooth.RefreshHostsAsync();
        }
        catch (OperationCanceledException)
        {
            if (!closing) { connection.Text = "接続の開始を中断しました。再試行できます。"; startupDetails.Text = $"接続診断: {bluetooth.Diagnostic} / 中断"; }
        }
        catch (Exception error)
        {
            if (!closing)
            {
                connection.Text = "接続待ちを開始できませんでした。「再接続」を押せます。";
                startupDetails.Text = $"接続診断: {error.Message}";
                message.Text = "接続できませんでした。設定から再接続してください。";
            }
        }
        finally
        {
            operation = null;
            starting = false;
            working = false;
            UpdateConnection();
        }
    }

    private Task SendAsync(bool prepareOnly)
    {
        var destination = bluetooth.Target;
        return destination is null ? Task.CompletedTask : SendToAsync(destination, prepareOnly);
    }

    internal async Task SendToAsync(SendTarget destination, bool prepareOnly = false)
    {
        if (working || closing) return;
        var options = SelectedOptions();
        TextSnapshot? snapshot;
        try
        {
            options.Validate();
            snapshot = prepareOnly ? null : TextSnapshot.Create(editor.Text, true);
            if (snapshot is not null && options.Mode == TextInputMode.Ascii) AsciiKeyEncoder.ValidateSnapshot(snapshot);
        }
        catch (ArgumentException error) { message.Text = error.Message; UpdateButtons(); return; }
        Task? transfer = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        transferCancellation = cancellation;
        working = true;
        preparingTransfer = true;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        operation = completed.Task;
        UpdateButtons();
        var attemptedBegin = false;
        try
        {
            activeTarget = destination;
            if (useConnectionHooks)
            {
                if (!bluetooth.IsReady(destination)) throw new InvalidOperationException("接続先を確認してください。");
                attemptedBegin = true;
                await bluetooth.BeginSendAsync(destination, cancellation.Token);
            }
            cancellation.Token.ThrowIfCancellationRequested();
            preparingTransfer = false;
            transfer = prepareOnly ? sender.PrepareImeAsync(destination, options, cancellation.Token)
                : sender.SendSnapshotAsync(snapshot!, destination, options, cancellation.Token);
            await transfer;
            message.Text = sender.State switch
            {
                SendState.Ready => options.Mode == TextInputMode.Ascii
                    ? "受信側のIMEオフ（A）を確認してください。"
                    : "受信側が「あ」になったか確認してください。",
                SendState.KeysSent => "キー列を送信しました。受信側で文章を確認してください。",
                _ => "途中停止しました。一部入力・意図しない確定の可能性があります。受信側を確認してください。"
            };
        }
        catch (OperationCanceledException) { message.Text = "途中停止しました。受信側を確認してください。自動再送はしません。"; }
        catch (ArgumentException error) { message.Text = error.Message; }
        catch (Exception error) { message.Text = $"送信を停止しました: {error.Message} 一部入力の可能性があります。受信側を確認してください。"; }
        finally
        {
            if (attemptedBegin)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await bluetooth.EndSendAsync(destination, cleanup.Token); }
                catch { message.Text += " XIAOの停止・解放は未確認です。受信側のキー状態を確認してください。"; }
            }
            // A validation/start failure has no new release result. In
            // particular, do not display the previous transfer's result.
            if (transfer is not null)
            {
                if (sender.ReleaseReportSent == false)
                    message.Text += " キー解放は未確認です。受信側のキーボード等でキー・IME・入力欄を確認してください。";
                else if (sender.State is SendState.Faulted or SendState.StoppedPartial)
                    message.Text += " キー解放の操作は送信済みです。受信側の実状態は未確認です。キー・IME・入力欄を確認してください。";
            }
            working = false;
            preparingTransfer = false;
            transferCancellation = null;
            operation = null;
            activeTarget = null;
            completed.TrySetResult();
            UpdateButtons();
        }
    }

    private SendOptions SelectedOptions() => inputSettings.Capture();

    private void UpdateMode()
    {
        updatingSettings = true;
        try
        {
            inputMode.SelectedIndex = inputSettings.Mode == TextInputMode.Ascii ? 1 : 0;
            inputSpeed.ItemsSource = InputSettings.SpeedLabels(inputSettings.Mode);
            inputSpeed.SelectedIndex = inputSettings.SpeedIndex;
            receiverLayout.SelectedIndex = inputSettings.Layout == ReceiverKeyboardLayout.EnglishUs ? 1 : 0;
        }
        finally { updatingSettings = false; }
        modeHelp.Text = $"現在の設定: {inputSettings.Description}。通信の待ち時間は別途加わります。送信中・一時停止中は設定を固定します。";
        inputSpeed.ToolTip = inputSettings.Description;
        UpdateButtons();
    }

    private void UpdateConnection()
    {
        if (closing) return;
        var selectedId = bluetooth.Target?.Id;
        if (bluetooth.IsStarted && selectedId is null && !working)
        {
            var remembered = (bluetooth.UsesLocalBluetooth ? connectionPreferences
                : new ConnectionPreferences(AutoReconnect: connectionPreferences.AutoReconnect, PeerHash: transportPreferences.PeerHash))
                .FindRememberedPeer(bluetooth.Hosts.Select(host => host.Id));
            if (remembered is not null)
            {
                bluetooth.Select(remembered);
                selectedId = remembered;
                connection.Text = "前回のPCの接続を確認し、送信先を復帰しました。受信側を確認してから送信してください。";
            }
        }
        updatingHosts = true;
        try
        {
            hosts.ItemsSource = bluetooth.Hosts;
            hosts.SelectedValue = selectedId;
            hosts.ToolTip = hosts.SelectedItem is HostChoice host ? host.Description : "送信先PC";
        }
        finally { updatingHosts = false; }
        if (hosts.SelectedItem is null && selectedId is not null) bluetooth.Select(null);
        caps.Text = bluetooth.CapsLock switch { true => "CapsLock: オン（送信不可）", false => "CapsLock: オフ", null => "CapsLock: 未取得（手動確認）" };
        if (activeTarget is not null && !bluetooth.IsReady(activeTarget))
        {
            CancelTransfer();
            message.Text = "接続先またはキー状態が変わりました。途中入力・キー解放は受信側で確認してください。自動再送はしません。";
        }
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var ready = !closing && bluetooth.IsStarted && bluetooth.Target is { } target && bluetooth.IsReady(target);
        settings.IsEnabled = !closing;
        connectionMethod.IsEnabled = !closing && !working;
        usbPorts.IsEnabled = !closing && !working && !bluetooth.IsStarted;
        usbPorts.Visibility = clearXiaoBond.Visibility = bluetooth.UsesLocalBluetooth ? Visibility.Collapsed : Visibility.Visible;
        clearXiaoBond.IsEnabled = !closing && !working && bluetooth.IsStarted;
        start.IsEnabled = !closing && !working;
        retryBluetooth.IsEnabled = !closing && !working;
        autoReconnect.IsEnabled = !closing && !working;
        // A stopped XIAO still needs refresh to rediscover USB ports.
        refresh.IsEnabled = !closing && !working && (bluetooth.IsStarted || !bluetooth.UsesLocalBluetooth);
        hosts.IsEnabled = !closing && !working;
        inputMode.IsEnabled = !closing && !working;
        inputSpeed.IsEnabled = !closing && !working;
        receiverLayout.IsEnabled = !closing && !working && SelectedOptions().Mode == TextInputMode.Ascii;
        prepareIme.IsEnabled = !closing && !working && bluetooth.Target is { } destination && bluetooth.IsReady(destination);
        ApplyTransferControls(sender.State, working, starting, ready);
        if (preparingTransfer) { pause.Visibility = Visibility.Collapsed; send.Content = "送信準備中"; }
        hostPlaceholder.Visibility = hosts.SelectedItem is null ? Visibility.Visible : Visibility.Collapsed;
        // XIAO: a registered PC is connected but not yet chosen (first registration is never auto-selected).
        var needsHostChoice = !starting && bluetooth.IsStarted && !bluetooth.UsesLocalBluetooth && bluetooth.Target is null && bluetooth.Hosts.Count > 0;
        hostPlaceholder.Text = needsHostChoice ? "ここで送信先PCを選択" : "送信先PC";
        connectionStatus.Text = starting ? "準備中" : !bluetooth.IsStarted ? "未接続"
            : needsHostChoice ? "送信先PCを選択してください"
            : bluetooth.Target is null ? "接続待ち" : bluetooth.CapsLock == true ? "CapsLock オン" : ready ? "接続済み" : "接続確認中";
        connectionStatus.Foreground = ready ? new SolidColorBrush(Color.FromRgb(15, 118, 110)) : Brushes.DimGray;
        connectionStatus.ToolTip = $"{connection.Text}\n{startupDetails.Text}";
        message.Visibility = string.IsNullOrWhiteSpace(message.Text) ? Visibility.Collapsed : Visibility.Visible;
        message.ToolTip = message.Text;
    }

    internal void ApplyTransferControls(SendState state, bool isWorking, bool isStarting, bool targetReady)
    {
        var paused = state == SendState.Paused;
        send.IsEnabled = !closing && targetReady && !isWorking && !composition;
        pause.IsEnabled = !closing && isWorking && state == SendState.Sending;
        resume.IsEnabled = !closing && targetReady && isWorking && paused;
        stop.IsEnabled = !closing && !isStarting && isWorking;
        send.Visibility = paused ? Visibility.Collapsed : Visibility.Visible;
        send.Content = isWorking ? "送信中" : "送信";
        resume.Visibility = paused ? Visibility.Visible : Visibility.Collapsed;
        pause.Visibility = state is SendState.Sending or SendState.Pausing ? Visibility.Visible : Visibility.Collapsed;
        stop.Visibility = isWorking && !isStarting ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateProgress()
    {
        var info = sender.ProgressInfo;
        var draft = editor.Text;
        var lineBreaks = draft.Count(c => c == '\r')
            + draft.Where((c, index) => c == '\n' && (index == 0 || draft[index - 1] != '\r')).Count();
        draftInfo.Text = $"{TextSnapshot.CountInputUnits(draft):N0} / 2,000文字 · 改行{lineBreaks:N0}";
        draftInfo.ToolTip = "改行は受信側へEnterとして送ります。";
        if (draft.StartsWith('\r') || draft.StartsWith('\n')) draftInfo.ToolTip += " 先頭改行あり。";
        if (draft.EndsWith('\r') || draft.EndsWith('\n')) draftInfo.ToolTip += " 末尾改行あり。";
        if (working && info.TotalCharacters > 0) draftInfo.ToolTip += $" 送信対象: {info.TotalCharacters:N0}文字（編集は次回分）。";
        progress.Visibility = info.TotalCharacters > 0 || sender.State == SendState.PreparingIme ? Visibility.Visible : Visibility.Collapsed;
        progressBar.Visibility = info.TotalCharacters > 0 ? Visibility.Visible : Visibility.Collapsed;
        message.Visibility = string.IsNullOrWhiteSpace(message.Text) ? Visibility.Collapsed : Visibility.Visible;
        message.ToolTip = message.Text;
        progressBar.Value = info.TotalCharacters == 0 ? 0 : 100.0 * info.ProcessedCharacters / info.TotalCharacters;
        if (info.TotalCharacters == 0)
        {
            progress.Text = sender.State == SendState.PreparingIme ? "受信側のIMEを準備しています。" : "本文のキー列は送信していません。";
            return;
        }
        var remaining = info.EstimatedRemaining is { } estimate ? FormatDuration(estimate) : "計算中";
        var state = sender.State switch
        {
            SendState.Paused => "一時停止中",
            SendState.Pausing => "一時停止準備",
            SendState.StoppedPartial => "停止",
            SendState.Faulted => "停止（エラー）",
            SendState.KeysSent => "キー列送信済み",
            _ => "キー列処理"
        };
        progress.Text = $"{state} {info.ProcessedCharacters:N0}/{info.TotalCharacters:N0} · 残り {remaining}";
        progress.ToolTip = $"処理時間 {FormatDuration(info.ActiveElapsed)}。受信結果は手動で確認してください。";
    }

    private static string FormatDuration(TimeSpan value) => $"{(long)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";

    internal void RestoreFromTray()
    {
        if (closing) return;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    internal void RequestExit()
    {
        if (closing) return;
        settingsWindow?.Hide();
        RestoreFromTray();
        exitRequested = true;
        Close();
    }

    internal void PrepareForSessionEnd()
    {
        // Windows logoff/shutdown must not turn into a hidden resident process.
        allowClose = true;
        if (!closing)
        {
            closing = true;
            lifetime.Cancel();
            CancelTransfer();
        }
        tray.Dispose();
        bluetooth.Dispose();
    }

    internal void DisposeTrayIcon() => tray.Dispose();

    private async void OnClosing(object? source, CancelEventArgs args)
    {
        if (allowClose) return;
        args.Cancel = true;
        if (closing) return;
        if (!exitRequested)
        {
            // WPF forbids changing Visibility inside Closing. Hide only after
            // cancellation has returned to the dispatcher, without stopping BLE.
            Dispatch(() =>
            {
                if (closing) return;
                settingsWindow?.Hide();
                Hide();
            });
            return;
        }
        closing = true;
        lifetime.Cancel();
        CancelTransfer();
        UpdateButtons();
        if (operation is not null)
        {
            try { await operation; } catch { }
        }
        if (!bluetooth.UsesLocalBluetooth)
        {
            bluetooth.Dispose();
            lifetime.Dispose();
            allowClose = true;
            Dispatch(Close);
            return;
        }
        var hostConnected = bluetooth.HasConnectedHost;
        if (hostConnected) connection.Text = "受信PCとの接続を切ってから終了します（SurfaceのBluetoothを数秒オフにします）…";
        var radio = new BluetoothRadioSwitch();
        ExitDisconnectResult result;
        try
        {
            result = await ExitDisconnect.ReleaseAsync(hostConnected, radio, bluetooth.WaitForHostsDisconnectedAsync, () =>
            {
                bluetooth.Dispose();
                // Release native GATT objects before the receiver can reconnect.
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }, TimeSpan.FromSeconds(5));
        }
        catch (Exception)
        {
            MessageBox.Show(this, "Bluetoothサービスの終了を確認できませんでした。WindowsのBluetooth設定と受信PCの接続状態を確認してください。",
                "終了処理の確認", MessageBoxButton.OK, MessageBoxImage.Warning);
            result = ExitDisconnectResult.RadioRestoreFailed;
        }
        if (result == ExitDisconnectResult.RadioRestoreFailed)
        {
            while (MessageBox.Show(this,
                "Bluetoothをオンに戻せたことを確認できません。マウス・キーボード等も切れたままの可能性があります。\n\n「OK」で復帰を再試行します。「キャンセル」で終了し、Windowsの設定 → BluetoothとデバイスからBluetoothをオンにしてください。",
                "Bluetoothの復帰が必要です", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
            {
                try { await radio.TurnOnAsync(); break; }
                catch (Exception) { }
            }
        }
        else if (result == ExitDisconnectResult.ServiceReleaseFailed)
        {
            MessageBox.Show(this, "Bluetoothサービスの終了を確認できませんでした。WindowsのBluetooth設定と受信PCの接続状態を確認してください。",
                "終了処理の確認", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else if (result is ExitDisconnectResult.DisconnectUnconfirmed or ExitDisconnectResult.RadioUnavailable)
        {
            MessageBox.Show(this, "受信PCの切断を確認できないままキーボード機能を終了しました。次回つながらない場合は、受信PCのBluetooth接続を確認してください。",
                "接続終了の確認", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        lifetime.Dispose();
        allowClose = true;
        // A completed operation resumes synchronously inside Closing, where
        // WPF rejects Close(); defer it until the event has returned.
        Dispatch(Close);
    }

    private void Dispatch(Action action)
    {
        if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
    }

    private static Button Button(string text) => new() { Content = text, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 0) };
    private static StackPanel Choice(string label, ComboBox control)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(control);
        return panel;
    }
    private static TextBlock Label(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 0) };

    private static TextBlock Named(TextBlock block, string name) { block.Name = name; return block; }
}
