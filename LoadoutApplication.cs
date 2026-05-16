using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using WeaponPaints.API;

namespace WeaponPaints;

public partial class WeaponPaints
{
    internal bool ApplyLoadedLoadout(CCSPlayerController player, WeaponPaintsReloadFlags flags)
    {
        if (!Utility.IsPlayerValid(player) || flags == WeaponPaintsReloadFlags.None)
            return false;

		var applied = false;
		var requiresAlivePawn = flags.HasFlag(WeaponPaintsReloadFlags.Weapons) ||
		                     flags.HasFlag(WeaponPaintsReloadFlags.Knife) ||
		                     flags.HasFlag(WeaponPaintsReloadFlags.Gloves) ||
		                     flags.HasFlag(WeaponPaintsReloadFlags.Agent);
		var canApplyAlivePawn = _gBCommandsAllowed &&
		                        player.PawnIsAlive &&
		                        player.PlayerPawn.Value != null &&
		                        (LifeState_t)player.LifeState == LifeState_t.LIFE_ALIVE;

        if (flags.HasFlag(WeaponPaintsReloadFlags.Music))
        {
            GivePlayerMusicKit(player);
            applied = true;
        }

		if (flags.HasFlag(WeaponPaintsReloadFlags.Agent) && canApplyAlivePawn)
		{
			GivePlayerAgent(player);
			applied = true;
		}

		if (flags.HasFlag(WeaponPaintsReloadFlags.Gloves) && canApplyAlivePawn)
		{
			GivePlayerGloves(player);
			applied = true;
		}

		if ((flags.HasFlag(WeaponPaintsReloadFlags.Weapons) || flags.HasFlag(WeaponPaintsReloadFlags.Knife)) && canApplyAlivePawn)
		{
			RefreshWeapons(player);
			applied = true;
        }

		if (flags.HasFlag(WeaponPaintsReloadFlags.Pins))
		{
			var steamId = player.SteamID;
			var userId = player.UserId;
			AddTimer(0.15f, () =>
			{
				var currentPlayer = CounterStrikeSharp.API.Utilities.GetPlayers().FirstOrDefault(candidate =>
					candidate is { IsValid: true, IsBot: false, UserId: not null } &&
					candidate.SteamID == steamId &&
					candidate.UserId == userId);

				if (Utility.IsPlayerValid(currentPlayer))
					GivePlayerPin(currentPlayer!);
			}, TimerFlags.STOP_ON_MAPCHANGE);
			applied = true;
		}

		return applied && (!requiresAlivePawn || canApplyAlivePawn);
	}
}
