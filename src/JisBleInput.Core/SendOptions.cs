namespace JisBleInput.Core;

public enum TextInputMode { Japanese, Ascii }
public enum JapaneseSpeed
{
    Standard, SlightlyFaster, Faster, XiaoSlow, XiaoStandard, XiaoFastest,
    MacUnicodeHexFastest, MacUnicodeHexHighSpeedTrial
}
public enum AsciiSpeed { Relaxed, Fast, Fastest, XiaoStandard, XiaoFastest }
public enum ReceiverKeyboardLayout { JapaneseJis, EnglishUs }
public enum JapaneseInputMethod { MicrosoftIme, MacUnicodeHex }

// A value captured with the text and destination, never read from changing UI state.
public sealed record SendOptions(TextInputMode Mode = TextInputMode.Japanese,
    JapaneseSpeed Speed = JapaneseSpeed.Standard,
    ReceiverKeyboardLayout Layout = ReceiverKeyboardLayout.JapaneseJis,
    AsciiSpeed DirectSpeed = AsciiSpeed.Fast,
    JapaneseInputMethod JapaneseInput = JapaneseInputMethod.MicrosoftIme)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Mode) || !Enum.IsDefined(Speed) || !Enum.IsDefined(Layout) || !Enum.IsDefined(DirectSpeed)
            || !Enum.IsDefined(JapaneseInput))
            throw new ArgumentException("入力モード・速度・受信側の配列を確認してください。");
        if (Mode == TextInputMode.Japanese && JapaneseInput == JapaneseInputMethod.MacUnicodeHex
            && Speed is JapaneseSpeed.XiaoSlow or JapaneseSpeed.XiaoStandard)
            throw new ArgumentException("Mac向けの入力速度を確認してください。");
        if (Speed is JapaneseSpeed.MacUnicodeHexFastest or JapaneseSpeed.MacUnicodeHexHighSpeedTrial
            && (Mode != TextInputMode.Japanese || JapaneseInput != JapaneseInputMethod.MacUnicodeHex))
            throw new ArgumentException("この速度はMac向けの日本語入力で使えます。");
    }
}
