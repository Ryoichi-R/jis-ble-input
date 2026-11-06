namespace JisBleInput.Core;

// The UI owns this draft. Capture returns a separate immutable send value.
public sealed class InputSettings
{
    public TextInputMode Mode { get; private set; } = TextInputMode.Japanese;
    public JapaneseSpeed JapaneseSpeed { get; private set; } = JapaneseSpeed.Standard;
    public AsciiSpeed AsciiSpeed { get; private set; } = AsciiSpeed.Fast;
    public ReceiverKeyboardLayout Layout { get; private set; } = ReceiverKeyboardLayout.JapaneseJis;

    public void SetMode(TextInputMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentException("入力内容を選択してください。");
        Mode = mode;
    }

    // Selector indexes follow the enum order: slowest first.
    public void SetSpeed(int index)
    {
        if (index is < 0 or > 2) throw new ArgumentException("入力速度を選択してください。");
        if (Mode == TextInputMode.Japanese) JapaneseSpeed = (JapaneseSpeed)index;
        else AsciiSpeed = (AsciiSpeed)index;
    }

    public int SpeedIndex => Mode == TextInputMode.Japanese ? (int)JapaneseSpeed : (int)AsciiSpeed;

    public void SetLayout(ReceiverKeyboardLayout layout)
    {
        if (!Enum.IsDefined(layout)) throw new ArgumentException("受信側の配列を選択してください。");
        Layout = layout;
    }

    public SendOptions Capture() => new(Mode, JapaneseSpeed, Layout, AsciiSpeed);

    public InputPreferences ToPreferences() => new(Mode: Mode, JapaneseSpeed: JapaneseSpeed, AsciiSpeed: AsciiSpeed, Layout: Layout);

    public void Restore(InputPreferences preferences)
    {
        preferences.Validate();
        Mode = preferences.Mode;
        JapaneseSpeed = preferences.JapaneseSpeed;
        AsciiSpeed = preferences.AsciiSpeed;
        Layout = preferences.Layout;
    }

    public static IReadOnlyList<string> SpeedLabels(TextInputMode mode) => mode == TextInputMode.Japanese
        ? ["標準（1.10秒/文字）", "少し速め（0.90秒/文字）", "速い（0.70秒/文字）"]
        : ["ゆっくり（0.12秒/文字）", "高速（0.08秒/文字）", "最速（0.05秒/文字）"];

    public string Description => Mode == TextInputMode.Japanese
        ? $"日本語・混在文 / {SpeedLabels(Mode)[SpeedIndex]}"
        : $"英数字・URL / {SpeedLabels(Mode)[SpeedIndex]} / {(Layout == ReceiverKeyboardLayout.JapaneseJis ? "JIS" : "US")}";
}
