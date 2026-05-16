using Dapper;
using MySqlConnector;
using System.Collections.Concurrent;
using CounterStrikeSharp.API.Modules.Utils;
using System.Globalization;
using WeaponPaints.API;
using WeaponPaints.Models;

namespace WeaponPaints;

internal class WeaponSynchronization
{
	private readonly WeaponPaintsConfig _config;
	private readonly Database _database;

	internal WeaponSynchronization(Database database, WeaponPaintsConfig config)
	{
		_database = database;
		_config = config;
	}

	internal async Task<PlayerLoadout> LoadPlayerDataAsync(PlayerInfo? player, WeaponPaintsReloadFlags requestedFlags, CancellationToken cancellationToken = default)
	{
		if (player == null || string.IsNullOrEmpty(player.SteamId) || !ulong.TryParse(player.SteamId, out var steamId64))
		{
			throw new ArgumentException("Invalid player info supplied for loadout reload.", nameof(player));
		}

		var flags = NormalizeLoadFlags(requestedFlags);
		var loadout = new PlayerLoadout(steamId64);

		await EnsureDatabaseReadyAsync();
		await using var connection = await _database.GetConnectionAsync();

		if (flags.HasFlag(WeaponPaintsReloadFlags.Knife) && _config.Additional.KnifeEnabled)
		{
			await LoadKnifeAsync(loadout, player.SteamId, connection, cancellationToken);
			loadout.LoadedFlags |= WeaponPaintsReloadFlags.Knife;
		}

		if (flags.HasFlag(WeaponPaintsReloadFlags.Gloves) && _config.Additional.GloveEnabled)
		{
			await LoadGlovesAsync(loadout, player.SteamId, connection, cancellationToken);
			loadout.LoadedFlags |= WeaponPaintsReloadFlags.Gloves;
		}

		if (flags.HasFlag(WeaponPaintsReloadFlags.Agent) && _config.Additional.AgentEnabled)
		{
			await LoadAgentsAsync(loadout, player.SteamId, connection, cancellationToken);
			loadout.LoadedFlags |= WeaponPaintsReloadFlags.Agent;
		}

		if (flags.HasFlag(WeaponPaintsReloadFlags.Music) && _config.Additional.MusicEnabled)
		{
			await LoadMusicAsync(loadout, player.SteamId, connection, cancellationToken);
			loadout.LoadedFlags |= WeaponPaintsReloadFlags.Music;
		}

		if (flags.HasFlag(WeaponPaintsReloadFlags.Weapons) && _config.Additional.SkinEnabled)
		{
			await LoadWeaponPaintsAsync(loadout, player.SteamId, connection, cancellationToken);
			loadout.LoadedFlags |= WeaponPaintsReloadFlags.Weapons;
		}

		if (flags.HasFlag(WeaponPaintsReloadFlags.Pins) && _config.Additional.PinsEnabled)
		{
			await LoadPinsAsync(loadout, player.SteamId, connection, cancellationToken);
			loadout.LoadedFlags |= WeaponPaintsReloadFlags.Pins;
		}

		return loadout;
	}

	private static WeaponPaintsReloadFlags NormalizeLoadFlags(WeaponPaintsReloadFlags flags)
	{
		if (flags == WeaponPaintsReloadFlags.None)
			return flags;

		if (flags.HasFlag(WeaponPaintsReloadFlags.Knife) || flags.HasFlag(WeaponPaintsReloadFlags.Gloves))
			flags |= WeaponPaintsReloadFlags.Weapons;

		return flags;
	}

	private static async Task LoadKnifeAsync(PlayerLoadout loadout, string steamId, MySqlConnection connection, CancellationToken cancellationToken)
	{
		const string query = "SELECT `knife`, `weapon_team` FROM `wp_player_knife` WHERE `steamid` = @steamid ORDER BY `weapon_team` ASC";
		var rows = await connection.QueryAsync<dynamic>(new CommandDefinition(query, new { steamid = steamId }, cancellationToken: cancellationToken));

		foreach (var row in rows)
		{
			IDictionary<string, object?> rowValues = ToRow(row);
			var knife = GetString(rowValues, "knife");
			if (string.IsNullOrEmpty(knife)) continue;

			AssignTeamValue(loadout.Knives, ToTeam(GetInt(rowValues, "weapon_team")), knife);
		}
	}

	private static async Task LoadGlovesAsync(PlayerLoadout loadout, string steamId, MySqlConnection connection, CancellationToken cancellationToken)
	{
		const string query = "SELECT `weapon_defindex`, `weapon_team` FROM `wp_player_gloves` WHERE `steamid` = @steamid ORDER BY `weapon_team` ASC";
		var rows = await connection.QueryAsync<dynamic>(new CommandDefinition(query, new { steamid = steamId }, cancellationToken: cancellationToken));

		foreach (var row in rows)
		{
			IDictionary<string, object?> rowValues = ToRow(row);
			if (!TryGetUShort(rowValues, "weapon_defindex", out var defIndex) || defIndex == 0) continue;

			AssignTeamValue(loadout.Gloves, ToTeam(GetInt(rowValues, "weapon_team")), defIndex);
		}
	}

