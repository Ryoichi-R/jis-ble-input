using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using JisBleInput.Core;

namespace JisBleInput.App;

internal enum XiaoPortKind { Application, Bootloader, Unsupported }

internal sealed record XiaoUsbPort(string PortName, string Serial, XiaoPortKind Kind = XiaoPortKind.Application)
{
    public string Hash => ConnectionPreferences.HashPeer(Serial.ToUpperInvariant());
    public bool Connectable => Kind == XiaoPortKind.Application;
    public string Display => Kind switch
    {
        XiaoPortKind.Application => $"{PortName} / XIAO [{Hash[..8]}]",
        XiaoPortKind.Bootloader => $"{PortName} / XIAO 書込モード（接続不可）",
        _ => $"{PortName} / 対象外のSeeed基板（通常版・Plus等）"
    };

    // Sense application 8045 only; bootloader 0045 is shown but never opened.
    public static XiaoPortKind Classify(string instanceId) =>
        instanceId.Contains("VID_2886&PID_8045", StringComparison.OrdinalIgnoreCase) ? XiaoPortKind.Application
        : instanceId.Contains("VID_2886&PID_0045", StringComparison.OrdinalIgnoreCase) ? XiaoPortKind.Bootloader
        : XiaoPortKind.Unsupported;

    public static IReadOnlyList<XiaoUsbPort> Discover()
    {
        var result = new List<XiaoUsbPort>();
        var ports = new Guid("4d36e978-e325-11ce-bfc1-08002be10318");
        var set = SetupDiGetClassDevs(ref ports, null, IntPtr.Zero, 2);
        if (set == new IntPtr(-1)) throw new IOException("USBポート一覧を取得できません。");
        try
        {
            for (uint index = 0; ; index++)
            {
                var info = new DeviceInfo { Size = (uint)Marshal.SizeOf<DeviceInfo>() };
                if (!SetupDiEnumDeviceInfo(set, index, ref info))
                {
                    if (Marshal.GetLastWin32Error() != 259) throw new IOException("USBポート列挙に失敗しました。");
                    break;
                }
                var instance = DeviceId(info.DevInst);
                if (!instance.Contains("VID_2886&PID_", StringComparison.OrdinalIgnoreCase)) continue;
                var kind = Classify(instance);
                var name = Property(set, ref info, 12);
                var match = Regex.Match(name, @"\((COM[0-9]+)\)", RegexOptions.CultureInvariant);
                if (!match.Success) continue;
                var serial = "";
                var node = info.DevInst;
                for (var level = 0; level < 5; level++)
                {
                    var identity = DeviceId(node);
                    if (identity.StartsWith("USB\\VID_2886&PID_8045\\", StringComparison.OrdinalIgnoreCase))
                    {
                        serial = identity[(identity.LastIndexOf('\\') + 1)..].ToUpperInvariant();
                        break;
                    }
                    if (CM_Get_Parent(out var parent, node, 0) != 0) break;
                    node = parent;
                }
                // Without an actual USB serial number, location IDs cannot prove board identity.
                if (kind != XiaoPortKind.Application) result.Add(new(match.Groups[1].Value, "", kind));
                else if (serial.Length == 16 && serial.All(char.IsAsciiHexDigit)) result.Add(new(match.Groups[1].Value, serial));
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }

    private static string DeviceId(uint node)
    {
        var text = new StringBuilder(1024);
        return CM_Get_Device_ID(node, text, text.Capacity, 0) == 0 ? text.ToString() : "";
    }
    private static string Property(IntPtr set, ref DeviceInfo info, uint property)
    {
        var data = new byte[2048];
        return SetupDiGetDeviceRegistryProperty(set, ref info, property, out _, data, (uint)data.Length, out _)
            ? Encoding.Unicode.GetString(data).TrimEnd('\0') : "";
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfo { public uint Size; public Guid ClassGuid; public uint DevInst; public UIntPtr Reserved; }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string? enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref DeviceInfo info);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set, ref DeviceInfo info, uint property, out uint type, byte[] data, uint size, out uint needed);
    [DllImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_Parent(out uint parent, uint node, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_ID(uint node, StringBuilder buffer, int length, uint flags);
}
