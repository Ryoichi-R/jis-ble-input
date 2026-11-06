using JisBleInput.Core;

namespace JisBleInput.App;

// A presentation identifier, not an authentication credential. Transport
// authorization continues to use the complete paired device ID and generation.
internal sealed record HostChoice(string Id, string Name)
{
    public string Fingerprint => ConnectionPreferences.HashPeer(Id);
    public string Display => $"{Name} [{Fingerprint[..8]}]";
    public string Description => $"{Name}\n識別用ID（SHA-256）: {Fingerprint}\n同名PCは、1台ずつ接続してこの表示を確認してください。";
}