	private static async Task LoadAgentsAsync(PlayerLoadout loadout, string steamId, MySqlConnection connection, CancellationToken cancellationToken)
	{
		const string query = "SELECT `agent_ct`, `agent_t` FROM `wp_player_agents` WHERE `steamid` = @steamid";
		var row = await connection.QueryFirstOrDefaultAsync<dynamic>(new CommandDefinition(query, new { steamid = steamId }, cancellationToken: cancellationToken));
		if (row == null) return;

		IDictionary<string, object?> rowValues = ToRow(row);
		loadout.AgentCT = NormalizeNullableString(GetString(rowValues, "agent_ct"));
		loadout.AgentT = NormalizeNullableString(GetString(rowValues, "agent_t"));
	}

	private static async Task LoadMusicAsync(PlayerLoadout loadout, string steamId, MySqlConnection connection, CancellationToken cancellationToken)
	{
		const string query = "SELECT `music_id`, `weapon_team` FROM `wp_player_music` WHERE `steamid` = @steamid ORDER BY `weapon_team` ASC";
		var rows = await connection.QueryAsync<dynamic>(new CommandDefinition(query, new { steamid = steamId }, cancellationToken: cancellationToken));

		foreach (var row in rows)
		{
			IDictionary<string, object?> rowValues = ToRow(row);
			if (!TryGetUShort(rowValues, "music_id", out var musicId)) continue;
			AssignTeamValue(loadout.Music, ToTeam(GetInt(rowValues, "weapon_team")), musicId);
		}
	}

	private static async Task LoadPinsAsync(PlayerLoadout loadout, string steamId, MySqlConnection connection, CancellationToken cancellationToken)
	{
		const string query = "SELECT `id`, `weapon_team` FROM `wp_player_pins` WHERE `steamid` = @steamid ORDER BY `weapon_team` ASC";
		var rows = await connection.QueryAsync<dynamic>(new CommandDefinition(query, new { steamid = steamId }, cancellationToken: cancellationToken));

		foreach (var row in rows)
		{
			IDictionary<string, object?> rowValues = ToRow(row);
			if (!TryGetUShort(rowValues, "id", out var pinId)) continue;
			AssignTeamValue(loadout.Pins, ToTeam(GetInt(rowValues, "weapon_team")), pinId);
		}
	}

	private static async Task LoadWeaponPaintsAsync(PlayerLoadout loadout, string steamId, MySqlConnection connection, CancellationToken cancellationToken)
	{
		const string query = "SELECT * FROM `wp_player_skins` WHERE `steamid` = @steamid ORDER BY `weapon_team` ASC";
		var rows = await connection.QueryAsync<dynamic>(new CommandDefinition(query, new { steamid = steamId }, cancellationToken: cancellationToken));

		foreach (var row in rows)
		{
			IDictionary<string, object?> rowValues = ToRow(row);
			var weaponDefIndex = GetInt(rowValues, "weapon_defindex");
			if (weaponDefIndex <= 0) continue;

			var weaponInfo = new WeaponInfo
			{
				Paint = GetInt(rowValues, "weapon_paint_id"),
				Seed = GetInt(rowValues, "weapon_seed"),
				Wear = GetFloat(rowValues, "weapon_wear"),
				Nametag = GetString(rowValues, "weapon_nametag") ?? string.Empty,
				KeyChain = ParseKeyChain(GetString(rowValues, "weapon_keychain")),
				StatTrak = GetBool(rowValues, "weapon_stattrak"),
				StatTrakCount = GetInt(rowValues, "weapon_stattrak_count")
			};

			for (var i = 0; i <= 4; i++)
			{
				var sticker = ParseSticker(i, GetString(rowValues, $"weapon_sticker_{i}"));
				if (sticker != null)
					weaponInfo.Stickers.Add(sticker);
			}

			AssignWeaponInfo(loadout.Weapons, ToTeam(GetInt(rowValues, "weapon_team")), weaponDefIndex, weaponInfo);
		}
	}

	private static IDictionary<string, object?> ToRow(dynamic row)
	{
		return (IDictionary<string, object?>)row;
	}

	private static CsTeam ToTeam(int weaponTeam)
	{
		return weaponTeam switch
		{
			2 => CsTeam.Terrorist,
			3 => CsTeam.CounterTerrorist,
			_ => CsTeam.None,
		};
	}

	private static void AssignTeamValue<T>(Dictionary<CsTeam, T> target, CsTeam team, T value)
	{
		if (team == CsTeam.None)
		{
			target[CsTeam.Terrorist] = value;
			target[CsTeam.CounterTerrorist] = value;
			return;
		}

		target[team] = value;
	}

