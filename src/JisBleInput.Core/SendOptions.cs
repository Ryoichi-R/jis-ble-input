namespace JisBleInput.Core;

public enum TextInputMode { Japanese, Ascii }
public enum JapaneseSpeed { Standard, SlightlyFaster, Faster, XiaoSlow, XiaoStandard, XiaoFastest }
public enum AsciiSpeed { Relaxed, Fast, Fastest, XiaoStandard, XiaoFastest }
public enum ReceiverKeyboardLayout { JapaneseJis, EnglishUs }

// A value captured with the text and destination, never read from changing UI state.
public sealed record SendOptions(TextInputMode Mode = TextInputMode.Japanese,
    JapaneseSpeed Speed = JapaneseSpeed.Standard,
    ReceiverKeyboardLayout Layout = ReceiverKeyboardLayout.JapaneseJis,
    AsciiSpeed DirectSpeed = AsciiSpeed.Fast)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Mode) || !Enum.IsDefined(Speed) || !Enum.IsDefined(Layout) || !Enum.IsDefined(DirectSpeed))
            throw new ArgumentException("入力モード・速度・受信側の配列を確認してください。");
    }
}
