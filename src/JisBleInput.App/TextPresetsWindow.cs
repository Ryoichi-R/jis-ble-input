using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using JisBleInput.Core;

namespace JisBleInput.App;

internal sealed class TextPresetsWindow : Window
{
    private readonly TextPresetStore store;
    private readonly TextPreset draft;
    private readonly bool xiaoAvailable;
    private readonly ComboBox entries = new() { Name = "PresetEntries", MinHeight = 30 };
    private readonly TextBox displayName = new() { Name = "PresetName", MaxLength = 80 };
    private readonly TextBox text = new()
    {
        Name = "PresetText",
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Height = 94
    };
    private readonly ComboBox mode = new() { Name = "PresetMode", ItemsSource = new[] { "日本語・混在文", "英数字・URL" } };
    private readonly ComboBox localSpeed = new();
    private readonly ComboBox xiaoSpeed = new();
    private readonly TextBlock feedback = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button delete = Action("削除");
    private readonly TextBlock recovery = new() { TextWrapping = TextWrapping.Wrap };
    private bool refreshing;

    internal TextPreset[] Presets { get; private set; }
    internal string? RecoveryNotice { get; private set; }

    internal TextPresetsWindow(Window owner, TextPresetStore store, TextPreset[] presets, TextPreset draft, int selectedIndex,
        string? recoveryNotice, bool xiaoAvailable)
    {
        Owner = owner;
        this.store = store;
        this.draft = draft;
        this.xiaoAvailable = xiaoAvailable;
        Presets = presets;
        RecoveryNotice = recoveryNotice;
        Title = "JIS BLE Input — 定型文の管理";
        Icon = owner.Icon;
        FontFamily = owner.FontFamily;
        FontSize = owner.FontSize;
        Width = 420;
        Height = 480;
        MinWidth = 360;
        MinHeight = 320;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel { Margin = new Thickness(14) };
        recovery.Visibility = recoveryNotice is null ? Visibility.Collapsed : Visibility.Visible;
        recovery.Text = $"{recoveryNotice} 保存すると元ファイルを退避します。";
        recovery.Margin = new Thickness(0, 0, 0, 8);
        panel.Children.Add(recovery);
        var selectRow = new DockPanel();
        var add = Action("現在の本文から新規");
        add.Name = "NewPresetButton";
        add.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(add, Dock.Right);
        selectRow.Children.Add(add);
        selectRow.Children.Add(entries);
        panel.Children.Add(selectRow);
        AutomationProperties.SetName(entries, "編集する定型文");
        AddField(panel, "表示名", displayName);
        AddField(panel, "本文", text);
        AddField(panel, "入力方法", mode);
        var speeds = new Grid();
        speeds.ColumnDefinitions.Add(new ColumnDefinition());
        speeds.ColumnDefinitions.Add(new ColumnDefinition());
        var xiao = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        var local = new StackPanel();
        AddField(xiao, "XIAOの速度", xiaoSpeed);
        AddField(local, "内蔵Bluetoothの速度", localSpeed);
        Grid.SetColumn(local, 1);
        if (xiaoAvailable) speeds.Children.Add(xiao);
        else Grid.SetColumnSpan(local, 2);
        speeds.Children.Add(local);
        panel.Children.Add(speeds);
        panel.Children.Add(new TextBlock
        {
            Text = "登録した本文はこのPCに保存されます。認証情報は登録しないでください。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var save = Action("保存");
        save.Name = "SavePresetButton";
        var close = Action("閉じる");
        close.Name = "ClosePresetWindow";
        close.IsCancel = true;
        delete.Name = "DeletePresetButton";
        actions.Children.Add(delete);
        actions.Children.Add(save);
        actions.Children.Add(close);
        panel.Children.Add(actions);
        feedback.Name = "PresetFeedback";
        panel.Children.Add(feedback);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        entries.SelectionChanged += (_, _) => { if (!refreshing) ShowEntry(); };
        mode.SelectionChanged += (_, _) => { if (!refreshing) UpdateSpeeds(null, null); };
        add.Click += (_, _) => { entries.SelectedIndex = 0; ShowEntry(); displayName.Focus(); };
        save.Click += (_, _) => SaveEntry();
        close.Click += (_, _) => Close();
        delete.Click += (_, _) =>
        {
            var index = entries.SelectedIndex - 1;
            if (index < 0 || MessageBox.Show(this, "選んだ定型文を削除しますか？", "定型文の削除",
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            var next = Presets.ToList();
            next.RemoveAt(index);
            Save(next, 0, "定型文を削除しました。");
        };
        UpdateEntries(selectedIndex + 1);
    }

    private void UpdateEntries(int selected)
    {
        refreshing = true;
        entries.ItemsSource = new[] { "新規登録" }.Concat(Presets.Select(preset => preset.Name)).ToArray();
        entries.SelectedIndex = selected;
        refreshing = false;
        ShowEntry();
    }

    private void ShowEntry()
    {
        var index = entries.SelectedIndex - 1;
        var preset = index >= 0 ? Presets[index] : draft;
        refreshing = true;
        displayName.Text = preset.Name;
        text.Text = preset.Text;
        mode.SelectedIndex = (int)preset.Mode;
        UpdateSpeeds(preset.BluetoothSpeed, preset.XiaoSpeed);
        refreshing = false;
        delete.IsEnabled = index >= 0;
        feedback.Text = "";
    }

    private void UpdateSpeeds(int? local, int? xiao)
    {
        var inputMode = (TextInputMode)mode.SelectedIndex;
        localSpeed.ItemsSource = new[] { "現在の設定を使う" }.Concat(InputSettings.SpeedLabels(inputMode)).ToArray();
        xiaoSpeed.ItemsSource = new[] { "現在の設定を使う" }.Concat(InputSettings.SpeedLabels(inputMode, useXiao: true)).ToArray();
        localSpeed.SelectedIndex = (local ?? -1) + 1;
        xiaoSpeed.SelectedIndex = (xiao ?? -1) + 1;
    }

    private void SaveEntry()
    {
        var index = entries.SelectedIndex - 1;
        var xiaoSelection = xiaoAvailable
            ? (xiaoSpeed.SelectedIndex > 0 ? xiaoSpeed.SelectedIndex - 1 : (int?)null)
            : (index >= 0 ? Presets[index].XiaoSpeed : draft.XiaoSpeed);
        var preset = new TextPreset(displayName.Text.Trim(), text.Text, (TextInputMode)mode.SelectedIndex,
            localSpeed.SelectedIndex > 0 ? localSpeed.SelectedIndex - 1 : null,
            xiaoSelection);
        var next = Presets.ToList();
        if (index < 0) { index = next.Count; next.Add(preset); }
        else next[index] = preset;
        Save(next, index + 1, "定型文を保存しました。");
    }

    private void Save(List<TextPreset> next, int selected, string success)
    {
        try
        {
            var updated = next.ToArray();
            if (RecoveryNotice is not null && MessageBox.Show(this,
                    "元の定型文ファイルを退避し、読み込めた定型文と今回の変更で保存しますか？",
                    "定型文ファイルの復旧", MessageBoxButton.YesNo, MessageBoxImage.Question,
                    MessageBoxResult.No) != MessageBoxResult.Yes) return;
            var backup = store.Save(updated, backupOriginal: RecoveryNotice is not null);
            Presets = updated;
            RecoveryNotice = null;
            recovery.Visibility = Visibility.Collapsed;
            UpdateEntries(selected);
            feedback.Text = backup is null ? success : $"{success} 元ファイル: {Path.GetFileName(backup)}";
        }
        catch (ArgumentException error) { feedback.Text = error.Message; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { feedback.Text = "定型文を保存できませんでした。"; }
    }

    private static Button Action(string label) => new() { Content = label, Padding = new Thickness(8, 5, 8, 5) };

    private static void AddField(Panel panel, string label, Control control)
    {
        panel.Children.Add(new Label { Content = label, Target = control, Padding = new Thickness(0, 6, 0, 2) });
        AutomationProperties.SetName(control, label);
        control.MinHeight = 28;
        panel.Children.Add(control);
    }
}