	private static void AssignWeaponInfo(Dictionary<CsTeam, Dictionary<int, WeaponInfo>> target, CsTeam team, int weaponDefIndex, WeaponInfo weaponInfo)
	{
		if (team == CsTeam.None)
		{
			GetTeamWeapons(target, CsTeam.Terrorist)[weaponDefIndex] = weaponInfo;
			GetTeamWeapons(target, CsTeam.CounterTerrorist)[weaponDefIndex] = weaponInfo;
			return;
		}

		GetTeamWeapons(target, team)[weaponDefIndex] = weaponInfo;
	}

	private static Dictionary<int, WeaponInfo> GetTeamWeapons(Dictionary<CsTeam, Dictionary<int, WeaponInfo>> target, CsTeam team)
	{
		if (!target.TryGetValue(team, out var weapons))
		{
			weapons = new Dictionary<int, WeaponInfo>();
			target[team] = weapons;
		}

		return weapons;
	}

	private static string? NormalizeNullableString(string? value)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Equals("null", StringComparison.OrdinalIgnoreCase))
			return null;

		return value;
	}

	private static KeyChainInfo? ParseKeyChain(string? value)
	{
		var parts = value?.Split(';', StringSplitOptions.TrimEntries) ?? [];

		if (parts.Length == 5 &&
		    uint.TryParse(parts[0], out var keyChainId) &&
		    keyChainId != 0 &&
		    float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var offsetX) &&
		    float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var offsetY) &&
		    float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var offsetZ) &&
		    uint.TryParse(parts[4], out var seed))
		{
			return new KeyChainInfo
			{
				Id = keyChainId,
				OffsetX = offsetX,
				OffsetY = offsetY,
				OffsetZ = offsetZ,
				Seed = seed
			};
		}

		return null;
	}

	private static StickerInfo? ParseSticker(int slot, string? value)
	{
		var parts = value?.Split(';', StringSplitOptions.TrimEntries) ?? [];
		if (parts.Length != 7 ||
		    !uint.TryParse(parts[0], out var stickerId) ||
		    stickerId == 0 ||
		    !uint.TryParse(parts[1], out var stickerSchema) ||
		    !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var offsetX) ||
		    !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var offsetY) ||
		    !float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var wear) ||
		    !float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) ||
		    !float.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var rotation))
		{
			return null;
		}

		return new StickerInfo
		{
			Slot = slot,
			Id = stickerId,
			Schema = stickerSchema,
			OffsetX = offsetX,
			OffsetY = offsetY,
			Wear = wear,
			Scale = scale,
			Rotation = rotation
		};
	}

	private static string? GetString(IDictionary<string, object?> row, string key)
	{
		return row.TryGetValue(key, out var value) && value != null && value != DBNull.Value ? value.ToString() : null;
	}

	private static int GetInt(IDictionary<string, object?> row, string key)
	{
		if (!row.TryGetValue(key, out var value) || value == null || value == DBNull.Value)
			return 0;

		try
		{
			return Convert.ToInt32(value, CultureInfo.InvariantCulture);
		}
		catch
		{
			return 0;
		}
	}

	private static bool TryGetUShort(IDictionary<string, object?> row, string key, out ushort value)
	{
		var raw = GetInt(row, key);
		if (raw is < 0 or > ushort.MaxValue)
		{
			value = 0;
			return false;
		}

		value = (ushort)raw;
		return true;
	}

	private static float GetFloat(IDictionary<string, object?> row, string key)
	{
		if (!row.TryGetValue(key, out var value) || value == null || value == DBNull.Value)
			return 0f;

		try
		{
			return Convert.ToSingle(value, CultureInfo.InvariantCulture);
		}
		catch
		{
			return 0f;
		}
	}

	private static bool GetBool(IDictionary<string, object?> row, string key)
	{
		if (!row.TryGetValue(key, out var value) || value == null || value == DBNull.Value)
			return false;

		return value switch
		{
			bool boolValue => boolValue,
			byte byteValue => byteValue != 0,
			sbyte sbyteValue => sbyteValue != 0,
			short shortValue => shortValue != 0,
			int intValue => intValue != 0,
			long longValue => longValue != 0,
			_ => bool.TryParse(value.ToString(), out var parsed) && parsed
		};
	}

	private static Task EnsureDatabaseReadyAsync()
	{
		return WeaponPaints.Instance.DatabaseReadyTask;
	}

	internal async Task GetPlayerData(PlayerInfo? player)
	{
		try
		{
			if (player == null || string.IsNullOrEmpty(player.SteamId)) return;

			var loadout = await LoadPlayerDataAsync(player, WeaponPaintsReloadFlags.All);
			WeaponPaints.Instance.LoadoutCache.Store(loadout);
			WeaponPaints.Instance.LoadoutCache.ApplyToLegacySlot(loadout, player.Slot, player.UserId, loadout.LoadedFlags);
		}
		catch (Exception ex)
		{
			// Log the exception or handle it appropriately
			Console.WriteLine($"An error occurred: {ex.Message}");
		}
	}

	private void GetKnifeFromDatabase(PlayerInfo? player, MySqlConnection connection)
	{
		try
		{
			if (!_config.Additional.KnifeEnabled || string.IsNullOrEmpty(player?.SteamId))
				return;

			const string query = "SELECT `knife`, `weapon_team` FROM `wp_player_knife` WHERE `steamid` = @steamid ORDER BY `weapon_team` ASC";
			var rows = connection.Query<dynamic>(query, new { steamid = player.SteamId }); // Retrieve all records for the player

			foreach (var row in rows)
			{
				// Check if knife is null or empty
				if (string.IsNullOrEmpty(row.knife)) continue;

				// Determine the weapon team based on the query result
				CsTeam weaponTeam = (int)row.weapon_team switch
				{
					2 => CsTeam.Terrorist,
					3 => CsTeam.CounterTerrorist,
					_ => CsTeam.None,
				};

				// Get or create entries for the player’s slot
				var playerKnives = WeaponPaints.GPlayersKnife.GetOrAdd(player.Slot, _ => new ConcurrentDictionary<CsTeam, string>());

				if (weaponTeam == CsTeam.None)
				{
					// Assign knife to both teams if weaponTeam is None
					playerKnives[CsTeam.Terrorist] = row.knife;
					playerKnives[CsTeam.CounterTerrorist] = row.knife;
				}
				else
				{
					// Assign knife to the specific team
					playerKnives[weaponTeam] = row.knife;
				}
			}
		}
		catch (Exception ex)
		{
			Utility.Log($"An error occurred in GetKnifeFromDatabase: {ex.Message}");
		}
	}

	private void GetGloveFromDatabase(PlayerInfo? player, MySqlConnection connection)
	{
		try
		{
			if (!_config.Additional.GloveEnabled || string.IsNullOrEmpty(player?.SteamId))
				return;

			const string query = "SELECT `weapon_defindex`, `weapon_team` FROM `wp_player_gloves` WHERE `steamid` = @steamid ORDER BY `weapon_team` ASC";
			var rows = connection.Query<dynamic>(query, new { steamid = player.SteamId }); // Retrieve all records for the player

			foreach (var row in rows)
			{
				// Check if weapon_defindex is null
				if (row.weapon_defindex == null) continue;
				// Determine the weapon team based on the query result
				var playerGloves = WeaponPaints.GPlayersGlove.GetOrAdd(player.Slot, _ => new ConcurrentDictionary<CsTeam, ushort>());
				CsTeam weaponTeam = (int)row.weapon_team switch
				{
					2 => CsTeam.Terrorist,
					3 => CsTeam.CounterTerrorist,
					_ => CsTeam.None,
				};

				// Get or create entries for the player’s slot

				if (weaponTeam == CsTeam.None)
				{
					// Assign glove ID to both teams if weaponTeam is None
					playerGloves[CsTeam.Terrorist] = (ushort)row.weapon_defindex;
					playerGloves[CsTeam.CounterTerrorist] = (ushort)row.weapon_defindex;
				}
				else
				{
					// Assign glove ID to the specific team
					playerGloves[weaponTeam] = (ushort)row.weapon_defindex;
				}
			}
		}
		catch (Exception ex)
		{
			Utility.Log($"An error occurred in GetGlovesFromDatabase: {ex.Message}");
		}
	}

	private void GetAgentFromDatabase(PlayerInfo? player, MySqlConnection connection)
	{
		try
		{
			if (!_config.Additional.AgentEnabled || string.IsNullOrEmpty(player?.SteamId))
				return;

			const string query = "SELECT `agent_ct`, `agent_t` FROM `wp_player_agents` WHERE `steamid` = @steamid";
			var agentData = connection.QueryFirstOrDefault<(string, string)>(query, new { steamid = player.SteamId });

			if (agentData == default) return;
			var agentCT = agentData.Item1;
			var agentT = agentData.Item2;

			if (!string.IsNullOrEmpty(agentCT) || !string.IsNullOrEmpty(agentT))
			{
				WeaponPaints.GPlayersAgent[player.Slot] = (
					agentCT,
					agentT
				);
			}
		}
		catch (Exception ex)
		{
			Utility.Log($"An error occurred in GetAgentFromDatabase: {ex.Message}");
		}
	}

	private void GetWeaponPaintsFromDatabase(PlayerInfo? player, MySqlConnection connection)
	{
		try
		{
			if (!_config.Additional.SkinEnabled || player == null || string.IsNullOrEmpty(player.SteamId))
				return;
				
			var playerWeapons = WeaponPaints.GPlayerWeaponsInfo.GetOrAdd(player.Slot,
				_ => new ConcurrentDictionary<CsTeam, ConcurrentDictionary<int, WeaponInfo>>());

			// var weaponInfos = new ConcurrentDictionary<int, WeaponInfo>();

			const string query = "SELECT * FROM `wp_player_skins` WHERE `steamid` = @steamid ORDER BY `weapon_team` ASC";
			var playerSkins = connection.Query<dynamic>(query, new { steamid = player.SteamId });

			foreach (var row in playerSkins)
			{
				int weaponDefIndex = row.weapon_defindex ?? 0;
				int weaponPaintId = row.weapon_paint_id ?? 0;
				float weaponWear = row.weapon_wear ?? 0f;
				int weaponSeed = row.weapon_seed ?? 0;
				string weaponNameTag = row.weapon_nametag ?? "";
				bool weaponStatTrak = row.weapon_stattrak ?? false;
				int weaponStatTrakCount = row.weapon_stattrak_count ?? 0;
				
				CsTeam weaponTeam = row.weapon_team switch
				{
					2 => CsTeam.Terrorist,
					3 => CsTeam.CounterTerrorist,
					_ => CsTeam.None,
				};
						
				string[]? keyChainParts = row.weapon_keychain?.ToString().Split(';');

				KeyChainInfo keyChainInfo = new KeyChainInfo();

				if (keyChainParts!.Length == 5 &&
				    uint.TryParse(keyChainParts[0], out uint keyChainId) &&
				    float.TryParse(keyChainParts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float keyChainOffsetX) &&
				    float.TryParse(keyChainParts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float keyChainOffsetY) &&
				    float.TryParse(keyChainParts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float keyChainOffsetZ) &&
				    uint.TryParse(keyChainParts[4], out uint keyChainSeed))
				{
					// Successfully parsed the values
					keyChainInfo.Id = keyChainId;
					keyChainInfo.OffsetX = keyChainOffsetX;
					keyChainInfo.OffsetY = keyChainOffsetY;
					keyChainInfo.OffsetZ = keyChainOffsetZ;
					keyChainInfo.Seed = keyChainSeed;
				}
				else
				{
					// Failed to parse the values, default to 0
					keyChainInfo.Id = 0;
					keyChainInfo.OffsetX = 0f;
					keyChainInfo.OffsetY = 0f;
					keyChainInfo.OffsetZ = 0f;
					keyChainInfo.Seed = 0;
				}

				// Create the WeaponInfo object
				WeaponInfo weaponInfo = new WeaponInfo
				{
					Paint = weaponPaintId,
					Seed = weaponSeed,
					Wear = weaponWear,
					Nametag = weaponNameTag,
					KeyChain = keyChainInfo,
					StatTrak = weaponStatTrak,
					StatTrakCount = weaponStatTrakCount,
				};

				// Retrieve and parse sticker data (up to 5 slots)
				for (int i = 0; i <= 4; i++)
				{
					// Access the sticker data dynamically using reflection
					string stickerColumn = $"weapon_sticker_{i}";
					var stickerData = ((IDictionary<string, object>)row!)[stickerColumn]; // Safely cast row to a dictionary

					if (string.IsNullOrEmpty(stickerData.ToString())) continue;
						
					var parts = stickerData.ToString()!.Split(';');

					//"id;schema;x;y;wear;scale;rotation"
					if (parts.Length != 7 ||
					    !uint.TryParse(parts[0], out uint stickerId) ||
					    !uint.TryParse(parts[1], out uint stickerSchema) ||
					    !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float stickerOffsetX) ||
					    !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float stickerOffsetY) ||
					    !float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float stickerWear) ||
					    !float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float stickerScale) ||
					    !float.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out float stickerRotation)) continue;
						
					StickerInfo stickerInfo = new StickerInfo
					{
						Slot = i,
						Id = stickerId,
						Schema = stickerSchema,
						OffsetX = stickerOffsetX,
						OffsetY = stickerOffsetY,
						Wear = stickerWear,
						Scale = stickerScale,
						Rotation = stickerRotation
					};

					weaponInfo.Stickers.Add(stickerInfo);
				}
					
				if (weaponTeam == CsTeam.None)
				{
					// Get or create entries for both teams
					var terroristWeapons = playerWeapons.GetOrAdd(CsTeam.Terrorist, _ => new ConcurrentDictionary<int, WeaponInfo>());
					var counterTerroristWeapons = playerWeapons.GetOrAdd(CsTeam.CounterTerrorist, _ => new ConcurrentDictionary<int, WeaponInfo>());

					// Add weaponInfo to both team weapon dictionaries
					terroristWeapons[weaponDefIndex] = weaponInfo;
					counterTerroristWeapons[weaponDefIndex] = weaponInfo;
				}
				else
				{
					// Add to the specific team
					var teamWeapons = playerWeapons.GetOrAdd(weaponTeam, _ => new ConcurrentDictionary<int, WeaponInfo>());
					teamWeapons[weaponDefIndex] = weaponInfo;
				}

				// weaponInfos[weaponDefIndex] = weaponInfo;
			}

			// WeaponPaints.GPlayerWeaponsInfo[player.Slot][weaponTeam] = weaponInfos;
		}
		catch (Exception ex)
		{
			Utility.Log($"An error occurred in GetWeaponPaintsFromDatabase: {ex.Message}");
		}
	}

	private void GetMusicFromDatabase(PlayerInfo? player, MySqlConnection connection)
	{
		try
		{
			if (!_config.Additional.MusicEnabled || string.IsNullOrEmpty(player?.SteamId))
				return;

			const string query = "SELECT `music_id`, `weapon_team` FROM `wp_player_music` WHERE `steamid` = @steamid ORDER BY `weapon_team` ASC";
			var rows = connection.Query<dynamic>(query, new { steamid = player.SteamId }); // Retrieve all records for the player

			foreach (var row in rows)
			{
				// Check if music_id is null
				if (row.music_id == null) continue;

				// Determine the weapon team based on the query result
				CsTeam weaponTeam = (int)row.weapon_team switch
				{
					2 => CsTeam.Terrorist,
					3 => CsTeam.CounterTerrorist,
					_ => CsTeam.None,
				};

				// Get or create entries for the player’s slot
				var playerMusic = WeaponPaints.GPlayersMusic.GetOrAdd(player.Slot, _ => new ConcurrentDictionary<CsTeam, ushort>());

				if (weaponTeam == CsTeam.None)
				{
					// Assign music ID to both teams if weaponTeam is None
					playerMusic[CsTeam.Terrorist] = (ushort)row.music_id;
					playerMusic[CsTeam.CounterTerrorist] = (ushort)row.music_id;
				}
				else
				{
					// Assign music ID to the specific team
					playerMusic[weaponTeam] = (ushort)row.music_id;
				}
			}
		}
		catch (Exception ex)
		{
			Utility.Log($"An error occurred in GetMusicFromDatabase: {ex.Message}");
		}
	}

	private void GetPinsFromDatabase(PlayerInfo? player, MySqlConnection connection)
	{
		try
		{
			if (string.IsNullOrEmpty(player?.SteamId))
				return;

			const string query = "SELECT `id`, `weapon_team` FROM `wp_player_pins` WHERE `steamid` = @steamid ORDER BY `weapon_team` ASC";
			var rows = connection.Query<dynamic>(query, new { steamid = player.SteamId }); // Retrieve all records for the player

			foreach (var row in rows)
			{
				// Check if id is null
				if (row.id == null) continue;

				// Determine the weapon team based on the query result
				CsTeam weaponTeam = (int)row.weapon_team switch
				{
					2 => CsTeam.Terrorist,
					3 => CsTeam.CounterTerrorist,
					_ => CsTeam.None,
				};

				// Get or create entries for the player’s slot
				var playerPins = WeaponPaints.GPlayersPin.GetOrAdd(player.Slot, _ => new ConcurrentDictionary<CsTeam, ushort>());

				if (weaponTeam == CsTeam.None)
				{
					// Assign pin ID to both teams if weaponTeam is None
					playerPins[CsTeam.Terrorist] = (ushort)row.id;
					playerPins[CsTeam.CounterTerrorist] = (ushort)row.id;
				}
				else
				{
					// Assign pin ID to the specific team
					playerPins[weaponTeam] = (ushort)row.id;
				}
			}
		}
		catch (Exception ex)
		{
			Utility.Log($"An error occurred in GetPinsFromDatabase: {ex.Message}");
		}
	}

	internal async Task SyncKnifeToDatabase(PlayerInfo player, string knife, CsTeam[] teams)
	{
		if (!_config.Additional.KnifeEnabled || string.IsNullOrEmpty(player.SteamId) || string.IsNullOrEmpty(knife) || teams.Length == 0) return;

		const string query = "INSERT INTO `wp_player_knife` (`steamid`, `weapon_team`, `knife`) VALUES(@steamid, @team, @newKnife) ON DUPLICATE KEY UPDATE `knife` = @newKnife";

		try
		{
			await EnsureDatabaseReadyAsync();
			await using var connection = await _database.GetConnectionAsync();
        
			// Loop through each team and insert/update accordingly
			foreach (var team in teams)
			{
				await connection.ExecuteAsync(query, new { steamid = player.SteamId, team, newKnife = knife });
			}
		}
		catch (Exception e)
		{
			Utility.Log($"Error syncing knife to database: {e.Message}");
		}
	}
	
	internal async Task SyncGloveToDatabase(PlayerInfo player, ushort gloveDefIndex, CsTeam[] teams)
	{
		// Check if the necessary conditions are met
		if (!_config.Additional.GloveEnabled || string.IsNullOrEmpty(player.SteamId) || teams.Length == 0) 
			return;

		const string query = @"
        INSERT INTO `wp_player_gloves` (`steamid`, `weapon_team`, `weapon_defindex`) 
        VALUES(@steamid, @team, @gloveDefIndex) 
        ON DUPLICATE KEY UPDATE `weapon_defindex` = @gloveDefIndex";

		try
		{
			await EnsureDatabaseReadyAsync();
			// Get a database connection
			await using var connection = await _database.GetConnectionAsync();
        
			// Loop through each team and insert/update accordingly
			foreach (var team in teams)
			{
				// Execute the SQL command for each team
				await connection.ExecuteAsync(query, new { 
					steamid = player.SteamId, 
					team = (int)team, // Cast the CsTeam enum to int for insertion
					gloveDefIndex 
				});
			}
		}
		catch (Exception e)
		{
			// Log any exceptions that occur
			Utility.Log($"Error syncing glove to database: {e.Message}");
		}
	}

	internal async Task SyncAgentToDatabase(PlayerInfo player)
	{
		if (!WeaponPaints.GPlayersAgent.TryGetValue(player.Slot, out var agents)) return;
		await SyncAgentToDatabase(player, agents.CT, agents.T);
	}

	internal async Task SyncAgentToDatabase(PlayerInfo player, string? agentCt, string? agentT)
	{
		if (!_config.Additional.AgentEnabled || string.IsNullOrEmpty(player.SteamId)) return;

		const string query = """
		                     					INSERT INTO `wp_player_agents` (`steamid`, `agent_ct`, `agent_t`)
		                     					VALUES(@steamid, @agent_ct, @agent_t)
		                     					ON DUPLICATE KEY UPDATE
		                     						`agent_ct` = @agent_ct,
		                     						`agent_t` = @agent_t
		                     """;
		try
		{
			await EnsureDatabaseReadyAsync();
			await using var connection = await _database.GetConnectionAsync();

			await connection.ExecuteAsync(query, new { steamid = player.SteamId, agent_ct = agentCt, agent_t = agentT });
		}
		catch (Exception e)
		{
			Utility.Log($"Error syncing agents to database: {e.Message}");
		}
	}

	internal async Task SyncWeaponPaintsToDatabase(PlayerInfo player)
	{
		if (string.IsNullOrEmpty(player.SteamId) || !WeaponPaints.GPlayerWeaponsInfo.TryGetValue(player.Slot, out var teamWeaponInfos))
			return;

		var snapshot = teamWeaponInfos.ToDictionary(
			team => team.Key,
			team => team.Value.ToDictionary(weapon => weapon.Key, weapon => weapon.Value));

		await SyncWeaponPaintsToDatabase(player, snapshot);
	}

	internal async Task SyncWeaponPaintsToDatabase(PlayerInfo player, IReadOnlyDictionary<CsTeam, Dictionary<int, WeaponInfo>> teamWeaponInfos)
	{
		if (string.IsNullOrEmpty(player.SteamId) || teamWeaponInfos.Count == 0)
			return;

		try
		{
			await EnsureDatabaseReadyAsync();
			await using var connection = await _database.GetConnectionAsync();

			// Loop through each team (Terrorist and CounterTerrorist)
			foreach (var (teamId, weaponsInfo) in teamWeaponInfos)
			{
				foreach (var (weaponDefIndex, weaponInfo) in weaponsInfo)
				{
					var paintId = weaponInfo.Paint;
					var wear = weaponInfo.Wear;
					var seed = weaponInfo.Seed;

					// Prepare the queries to check and update/insert weapon skin data
					const string queryCheckExistence = "SELECT COUNT(*) FROM `wp_player_skins` WHERE `steamid` = @steamid AND `weapon_defindex` = @weaponDefIndex AND `weapon_team` = @weaponTeam";
		                
					var existingRecordCount = await connection.ExecuteScalarAsync<int>(
						queryCheckExistence, 
						new { steamid = player.SteamId, weaponDefIndex, weaponTeam = teamId }
					);

					string query;
					object parameters;

					if (existingRecordCount > 0)
					{
						// Update existing record
						query = "UPDATE `wp_player_skins` SET `weapon_paint_id` = @paintId, `weapon_wear` = @wear, `weapon_seed` = @seed " +
						        "WHERE `steamid` = @steamid AND `weapon_defindex` = @weaponDefIndex AND `weapon_team` = @weaponTeam";
						parameters = new { steamid = player.SteamId, weaponDefIndex, weaponTeam = (int)teamId, paintId, wear, seed };
					}
					else
					{
						// Insert new record
						query = "INSERT INTO `wp_player_skins` (`steamid`, `weapon_defindex`, `weapon_team`, `weapon_paint_id`, `weapon_wear`, `weapon_seed`) " +
						        "VALUES (@steamid, @weaponDefIndex, @weaponTeam, @paintId, @wear, @seed)";
						parameters = new { steamid = player.SteamId, weaponDefIndex, weaponTeam = (int)teamId, paintId, wear, seed };
					}

					await connection.ExecuteAsync(query, parameters);
				}
			}
		}
		catch (Exception e)
		{
			Utility.Log($"Error syncing weapon paints to database: {e.Message}");
		}
	}

	internal async Task SyncMusicToDatabase(PlayerInfo player, ushort music, CsTeam[] teams)
	{
		if (!_config.Additional.MusicEnabled || string.IsNullOrEmpty(player.SteamId)) return;

		const string query = "INSERT INTO `wp_player_music` (`steamid`, `weapon_team`, `music_id`) VALUES(@steamid, @team, @newMusic) ON DUPLICATE KEY UPDATE `music_id` = @newMusic";

		try
		{
			await EnsureDatabaseReadyAsync();
			await using var connection = await _database.GetConnectionAsync();
        
			// Loop through each team and insert/update accordingly
			foreach (var team in teams)
			{
				await connection.ExecuteAsync(query, new { steamid = player.SteamId, team, newMusic = music });
			}
		}
		catch (Exception e)
		{
			Utility.Log($"Error syncing music kit to database: {e.Message}");
		}
	}
		
	internal async Task SyncPinToDatabase(PlayerInfo player, ushort pin, CsTeam[] teams)
	{
		if (!_config.Additional.PinsEnabled || string.IsNullOrEmpty(player.SteamId)) return;

		const string query = "INSERT INTO `wp_player_pins` (`steamid`, `weapon_team`, `id`) VALUES(@steamid, @team, @newPin) ON DUPLICATE KEY UPDATE `id` = @newPin";

		try
		{
			await EnsureDatabaseReadyAsync();
			await using var connection = await _database.GetConnectionAsync();
        
			// Loop through each team and insert/update accordingly
			foreach (var team in teams)
			{
				await connection.ExecuteAsync(query, new { steamid = player.SteamId, team, newPin = pin });
			}
		}
		catch (Exception e)
		{
			Utility.Log($"Error syncing pin to database: {e.Message}");
		}
	}

	internal async Task SyncStatTrakToDatabase(PlayerInfo player, Dictionary<CsTeam, Dictionary<int, (bool StatTrak, int StatTrakCount)>> statTrakSnapshot)
	{
		if (statTrakSnapshot.Count == 0 || string.IsNullOrEmpty(player.SteamId))
			return;

		try
		{
			await EnsureDatabaseReadyAsync();
			await using var connection = await _database.GetConnectionAsync();
			await using var transaction = await connection.BeginTransactionAsync();

			foreach (var (team, weapons) in statTrakSnapshot)
			{
				foreach (var (defindex, statTrakInfo) in weapons)
				{
					const string query = @"
					    UPDATE `wp_player_skins` 
					    SET `weapon_stattrak` = @StatTrak, 
					        `weapon_stattrak_count` = @StatTrakCount
					    WHERE `steamid` = @steamid 
					      AND `weapon_defindex` = @weaponDefIndex
					      AND `weapon_team` = @weaponTeam";

					await connection.ExecuteAsync(query, new
					{
						steamid = player.SteamId,
						weaponDefIndex = defindex,
						StatTrak = statTrakInfo.StatTrak,
						StatTrakCount = statTrakInfo.StatTrakCount,
						weaponTeam = (int)team
					}, transaction);
				}
			}

			await transaction.CommitAsync();
		}
		catch (Exception e)
		{
			Utility.Log($"Error syncing stattrak to database: {e.Message}");
		}
	}

	internal async Task SyncStatTrakToDatabase(PlayerInfo player)
	{
	    if (WeaponPaints.WeaponSync == null || WeaponPaints.GPlayerWeaponsInfo.IsEmpty) return;
	    if (string.IsNullOrEmpty(player.SteamId))
	        return;

	    try
	    {
	        await EnsureDatabaseReadyAsync();
	        await using var connection = await _database.GetConnectionAsync();
	        await using var transaction = await connection.BeginTransactionAsync();

	        // Check if player's slot exists in GPlayerWeaponsInfo
	        if (!WeaponPaints.GPlayerWeaponsInfo.TryGetValue(player.Slot, out var teamWeaponsInfo))
	            return;
	        
	        // Iterate through each team in the player's weapon info
	        foreach (var teamInfo in teamWeaponsInfo)
	        {
	            // Retrieve weaponInfos for the current team
	            var weaponInfos = teamInfo.Value;

	            // Get StatTrak weapons for the current team
	            var statTrakWeapons = weaponInfos
		            .ToDictionary(
			            w => w.Key, 
			            w => (w.Value.StatTrak, w.Value.StatTrakCount) // Store both StatTrak and StatTrakCount in a tuple
		            );

	            // Check if there are StatTrak weapons to sync
	            if (statTrakWeapons.Count == 0) continue;
	            
	            // Get the current team ID
	            int weaponTeam = (int)teamInfo.Key;

	            // Sync StatTrak values for the current team
	            foreach (var (defindex, (statTrak, statTrakCount)) in statTrakWeapons)
	            {
		            const string query = @"
					    UPDATE `wp_player_skins` 
					    SET `weapon_stattrak` = @StatTrak, 
					        `weapon_stattrak_count` = @StatTrakCount
					    WHERE `steamid` = @steamid 
					      AND `weapon_defindex` = @weaponDefIndex
					      AND `weapon_team` = @weaponTeam";

	                var parameters = new
	                {
	                    steamid = player.SteamId,
	                    weaponDefIndex = defindex,
	                    StatTrak = statTrak,
	                    StatTrakCount = statTrakCount,
	                    weaponTeam
	                };

	                await connection.ExecuteAsync(query, parameters, transaction);
	            }
	        }

	        await transaction.CommitAsync();
	    }
	    catch (Exception e)
	    {
	        Utility.Log($"Error syncing stattrak to database: {e.Message}");
	    }
	}
}
