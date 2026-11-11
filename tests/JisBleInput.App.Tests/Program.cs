using System.IO;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Windows.Input;
using JisBleInput.App;
using JisBleInput.Core;

internal static class UiTestProgram
{
    private static int passed;

    [STAThread]
    public static int Main(string[] args)
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var deadline = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        deadline.Tick += (_, _) => { Console.Error.WriteLine("FAIL: UI test timeout"); application.Shutdown(2); };
        application.Startup += async (_, _) =>
        {
            deadline.Start();
            try
            {
                await VerifyAsync(application, args.FirstOrDefault());
                Console.WriteLine($"Passed: {passed}");
                application.Shutdown(0);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"FAIL: {error}");
                application.Shutdown(1);
            }
            finally { deadline.Stop(); }
        };
        return application.Run();
    }

    private static async Task VerifyAsync(Application application, string? previewPath)
    {
        var hostA = new HostChoice("synthetic-peer-a", "同名PC");
        var hostB = new HostChoice("synthetic-peer-b", "同名PC");
        Check(hostA.Display != hostB.Display && hostA.Display.Contains(hostA.Fingerprint[..8]),
            "same-name computers have distinct compact identifiers");
        Check(hostA.Fingerprint.Length == 64 && hostA.Description.Contains(hostA.Fingerprint)
            && !hostA.Description.Contains(hostA.Id), "destination details expose a stable full hash instead of the raw device ID");
        Check(new HostChoice(hostA.Id, "変更後PC").Fingerprint == hostA.Fingerprint,
            "renaming a computer does not change its presentation identifier");
        await VerifyTransferFailuresAsync();
        VerifyUnsupportedPreferences();
        await VerifyKeepAwakeAsync();
        await VerifyConnectionPreparationAsync();
        await VerifyHostChoiceGuidanceAsync();
        await XiaoConnectionChecks.RunAsync(Check);

        // Exercise the real WPF Closing handler and NotifyIcon without starting
        // a GATT service, sending text or changing the computer's Bluetooth radio.
        var window = MakeWindow();
        application.MainWindow = window;
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        window.Show();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(window.IsVisible && window.TrayIconVisible, "opening the window creates the notification icon");
        Check(window.Width == 440 && window.Height == 480, "the default window is 440 by 480");
        Check(window.Icon is BitmapSource { PixelWidth: 256 }, "the window loads the dedicated high-resolution icon");
        using (var icon = AppIcon.CreateNotificationIcon())
            Check(icon.Width >= 16 && icon.Height >= 16, "the notification icon loads from the dedicated ICO resource");
        if (previewPath is not null) SavePreview(window, previewPath);

        var editor = Find<TextBox>(window, "Editor");
        var send = Find<Button>(window, "SendButton");
        var clear = Find<Button>(window, "ClearDraftButton");
        var paste = Find<Button>(window, "PasteDraftButton");
        var presetChoice = Find<ComboBox>(window, "PresetChoice");
        var loadPreset = Find<Button>(window, "LoadPresetButton");
        var settingsButton = Find<Button>(window, "SettingsButton");
        var pause = Find<Button>(window, "PauseButton");
        var resume = Find<Button>(window, "ResumeButton");
        var stop = Find<Button>(window, "StopButton");
        Check(editor.AcceptsReturn && !editor.AcceptsTab, "Enter retains multiline input while Tab navigates instead of inserting unsupported text");
        Check(clear.IsVisible && clear.IsEnabled && paste.IsVisible && paste.IsEnabled && pause.Visibility == Visibility.Collapsed
            && resume.Visibility == Visibility.Collapsed && stop.Visibility == Visibility.Collapsed,
            "idle mode shows send, clear and paste without transfer controls");
        Check(Find<TextBlock>(window, "HostPlaceholder").Visibility == Visibility.Visible, "an empty connection selector has a visible label");
        Check((string)presetChoice.SelectedItem == "定型文の選択…" && !loadPreset.IsEnabled,
            "the preset picker opens on its prompt and requires a registered selection");
        CheckPresetAlignment(window, settingsButton, loadPreset, clear, paste);

        window.Width = window.MinWidth;
        window.Height = window.MinHeight;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(IsWithinContent(editor, window) && IsWithinContent(send, window) && IsWithinContent(clear, window) && IsWithinContent(paste, window),
            "editor and all idle buttons fit at the minimum size");
        var rowWidth = send.ActualWidth + clear.ActualWidth + paste.ActualWidth;
        Check(clear.TranslatePoint(new Point(0, 0), window).X > send.TranslatePoint(new Point(send.ActualWidth, 0), window).X
            && paste.TranslatePoint(new Point(0, 0), window).X > clear.TranslatePoint(new Point(clear.ActualWidth, 0), window).X
            && send.ActualWidth / rowWidth is >= 0.56 and <= 0.65
            && clear.ActualWidth / rowWidth is >= 0.16 and <= 0.24 && paste.ActualWidth / rowWidth is >= 0.16 and <= 0.24,
            "the idle row orders send, clear and paste with widths of about 3:1:1");
        Check(editor.ActualHeight >= 80 && send.ActualHeight >= 32, "the minimum window retains usable input and button heights");
        CheckPresetAlignment(window, settingsButton, loadPreset, clear, paste);
        if (previewPath is not null) SavePreview(window, Path.ChangeExtension(previewPath, ".minimum.png"));
        window.Width = 440;
        window.Height = 480;

        settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var settings = application.Windows.OfType<Window>().Single(candidate => candidate.Owner == window);
        Check(settings.IsVisible && window.Width == 440 && window.Height == 480, "settings open separately without enlarging the main window");
        if (previewPath is not null) SavePreview(settings, Path.ChangeExtension(previewPath, ".settings.png"));
        var mode = Find<ComboBox>(window, "InputMode");
        var speed = Find<ComboBox>(window, "InputSpeed");
        var layout = Find<ComboBox>(settings, "ReceiverLayout");
        Check(speed.Items.Count == 3 && !layout.IsEnabled, "Japanese mode retains three speeds and disables ASCII-only layout selection");
        mode.SelectedIndex = 1;
        Check(speed.Items.Count == 3 && layout.IsEnabled, "ASCII mode retains three speeds and enables receiver layout selection");
        speed.SelectedIndex = 2;
        mode.SelectedIndex = 0;
        speed.SelectedIndex = 2;
        mode.SelectedIndex = 1;
        Check(speed.SelectedIndex == 2, "mode changes preserve the selected ASCII speed");
        mode.SelectedIndex = 0;
        Check(speed.SelectedIndex == 2, "mode changes preserve the selected Japanese speed");
        const string presetText = "https://example.com/";
        editor.Text = presetText;
        mode.SelectedIndex = 1;
        speed.SelectedIndex = 1;
        var register = window.Dispatcher.InvokeAsync(() =>
        {
            var manager = settings.OwnedWindows.OfType<TextPresetsWindow>().Single();
            try
            {
                Find<Button>(manager, "NewPresetButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(Find<TextBox>(manager, "PresetText").Text == presetText, "new preset takes the current draft");
                Find<TextBox>(manager, "PresetName").Text = "試験用URL";
                Find<Button>(manager, "SavePresetButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(manager.Presets.Single() == new TextPreset("試験用URL", presetText, TextInputMode.Ascii, 1, null),
                    "registration saves text, mode and active transport speed");
            }
            finally { manager.Close(); }
        }, DispatcherPriority.ApplicationIdle);
        Find<Button>(settings, "ManagePresetsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await register.Task;
        Check(presetChoice.Items.Count == 2 && !loadPreset.IsEnabled, "returning from management refreshes the main picker");
        settings.Close();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(!settings.IsVisible && window.IsVisible, "closing settings returns to the same main window");
        editor.Text = "既存の本文";
        mode.SelectedIndex = 0;
        presetChoice.SelectedIndex = 1;
        Check(loadPreset.IsEnabled && !window.TryLoadPreset(() => false) && editor.Text == "既存の本文" && mode.SelectedIndex == 0,
            "canceling replacement retains the existing draft and mode");
        Check(window.TryLoadPreset(() => true) && editor.Text == presetText && mode.SelectedIndex == 1 && speed.SelectedIndex == 1,
            "confirmed load replaces only the local draft and selects the registered mode and speed");
        editor.Clear();
        loadPreset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(editor.Text == presetText && !Find<TextBlock>(window, "TransferProgress").IsVisible,
            "the load button fills an empty draft without a send");
        editor.Text = "A\r\n\r\nB\r\n";
        var draft = Find<TextBlock>(window, "DraftInfo");
        Check(draft.Text.Contains("改行3") && draft.ToolTip?.ToString()?.Contains("末尾改行あり") == true,
            "compact counts preserve the CRLF Enter count and trailing-newline warning");
        clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(editor.Text.Length == 0 && draft.Text == "0 / 5,000文字 · 改行0"
            && !Find<TextBlock>(window, "TransferProgress").IsVisible, "clear only empties the sender draft and refreshes its counts");
        editor.Text = "A?B";
        editor.Select(1, 1);
        ExecutedRoutedEventHandler simulatedPaste = (_, args) =>
        {
            if (args.Command != ApplicationCommands.Paste) return;
            editor.SelectedText = "橋";
            args.Handled = true;
        };
        CommandManager.AddPreviewExecutedHandler(editor, simulatedPaste);
        try { paste.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        finally { CommandManager.RemovePreviewExecutedHandler(editor, simulatedPaste); }
        Check(editor.Text == "A橋B" && !Find<TextBlock>(window, "TransferProgress").IsVisible,
            "paste routes the native paste command to the editor selection without starting a send");

        window.ApplyTransferControls(SendState.Sending, true, false, true);
        Check(pause.IsVisible && pause.IsEnabled && stop.IsVisible && stop.IsEnabled && !send.IsEnabled && !clear.IsEnabled && !paste.IsEnabled,
            "sending exposes pause and stop while send, clear and paste are disabled");
        window.ApplyTransferControls(SendState.Paused, true, false, true);
        Check(resume.IsVisible && resume.IsEnabled && send.Visibility == Visibility.Collapsed && stop.IsEnabled,
            "paused mode makes resume the primary action and retains stop");
        Check(!presetChoice.IsEnabled && !loadPreset.IsEnabled && !window.TryLoadPreset(() => true),
            "paused transfers cannot load a preset");
        window.ApplyTransferControls(SendState.Paused, true, false, false);
        Check(!resume.IsEnabled && stop.IsEnabled, "a lost target prevents resume while retaining stop");
        window.ApplyTransferControls(SendState.Idle, false, false, false);

        window.OpenSettings();
        window.Close();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(!window.IsVisible && !settings.IsVisible, "hiding the main window also hides its settings window");
        window.RestoreFromTray();

        window.Close();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(!window.IsVisible && !closed.Task.IsCompleted, "the window close request hides instead of closing");
        Check(window.TrayIconVisible && !application.Dispatcher.HasShutdownStarted, "the notification icon and dispatcher remain alive while hidden");

        window.RestoreFromTray();
        Check(window.IsVisible, "the hidden window can be shown again");
        window.WindowState = WindowState.Minimized;
        window.RestoreFromTray();
        Check(window.WindowState == WindowState.Normal, "restore also recovers a minimized window");

        window.Close();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        window.RestoreFromTray();
        Check(window.IsVisible && !closed.Task.IsCompleted, "repeated hide and restore reuses the same live window");

        var opened = 0;
        var exited = 0;
        using (var tray = new TrayIcon(() => opened++, () => exited++))
        {
            tray.Menu.Items[0].PerformClick();
            tray.Menu.Items[2].PerformClick();
            Check(opened == 1 && exited == 1, "the actual tray menu items invoke restore and exit separately");
            tray.Dispose();
            tray.Dispose();
            Check(!tray.IsVisible, "notification icon disposal is repeatable and removes visibility");
        }

        window.OpenSettings();
        window.RequestExit();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(!window.TrayIconVisible && !settings.IsVisible, "explicit exit closes both windows and disposes the notification icon");
        window.RestoreFromTray();
        Check(!window.IsVisible, "activation cannot revive a window after shutdown begins");

        var sessionWindow = MakeWindow();
        application.MainWindow = sessionWindow;
        var sessionClosed = false;
        sessionWindow.Closed += (_, _) => sessionClosed = true;
        sessionWindow.Show();
        sessionWindow.Close();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(!sessionWindow.IsVisible && sessionWindow.TrayIconVisible, "the session-end scenario starts from a hidden resident window");
        sessionWindow.PrepareForSessionEnd();
        sessionWindow.PrepareForSessionEnd();
        sessionWindow.Close();
        Check(sessionClosed && !sessionWindow.TrayIconVisible, "session-end cleanup permits real closure instead of hiding");
    }

    private static void CheckPresetAlignment(Window window, Button settings, Button load, Button clear, Button paste)
    {
        double Left(Button button) => button.TranslatePoint(new Point(0, 0), window).X;
        double Right(Button button) => Left(button) + button.ActualWidth;
        Check(IsWithinContent(load, window) && Math.Abs(Left(load) - Left(clear)) < 0.1
            && Math.Abs(Right(load) - Right(paste)) < 0.1, "load spans clear, paste and their gap");
        Check(IsWithinContent(settings, window) && Math.Abs(settings.ActualWidth - paste.ActualWidth) < 0.1
            && Math.Abs(Left(settings) - Left(paste)) < 0.1, "settings matches the paste width and alignment");
    }

    private static MainWindow MakeWindow() => new(autoStartBluetooth: false,
        preferencesDirectory: Path.Combine(Directory.GetCurrentDirectory(), "_internal", "build-cache", "test-data", "jis-ble-ui-preferences", Guid.NewGuid().ToString("N")))
    {
        ShowInTaskbar = false,
        ShowActivated = false,
        Left = -32000,
        Top = -32000
    };

    private static async Task VerifyTransferFailuresAsync()
    {
        var transport = new FailingTransport();
        var window = new MainWindow(autoStartBluetooth: false,
            preferencesDirectory: Path.Combine(Directory.GetCurrentDirectory(), "_internal", "build-cache", "test-data", "jis-ble-ui-preferences", Guid.NewGuid().ToString("N")),
            reportTransport: transport);
        try
        {
            var editor = Find<TextBox>(window, "Editor");
            var message = Find<TextBlock>(window, "TransferMessage");
            var target = new SendTarget("synthetic-target", 1);
            editor.Text = "A";
            await window.SendToAsync(target);
            Check(transport.ReleaseAttempts == 1 && message.Text.Contains("キー解放は未確認")
                && message.Text.Contains("キー・IME・入力欄"),
                "a send exception plus release failure explicitly reports unconfirmed release and manual recovery");

            var previousWrites = transport.Writes;
            editor.Text = "A\tB";
            await window.SendToAsync(target);
            Check(transport.Writes == previousWrites && !message.Text.Contains("解放") && message.Text.Contains("対象外"),
                "whole-text rejection neither sends keys nor reuses the previous transfer's release failure");

            editor.Text = "A";
            transport.FailRelease = false;
            await window.SendToAsync(target);
            Check(message.Text.Contains("キー解放の操作は送信済み") && message.Text.Contains("実状態は未確認")
                && !message.Text.Contains("キー解放は未確認"),
                "a send exception with successful release distinguishes API success from remote key state");

            transport.CancelSend = true;
            transport.FailRelease = true;
            await window.SendToAsync(target);
            Check(message.Text.Contains("途中停止") && message.Text.Contains("キー解放は未確認"),
                "cancellation also retains an explicit release-failure warning");

            transport.WaitForStop = true;
            var stop = Find<Button>(window, "StopButton");
            foreach (var failRelease in new[] { true, false })
            {
                transport.FailRelease = failRelease;
                var sending = window.SendToAsync(target);
                Check(stop.IsEnabled, "a pending local Bluetooth notification can be stopped");
                stop.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                await sending;
                await Dispatcher.Yield(DispatcherPriority.Background);
                Check(message.Text.Contains("途中停止") && (failRelease
                    ? message.Text.Contains("キー解放は未確認") && message.Text.Contains("キー・IME・入力欄")
                    : message.Text.Contains("キー解放の操作は送信済み") && message.Text.Contains("実状態は未確認")),
                    failRelease ? "the local Bluetooth Stop button retains the release failure warning"
                        : "the local Bluetooth Stop button retains the distinction between release sent and remote state");
            }

            previousWrites = transport.Writes;
            transport.Ready = false;
            await window.SendToAsync(target);
            Check(transport.Writes == previousWrites && !message.Text.Contains("解放") && !message.Text.Contains("一部入力")
                && message.Text.Contains("送信を開始できませんでした"),
                "a pre-start connection rejection does not reuse an old release result");
        }
        finally
        {
            window.PrepareForSessionEnd();
            window.Close();
        }
    }

    private static async Task VerifyKeepAwakeAsync()
    {
        var requests = new List<uint>();
        var original = TransferKeepAwake.Apply;
        TransferKeepAwake.Apply = flags => { requests.Add(flags); return 0; };
        var transport = new FailingTransport { FailRelease = false };
        var window = new MainWindow(autoStartBluetooth: false,
            preferencesDirectory: Path.Combine(Directory.GetCurrentDirectory(), "_internal", "build-cache", "test-data", "jis-ble-ui-preferences", Guid.NewGuid().ToString("N")),
            reportTransport: transport);
        try
        {
            var editor = Find<TextBox>(window, "Editor");
            var target = new SendTarget("synthetic-target", 1);
            editor.Text = "A\tB";
            await window.SendToAsync(target);
            Check(requests.Count == 0, "a rejected text never requests keep-awake");
            foreach (var cancel in new[] { false, true })
            {
                requests.Clear();
                transport.CancelSend = cancel;
                editor.Text = "A";
                await window.SendToAsync(target);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Check(requests.Count >= 2 && requests[0] == TransferKeepAwake.KeepAwake
                    && requests[^1] == TransferKeepAwake.Continuous,
                    $"a {(cancel ? "cancelled" : "failed")} transfer requests keep-awake and always clears it");
            }
        }
        finally
        {
            TransferKeepAwake.Apply = original;
            window.PrepareForSessionEnd();
            window.Close();
        }
    }

    // Validation failures are InvalidDataException, which is not an IOException.
    private static void VerifyUnsupportedPreferences()
    {
        var settingsRoot = Path.Combine(Directory.GetCurrentDirectory(), "_internal", "build-cache", "test-data", "jis-ble-ui-preferences", Guid.NewGuid().ToString("N"));
        var cases = new (string File, string Content, string Notice)[]
        {
            ("transport-preferences.json", "{\"Version\":2}", "送信方式の設定を読み込めませんでした"),
            ("transport-preferences.json", "{\"Version\":1,\"Method\":9}", "送信方式の設定を読み込めませんでした"),
            ("transport-preferences.json", new string(' ', 4097), "送信方式の設定を読み込めませんでした"),
            ("connection-preferences.json", "{\"Version\":2}", "前回の接続設定を読み込めませんでした"),
            ("connection-preferences.json", "{\"Version\":1,\"PeerHash\":\"short\"}", "前回の接続設定を読み込めませんでした"),
            ("connection-preferences.json", "null", "前回の接続設定を読み込めませんでした")
        };
        for (var index = 0; index < cases.Length; index++)
        {
            var directory = Path.Combine(settingsRoot, index.ToString());
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, cases[index].File), cases[index].Content);
            var window = new MainWindow(autoStartBluetooth: false, preferencesDirectory: directory);
            try
            {
                Check(Find<TextBlock>(window, "TransferMessage").Text.Contains(cases[index].Notice),
                    $"unsupported {cases[index].File} case {index} opens with defaults and a notice");
            }
            finally
            {
                window.PrepareForSessionEnd();
                window.Close();
            }
        }
    }

    private static async Task VerifyConnectionPreparationAsync()
    {
        var settingsRoot = Path.Combine(Directory.GetCurrentDirectory(), "_internal", "build-cache", "test-data", "jis-ble-input-xiao-ui", Guid.NewGuid().ToString("N"));
        foreach (var stopDuringArm in new[] { true, false })
        {
            var connection = new PreparingConnection();
            var window = new MainWindow(false, Path.Combine(settingsRoot, stopDuringArm.ToString()), keyboardConnection: connection);
            try
            {
                window.Show();
                window.OpenSettings();
                var connectionSettings = window.OwnedWindows.Cast<Window>().Single();
                var editor = Find<TextBox>(window, "Editor");
                Find<ComboBox>(window, "InputMode").SelectedIndex = 1;
                Find<ComboBox>(window, "InputSpeed").SelectedIndex = 2;
                editor.Text = "A";
                var operation = window.SendToAsync(connection.Target!);
                await connection.Entered.Task;
                Check(Find<Button>(window, "StopButton").IsEnabled && !Find<ComboBox>(connectionSettings, "ConnectionMethod").IsEnabled,
                    "ARM wait permits stop and locks transport changes");
                editor.Text = "B";
                await window.SendToAsync(connection.Target!);
                Check(connection.Begins == 1, "a second send cannot overlap ARM preparation");
                if (stopDuringArm)
                {
                    var stop = Find<Button>(window, "StopButton");
                    stop.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    Check(!operation.IsCompleted && !stop.IsEnabled, "XIAO Stop waits for the ARM response without accepting another stop");
                }
                connection.Allow.TrySetResult();
                await operation;
                Check(connection.Ends == 1 && !connection.CleanupCancelled, "ARM cleanup uses a separate live bounded token");
                Check(stopDuringArm ? connection.Frames.All(frame => frame.All(value => value == 0))
                    : connection.Frames.Any(frame => frame[0] == 2 && frame[2] == 4) && !connection.Frames.Any(frame => frame[2] == 5),
                    stopDuringArm ? "stop during ARM emits no text or IME keys" : "text is captured before ARM and later edits apply only to next transfer");
            }
            finally { window.PrepareForSessionEnd(); window.Close(); }
        }
        var exiting = new PreparingConnection();
        var exitWindow = new MainWindow(false, Path.Combine(settingsRoot, "exit"), keyboardConnection: exiting);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        exitWindow.Closed += (_, _) => closed.TrySetResult();
        exitWindow.Show();
        var sending = exitWindow.SendToAsync(exiting.Target!);
        await exiting.Entered.Task;
        exitWindow.RequestExit();
        await sending;
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(exiting.Disposed && exiting.Ends == 1 && exiting.Frames.Count == 0,
            "exit during ARM cancels the whole transfer and closes USB without the local Bluetooth shutdown path");

        var failing = new PreparingConnection();
        var failingWindow = new MainWindow(false, Path.Combine(settingsRoot, "unexpected"), keyboardConnection: failing);
        failingWindow.Show();
        var arming = failingWindow.SendToAsync(failing.Target!);
        await failing.Entered.Task;
        var cleanup = failingWindow.StopAfterUnexpectedErrorAsync();
        failing.Allow.TrySetResult();
        await cleanup;
        await arming;
        Check(failing.Disposed && failing.Ends == 1 && failing.Frames.Count == 0,
            "unexpected UI error attempts bounded disarm before closing USB and never starts the text");
        failingWindow.Close();
    }

    private static async Task VerifyHostChoiceGuidanceAsync()
    {
        var settingsRoot = Path.Combine(Directory.GetCurrentDirectory(), "_internal", "build-cache", "test-data", "jis-ble-input-xiao-ui", Guid.NewGuid().ToString("N"));
        foreach (var local in new[] { false, true })
        {
            var connection = new UnselectedConnection(local);
            var window = new MainWindow(false, Path.Combine(settingsRoot, local.ToString()), keyboardConnection: connection);
            try
            {
                window.Show();
                connection.RaiseChanged();
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                var status = Find<TextBlock>(window, "ConnectionStatus");
                var placeholder = Find<TextBlock>(window, "HostPlaceholder");
                Check(connection.Selections == 0, local ? "local Bluetooth does not auto-select an unremembered PC" : "XIAO does not auto-select a first-time registered PC");
                Check(local ? status.Text == "接続待ち" && placeholder.Text == "送信先PC"
                    : status.Text == "送信先PCを選択してください" && placeholder.Text == "ここで送信先PCを選択" && placeholder.Visibility == Visibility.Visible,
                    local ? "local Bluetooth keeps the existing waiting label" : "XIAO asks to choose the connected registered PC");
                if (!local)
                {
                    Find<ComboBox>(window, "Hosts").SelectedIndex = 0;
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                    Check(connection.Selections == 1 && status.Text == "接続済み" && placeholder.Visibility == Visibility.Collapsed,
                        "choosing the XIAO PC clears the guidance and becomes ready");
                    var speed = Find<ComboBox>(window, "InputSpeed");
                    var mode = Find<ComboBox>(window, "InputMode");
                    Check(speed.Items.Cast<string>().SequenceEqual(new[] { "遅め（約240 ms/文字）", "標準（約205 ms/文字）", "最速（約175 ms/文字）" })
                        && speed.SelectedIndex == 1, "XIAO Japanese shows three production presets and defaults to standard");
                    speed.SelectedIndex = 2;
                    mode.SelectedIndex = 1;
                    Check(speed.Items.Cast<string>().SequenceEqual(new[] { "遅め（約65 ms/文字）", "標準（約30 ms/文字）", "最速（約25 ms/文字）" })
                        && speed.SelectedIndex == 1, "XIAO ASCII shows three production presets and defaults to standard");
                    speed.SelectedIndex = 2;
                    mode.SelectedIndex = 0;
                    Find<TextBox>(window, "Editor").Text = "橋";
                    await window.SendToAsync(connection.Target!);
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                    Check(Find<TextBlock>(window, "TransferProgress").Text.Contains("処理時間"), "completed XIAO send displays measured duration");
                    window.OpenSettings();
                    Find<ComboBox>(window.OwnedWindows.Cast<Window>().Single(), "ConnectionMethod").SelectedIndex = 0;
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                    Check(speed.Items.Count == 3 && speed.SelectedIndex == 0, "switching to local Bluetooth restores the local Japanese speed");
                    mode.SelectedIndex = 1;
                    Check(speed.Items.Count == 3 && speed.SelectedIndex == 1, "switching to local Bluetooth restores the local ASCII speed");
                }
            }
            finally { window.PrepareForSessionEnd(); window.Close(); }
        }
    }

    private sealed class UnselectedConnection(bool local) : IKeyboardConnection
    {
        private string? selected;
        public int Selections { get; private set; }
        public event Action? Changed;
        public event Action<string>? Status { add { } remove { } }
        public void RaiseChanged() => Changed?.Invoke();
        public IReadOnlyList<HostChoice> Hosts => [new("synthetic-registered-peer", "登録済みPC（XIAO）")];
        public SendTarget? Target => selected is null ? null : new(selected, 1);
        public bool? CapsLock => false;
        public bool IsStarted => true;
        public bool UsesLocalBluetooth => local;
        public bool HasConnectedHost => true;
        public string Diagnostic => "synthetic unselected host";
        public void Select(string? id) { if (id is not null) Selections++; selected = id; }
        public Task StartAsync(bool newPairing, CancellationToken cancellation) => Task.CompletedTask;
        public Task RefreshHostsAsync() => Task.CompletedTask;
        public Task BeginSendAsync(SendTarget target, CancellationToken cancellation) => Task.CompletedTask;
        public Task EndSendAsync(SendTarget target, CancellationToken cancellation) => Task.CompletedTask;
        public Task WaitForHostsDisconnectedAsync(CancellationToken cancellation) => Task.CompletedTask;
        public bool IsReady(SendTarget target) => target == Target;
        public Task WriteAsync(byte[] report, SendTarget target, CancellationToken cancellation) => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class PreparingConnection : IKeyboardConnection
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Allow { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Begins { get; private set; }
        public int Ends { get; private set; }
        public bool CleanupCancelled { get; private set; }
        public bool Disposed { get; private set; }
        public List<byte[]> Frames { get; } = [];
        public event Action? Changed { add { } remove { } }
        public event Action<string>? Status { add { } remove { } }
        public IReadOnlyList<HostChoice> Hosts => [new("synthetic-xiao-peer", "試験PC")];
        public SendTarget? Target => new("synthetic-xiao-peer", 1);
        public bool? CapsLock => false;
        public bool IsStarted => !Disposed;
        public bool UsesLocalBluetooth => false;
        public bool HasConnectedHost => !Disposed;
        public string Diagnostic => "synthetic XIAO";
        public void Select(string? id) { }
        public Task StartAsync(bool newPairing, CancellationToken cancellation) => Task.CompletedTask;
        public Task RefreshHostsAsync() => Task.CompletedTask;
        public async Task BeginSendAsync(SendTarget target, CancellationToken cancellation)
        { Begins++; Entered.TrySetResult(); await Allow.Task.WaitAsync(cancellation); }
        public Task EndSendAsync(SendTarget target, CancellationToken cancellation)
        { Ends++; CleanupCancelled = cancellation.IsCancellationRequested; return Task.CompletedTask; }
        public Task WaitForHostsDisconnectedAsync(CancellationToken cancellation) => throw new InvalidOperationException("USB must not wait for local radio shutdown");
        public bool IsReady(SendTarget target) => !Disposed && target == Target;
        public Task WriteAsync(byte[] report, SendTarget target, CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); Frames.Add(report.ToArray()); return Task.CompletedTask; }
        public void Dispose() => Disposed = true;
    }

    private sealed class FailingTransport : IReportTransport
    {
        public bool Ready { get; set; } = true;
        public bool FailRelease { get; set; } = true;
        public bool CancelSend { get; set; }
        public bool WaitForStop { get; set; }
        public int Writes { get; private set; }
        public int ReleaseAttempts { get; private set; }
        public bool IsReady(SendTarget target) => Ready;
        public Task WriteAsync(byte[] report, SendTarget target, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            Writes++;
            if (report.All(value => value == 0))
            {
                ReleaseAttempts++;
                if (FailRelease) throw new InvalidOperationException("synthetic release failure");
                return Task.CompletedTask;
            }
            if (WaitForStop) return Task.Delay(Timeout.Infinite, cancellation);
            if (CancelSend) throw new OperationCanceledException("synthetic send cancellation");
            throw new InvalidOperationException("synthetic notification failure");
        }
    }

    private static T Find<T>(DependencyObject root, string name) where T : DependencyObject =>
        LogicalTreeHelper.FindLogicalNode(root, name) as T ?? throw new InvalidOperationException($"Missing UI element: {name}");

    private static bool IsWithinContent(FrameworkElement element, Window window)
    {
        var content = (FrameworkElement)window.Content;
        var bounds = element.TransformToAncestor(content).TransformBounds(new Rect(element.RenderSize));
        return bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= content.ActualWidth + 1 && bounds.Bottom <= content.ActualHeight + 1;
    }

    private static void SavePreview(Window window, string path)
    {
        var destination = Path.GetFullPath(path);
        var root = Path.GetFullPath(Directory.GetCurrentDirectory()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Preview outside workspace");
        var content = (FrameworkElement)window.Content;
        var image = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
            drawing.DrawRectangle(Brushes.White, null, bounds);
            drawing.DrawRectangle(new VisualBrush(content), null, bounds);
        }
        image.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var stream = File.Create(destination);
        encoder.Save(stream);
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        passed++;
        Console.WriteLine($"PASS: {label}");
    }
}
