namespace JisBleInput.Core;

// The UI owns this draft. Capture returns a separate immutable send value.
public sealed class InputSettings
{
    private XiaoSpeedPreset xiaoJapanesePreset = XiaoSpeedPreset.Standard;
    private XiaoSpeedPreset xiaoAsciiPreset = XiaoSpeedPreset.Standard;

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
    public void SetSpeed(int index, bool useXiao = false)
    {
        if (index < 0 || index >= SpeedLabels(Mode, useXiao).Count)
            throw new ArgumentException("入力速度を選択してください。");
        if (useXiao)
        {
            if (Mode == TextInputMode.Japanese) xiaoJapanesePreset = (XiaoSpeedPreset)index;
            else xiaoAsciiPreset = (XiaoSpeedPreset)index;
        }
        else if (Mode == TextInputMode.Japanese) JapaneseSpeed = (JapaneseSpeed)index;
        else AsciiSpeed = (AsciiSpeed)index;
    }

    public int GetSpeedIndex(bool useXiao = false)
    {
        if (useXiao) return (int)(Mode == TextInputMode.Japanese ? xiaoJapanesePreset : xiaoAsciiPreset);
        return Mode == TextInputMode.Japanese ? (int)JapaneseSpeed : (int)AsciiSpeed;
    }

    public void SetLayout(ReceiverKeyboardLayout layout)
    {
        if (!Enum.IsDefined(layout)) throw new ArgumentException("受信側の配列を選択してください。");
        Layout = layout;
    }

    public SendOptions Capture(bool useXiao = false) => new(Mode,
        useXiao ? XiaoJapaneseSpeeds[(int)xiaoJapanesePreset].Speed : JapaneseSpeed, Layout,
        useXiao ? XiaoAsciiSpeeds[(int)xiaoAsciiPreset].Speed : AsciiSpeed);

    public InputPreferences ToPreferences() => new(Mode: Mode, JapaneseSpeed: JapaneseSpeed, AsciiSpeed: AsciiSpeed,
        Layout: Layout, XiaoJapanesePreset: xiaoJapanesePreset, XiaoAsciiPreset: xiaoAsciiPreset);

    public void Restore(InputPreferences preferences)
    {
        preferences.Validate();
        Mode = preferences.Mode;
        JapaneseSpeed = preferences.JapaneseSpeed;
        AsciiSpeed = preferences.AsciiSpeed;
        Layout = preferences.Layout;
        xiaoJapanesePreset = preferences.XiaoJapanesePreset;
        xiaoAsciiPreset = preferences.XiaoAsciiPreset;
    }

    private static readonly IReadOnlyList<string> JapaneseLabels =
        ["標準（1.10秒/文字）", "少し速め（0.90秒/文字）", "速い（0.70秒/文字）"];
    private static readonly IReadOnlyList<string> AsciiLabels =
        ["ゆっくり（0.12秒/文字）", "高速（0.08秒/文字）", "最速（0.05秒/文字）"];
    private static readonly (string Label, JapaneseSpeed Speed)[] XiaoJapaneseSpeeds =
    [
        ("遅め（約240 ms/文字）", JapaneseSpeed.XiaoSlow),
        ("標準（約205 ms/文字）", JapaneseSpeed.XiaoStandard),
        ("最速（約175 ms/文字）", JapaneseSpeed.XiaoFastest)
    ];
    private static readonly (string Label, AsciiSpeed Speed)[] XiaoAsciiSpeeds =
    [
        ("遅め（約65 ms/文字）", AsciiSpeed.Fastest),
        ("標準（約30 ms/文字）", AsciiSpeed.XiaoStandard),
        ("最速（約25 ms/文字）", AsciiSpeed.XiaoFastest)
    ];

    public static IReadOnlyList<string> SpeedLabels(TextInputMode mode, bool useXiao = false) => (mode, useXiao) switch
    {
        (TextInputMode.Ascii, true) => XiaoAsciiSpeeds.Select(preset => preset.Label).ToArray(),
        (TextInputMode.Japanese, true) => XiaoJapaneseSpeeds.Select(preset => preset.Label).ToArray(),
        (TextInputMode.Ascii, false) => AsciiLabels,
        _ => JapaneseLabels
    };

    public string Describe(bool useXiao = false)
    {
        var label = SpeedLabels(Mode, useXiao)[GetSpeedIndex(useXiao)];
        return Mode == TextInputMode.Japanese ? $"日本語・混在文 / {label}"
            : $"英数字・URL / {label} / {(Layout == ReceiverKeyboardLayout.JapaneseJis ? "JIS" : "US")}";
    }
}
