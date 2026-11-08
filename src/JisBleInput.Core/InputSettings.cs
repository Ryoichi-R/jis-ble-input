namespace JisBleInput.Core;

// The UI owns this draft. Capture returns a separate immutable send value.
public sealed class InputSettings
{
    private JapaneseSpeed? trialSpeed;

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
    public void SetSpeed(int index, bool allowXiaoTrials = false)
    {
        if (index < 0 || index >= SpeedLabels(Mode, allowXiaoTrials).Count)
            throw new ArgumentException("入力速度を選択してください。");
        if (Mode == TextInputMode.Japanese)
        {
            var speed = (JapaneseSpeed)index;
            trialSpeed = speed > JapaneseSpeed.Faster ? speed : null;
            if (trialSpeed is null) JapaneseSpeed = speed;
        }
        else AsciiSpeed = (AsciiSpeed)index;
    }

    public void ClearTrialSpeed() => trialSpeed = null;

    public int SpeedIndex => Mode == TextInputMode.Japanese ? (int)(trialSpeed ?? JapaneseSpeed) : (int)AsciiSpeed;

    public void SetLayout(ReceiverKeyboardLayout layout)
    {
        if (!Enum.IsDefined(layout)) throw new ArgumentException("受信側の配列を選択してください。");
        Layout = layout;
    }

    public SendOptions Capture() => new(Mode, Mode == TextInputMode.Japanese ? trialSpeed ?? JapaneseSpeed : JapaneseSpeed, Layout, AsciiSpeed);

    public InputPreferences ToPreferences() => new(Mode: Mode, JapaneseSpeed: JapaneseSpeed, AsciiSpeed: AsciiSpeed, Layout: Layout);

    public void Restore(InputPreferences preferences)
    {
        preferences.Validate();
        ClearTrialSpeed();
        Mode = preferences.Mode;
        JapaneseSpeed = preferences.JapaneseSpeed;
        AsciiSpeed = preferences.AsciiSpeed;
        Layout = preferences.Layout;
    }

    private static readonly IReadOnlyList<string> JapaneseLabels =
        ["標準（1.10秒/文字）", "少し速め（0.90秒/文字）", "速い（0.70秒/文字）"];

    public static IReadOnlyList<string> SpeedLabels(TextInputMode mode, bool allowXiaoTrials = false) => mode switch
    {
        TextInputMode.Ascii => ["ゆっくり（0.12秒/文字）", "高速（0.08秒/文字）", "最速（0.05秒/文字）"],
        _ when allowXiaoTrials => [.. JapaneseLabels, "試験 0.60秒/文字", "試験 0.50秒/文字", "試験 0.40秒/文字", "試験 0.35秒/文字"],
        _ => JapaneseLabels
    };

    public string Description => Mode == TextInputMode.Japanese
        ? $"日本語・混在文 / {SpeedLabels(Mode, trialSpeed is not null)[SpeedIndex]}"
        : $"英数字・URL / {SpeedLabels(Mode)[SpeedIndex]} / {(Layout == ReceiverKeyboardLayout.JapaneseJis ? "JIS" : "US")}";
}
