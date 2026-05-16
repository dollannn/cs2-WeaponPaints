using System.Collections.Concurrent;
using CounterStrikeSharp.API.Modules.Utils;
using WeaponPaints.API;
using WeaponPaints.Models;

namespace WeaponPaints.Services;

internal sealed class PlayerLoadoutCache
{
    private readonly ConcurrentDictionary<ulong, PlayerLoadout> _loadouts = new();
    private readonly ConcurrentDictionary<int, SlotBinding> _slotBindings = new();

    public void Store(PlayerLoadout loadout)
    {
        _loadouts.AddOrUpdate(loadout.SteamId64, loadout, (_, existing) => Merge(existing, loadout));
    }

    public bool TryGet(ulong steamId64, out PlayerLoadout? loadout)
    {
        return _loadouts.TryGetValue(steamId64, out loadout);
    }

    public void BindSlot(ulong steamId64, int slot, int? userId = null)
    {
        _slotBindings[slot] = new SlotBinding(steamId64, userId);
    }

    public bool RemoveSlot(int slot, ulong? steamId64 = null, int? userId = null)
    {
        if (!_slotBindings.TryGetValue(slot, out var binding))
        {
            if (steamId64.HasValue || userId.HasValue)
                return false;

            ClearLegacySlot(slot, WeaponPaintsReloadFlags.All);
            return true;
        }

        if ((steamId64.HasValue && binding.SteamId64 != steamId64.Value) ||
            (userId.HasValue && binding.UserId.HasValue && binding.UserId.Value != userId.Value))
        {
            return false;
        }

        _slotBindings.TryRemove(slot, out _);
        ClearLegacySlot(slot, WeaponPaintsReloadFlags.All);
        return true;
    }

    public void RemovePlayer(ulong steamId64)
    {
        _loadouts.TryRemove(steamId64, out _);
    }

    public void Clear()
    {
        _loadouts.Clear();
        _slotBindings.Clear();
        global::WeaponPaints.WeaponPaints.GPlayerWeaponsInfo.Clear();
        global::WeaponPaints.WeaponPaints.GPlayersKnife.Clear();
        global::WeaponPaints.WeaponPaints.GPlayersGlove.Clear();
        global::WeaponPaints.WeaponPaints.GPlayersAgent.Clear();
        global::WeaponPaints.WeaponPaints.GPlayersPin.Clear();
        global::WeaponPaints.WeaponPaints.GPlayersMusic.Clear();
    }

    public void ApplyToLegacySlot(PlayerLoadout loadout, int slot, int? userId, WeaponPaintsReloadFlags flags)
    {
        if (flags.HasFlag(WeaponPaintsReloadFlags.Weapons))
            PreserveLiveStatTrak(slot, loadout);

        BindSlot(loadout.SteamId64, slot, userId);
        ClearLegacySlot(slot, flags);

        if (flags.HasFlag(WeaponPaintsReloadFlags.Knife) && loadout.Knives.Count > 0)
        {
            global::WeaponPaints.WeaponPaints.GPlayersKnife[slot] = ToConcurrent(loadout.Knives);
        }

        if (flags.HasFlag(WeaponPaintsReloadFlags.Gloves) && loadout.Gloves.Count > 0)
        {
            global::WeaponPaints.WeaponPaints.GPlayersGlove[slot] = ToConcurrent(loadout.Gloves);
        }

        if (flags.HasFlag(WeaponPaintsReloadFlags.Music) && loadout.Music.Count > 0)
        {
            global::WeaponPaints.WeaponPaints.GPlayersMusic[slot] = ToConcurrent(loadout.Music);
        }

        if (flags.HasFlag(WeaponPaintsReloadFlags.Pins) && loadout.Pins.Count > 0)
        {
            global::WeaponPaints.WeaponPaints.GPlayersPin[slot] = ToConcurrent(loadout.Pins);
        }

        if (flags.HasFlag(WeaponPaintsReloadFlags.Agent) &&
            (!string.IsNullOrEmpty(loadout.AgentCT) || !string.IsNullOrEmpty(loadout.AgentT)))
        {
            global::WeaponPaints.WeaponPaints.GPlayersAgent[slot] = (loadout.AgentCT, loadout.AgentT);
        }

        if (flags.HasFlag(WeaponPaintsReloadFlags.Weapons) && loadout.Weapons.Count > 0)
        {
            global::WeaponPaints.WeaponPaints.GPlayerWeaponsInfo[slot] = ToConcurrentWeaponInfo(loadout.Weapons);
        }
    }

