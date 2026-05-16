using CounterStrikeSharp.API.Modules.Utils;
using WeaponPaints.API;

namespace WeaponPaints.Models;

internal sealed class PlayerLoadout(ulong steamId64)
{
    public ulong SteamId64 { get; } = steamId64;

    public Dictionary<CsTeam, string> Knives { get; } = new();
    public Dictionary<CsTeam, ushort> Gloves { get; } = new();
    public Dictionary<CsTeam, ushort> Music { get; } = new();
    public Dictionary<CsTeam, ushort> Pins { get; } = new();
    public Dictionary<CsTeam, Dictionary<int, WeaponInfo>> Weapons { get; } = new();

    public string? AgentCT { get; set; }
    public string? AgentT { get; set; }

    public WeaponPaintsReloadFlags LoadedFlags { get; set; } = WeaponPaintsReloadFlags.None;
}