    private static PlayerLoadout Merge(PlayerLoadout existing, PlayerLoadout incoming)
    {
        if (incoming.LoadedFlags.HasFlag(WeaponPaintsReloadFlags.Knife))
            Replace(existing.Knives, incoming.Knives);
        if (incoming.LoadedFlags.HasFlag(WeaponPaintsReloadFlags.Gloves))
            Replace(existing.Gloves, incoming.Gloves);
        if (incoming.LoadedFlags.HasFlag(WeaponPaintsReloadFlags.Music))
            Replace(existing.Music, incoming.Music);
        if (incoming.LoadedFlags.HasFlag(WeaponPaintsReloadFlags.Pins))
            Replace(existing.Pins, incoming.Pins);
        if (incoming.LoadedFlags.HasFlag(WeaponPaintsReloadFlags.Weapons))
            Replace(existing.Weapons, incoming.Weapons);

        if (incoming.LoadedFlags.HasFlag(WeaponPaintsReloadFlags.Agent))
        {
            existing.AgentCT = incoming.AgentCT;
            existing.AgentT = incoming.AgentT;
        }

        existing.LoadedFlags |= incoming.LoadedFlags;
        return existing;
    }

    private static void ClearLegacySlot(int slot, WeaponPaintsReloadFlags flags)
    {
        if (flags.HasFlag(WeaponPaintsReloadFlags.Knife))
            global::WeaponPaints.WeaponPaints.GPlayersKnife.TryRemove(slot, out _);
        if (flags.HasFlag(WeaponPaintsReloadFlags.Gloves))
            global::WeaponPaints.WeaponPaints.GPlayersGlove.TryRemove(slot, out _);
        if (flags.HasFlag(WeaponPaintsReloadFlags.Music))
            global::WeaponPaints.WeaponPaints.GPlayersMusic.TryRemove(slot, out _);
        if (flags.HasFlag(WeaponPaintsReloadFlags.Pins))
            global::WeaponPaints.WeaponPaints.GPlayersPin.TryRemove(slot, out _);
        if (flags.HasFlag(WeaponPaintsReloadFlags.Agent))
            global::WeaponPaints.WeaponPaints.GPlayersAgent.TryRemove(slot, out _);
        if (flags.HasFlag(WeaponPaintsReloadFlags.Weapons))
            global::WeaponPaints.WeaponPaints.GPlayerWeaponsInfo.TryRemove(slot, out _);
    }

    private static void PreserveLiveStatTrak(int slot, PlayerLoadout incomingLoadout)
    {
        if (!global::WeaponPaints.WeaponPaints.GPlayerWeaponsInfo.TryGetValue(slot, out var liveTeams))
            return;

        foreach (var (team, incomingWeapons) in incomingLoadout.Weapons)
        {
            if (!liveTeams.TryGetValue(team, out var liveWeapons))
                continue;

            foreach (var (weaponDefIndex, incomingWeaponInfo) in incomingWeapons)
            {
                if (!incomingWeaponInfo.StatTrak ||
                    !liveWeapons.TryGetValue(weaponDefIndex, out var liveWeaponInfo) ||
                    liveWeaponInfo.StatTrakCount <= incomingWeaponInfo.StatTrakCount)
                {
                    continue;
                }

                incomingWeaponInfo.StatTrakCount = liveWeaponInfo.StatTrakCount;
            }
        }
    }

    private static void Replace<TKey, TValue>(Dictionary<TKey, TValue> target, Dictionary<TKey, TValue> source)
        where TKey : notnull
    {
        target.Clear();
        foreach (var (key, value) in source)
        {
            target[key] = value;
        }
    }

    private static ConcurrentDictionary<CsTeam, TValue> ToConcurrent<TValue>(Dictionary<CsTeam, TValue> source)
    {
        return new ConcurrentDictionary<CsTeam, TValue>(source);
    }

    private static ConcurrentDictionary<CsTeam, ConcurrentDictionary<int, WeaponInfo>> ToConcurrentWeaponInfo(
        Dictionary<CsTeam, Dictionary<int, WeaponInfo>> source)
    {
        var result = new ConcurrentDictionary<CsTeam, ConcurrentDictionary<int, WeaponInfo>>();

        foreach (var (team, weapons) in source)
        {
            result[team] = new ConcurrentDictionary<int, WeaponInfo>(weapons);
        }

        return result;
    }

    private sealed record SlotBinding(ulong SteamId64, int? UserId);
}
