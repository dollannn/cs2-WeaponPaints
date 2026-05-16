using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;
using WeaponPaints.API;
using WeaponPaints.Models;

namespace WeaponPaints.Services;

internal sealed class LoadoutReloadService(global::WeaponPaints.WeaponPaints plugin, PlayerLoadoutCache cache)
{
    private readonly Dictionary<ulong, SemaphoreSlim> _playerReloadLocks = new();
    private readonly object _playerReloadLocksLock = new();
    private readonly Dictionary<ulong, OnlineConnection> _onlinePlayers = new();
    private readonly object _onlineSlotsLock = new();
    private int _generation;

    public void InvalidateActiveReloads()
    {
        Interlocked.Increment(ref _generation);
    }

    public void RegisterOnlinePlayer(CCSPlayerController player)
    {
        if (!IsUsablePlayer(player)) return;

        lock (_onlineSlotsLock)
        {
            _onlinePlayers[player.SteamID] = new OnlineConnection(player.Slot, player.UserId);
        }

        cache.BindSlot(player.SteamID, player.Slot, player.UserId);
    }

    public void UnregisterOnlinePlayer(CCSPlayerController player)
    {
        if (player == null || player.IsBot) return;

        UnregisterOnlinePlayer(player.SteamID, player.Slot, player.UserId);
    }

    public void UnregisterOnlinePlayer(ulong steamId64, int slot, int? userId = null)
    {
        if (steamId64 == 0) return;

        var removed = false;

        lock (_onlineSlotsLock)
        {
            if (_onlinePlayers.TryGetValue(steamId64, out var connection) &&
                connection.Slot == slot &&
                userId.HasValue &&
                connection.UserId.HasValue &&
                connection.UserId.Value == userId.Value)
            {
                _onlinePlayers.Remove(steamId64);
                removed = true;
            }
        }

        if (!removed)
            return;

        cache.RemoveSlot(slot, steamId64, userId);
        cache.RemovePlayer(steamId64);
    }

    public bool IsPlayerOnline(ulong steamId64)
    {
        lock (_onlineSlotsLock)
        {
            return _onlinePlayers.ContainsKey(steamId64);
        }
    }

    public async Task<WeaponPaintsReloadResult> ReloadPlayerAsync(
        ulong steamId64,
        WeaponPaintsReloadFlags flags = WeaponPaintsReloadFlags.All,
        CancellationToken cancellationToken = default)
    {
        if (steamId64 == 0)
        {
            return new WeaponPaintsReloadResult(steamId64, WeaponPaintsReloadStatus.InvalidPlayer, flags, WeaponPaintsReloadFlags.None, false, "Invalid SteamID64.");
        }

        if (flags == WeaponPaintsReloadFlags.None)
        {
            return new WeaponPaintsReloadResult(steamId64, WeaponPaintsReloadStatus.Success, flags, WeaponPaintsReloadFlags.None, false, "No reload flags requested.");
        }

        var generation = Volatile.Read(ref _generation);
        var playerSnapshot = await GetOnlinePlayerSnapshotAsync(steamId64, cancellationToken);
        if (playerSnapshot == null)
        {
            return new WeaponPaintsReloadResult(steamId64, WeaponPaintsReloadStatus.PlayerNotOnline, flags, WeaponPaintsReloadFlags.None, false, "Player is not online.");
        }

        var weaponSync = global::WeaponPaints.WeaponPaints.WeaponSync;
        if (weaponSync == null)
        {
            return new WeaponPaintsReloadResult(steamId64, WeaponPaintsReloadStatus.PluginNotReady, flags, WeaponPaintsReloadFlags.None, false, "WeaponPaints is not ready.");
        }

        var reloadLock = GetReloadLock(steamId64);
        await reloadLock.WaitAsync(cancellationToken);

        try
        {
            await plugin.DatabaseReadyTask.WaitAsync(cancellationToken);

            var playerInfo = new PlayerInfo
            {
                UserId = playerSnapshot.UserId,
                Slot = playerSnapshot.Slot,
                Index = playerSnapshot.Index,
                SteamId = steamId64.ToString(),
                Name = playerSnapshot.Name,
                IpAddress = playerSnapshot.IpAddress
            };

            var loadout = await weaponSync.LoadPlayerDataAsync(playerInfo, flags, cancellationToken);

            if (Volatile.Read(ref _generation) != generation)
            {
                return new WeaponPaintsReloadResult(
                    steamId64,
                    WeaponPaintsReloadStatus.LoadedButNotApplied,
                    flags,
                    loadout.LoadedFlags,
                    false,
                    "Reload was invalidated by map change or plugin reload.");
            }

            return await ApplyLoadedLoadoutAsync(playerSnapshot, loadout, flags, generation, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            plugin.Logger.LogError(ex, "Failed to reload WeaponPaints loadout for {SteamId64}", steamId64);
            return new WeaponPaintsReloadResult(steamId64, WeaponPaintsReloadStatus.Failed, flags, WeaponPaintsReloadFlags.None, false, ex.Message);
        }
        finally
        {
            reloadLock.Release();
        }
    }

    public async Task<IReadOnlyList<WeaponPaintsReloadResult>> ReloadOnlinePlayersAsync(
        WeaponPaintsReloadFlags flags = WeaponPaintsReloadFlags.All,
        CancellationToken cancellationToken = default)
    {
        ulong[] steamIds;
        lock (_onlineSlotsLock)
        {
            steamIds = _onlinePlayers.Keys.ToArray();
        }

        var results = new List<WeaponPaintsReloadResult>(steamIds.Length);
        foreach (var steamId in steamIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ReloadPlayerAsync(steamId, flags, cancellationToken));
        }

        return results;
    }

    private async Task<OnlinePlayerSnapshot?> GetOnlinePlayerSnapshotAsync(ulong steamId64, CancellationToken cancellationToken)
    {
        return await RunOnNextFrameAsync(() =>
        {
            var player = FindOnlinePlayer(steamId64);
            if (player == null)
            {
                lock (_onlineSlotsLock)
                {
                    _onlinePlayers.Remove(steamId64);
                }

                return null;
            }

            RegisterOnlinePlayer(player);

            return new OnlinePlayerSnapshot(
                player.SteamID,
                player.Slot,
                player.UserId,
                (int)player.Index,
                player.PlayerName,
                player.IpAddress?.Split(':')[0]);
        }, cancellationToken);
    }

    private async Task<WeaponPaintsReloadResult> ApplyLoadedLoadoutAsync(
        OnlinePlayerSnapshot originalSnapshot,
        PlayerLoadout loadout,
        WeaponPaintsReloadFlags requestedFlags,
        int generation,
        CancellationToken cancellationToken)
    {
        return await RunOnNextFrameAsync(() =>
        {
            if (Volatile.Read(ref _generation) != generation)
            {
                return new WeaponPaintsReloadResult(
                    originalSnapshot.SteamId64,
                    WeaponPaintsReloadStatus.LoadedButNotApplied,
                    requestedFlags,
                    loadout.LoadedFlags,
                    false,
                    "Reload was invalidated by map change or plugin reload.");
            }

            var player = FindOnlinePlayer(originalSnapshot.SteamId64);
            if (player == null || player.UserId != originalSnapshot.UserId)
            {
                cache.RemoveSlot(originalSnapshot.Slot, originalSnapshot.SteamId64, originalSnapshot.UserId);
                return new WeaponPaintsReloadResult(
                    originalSnapshot.SteamId64,
                    WeaponPaintsReloadStatus.LoadedButNotApplied,
                    requestedFlags,
                    loadout.LoadedFlags,
                    false,
                    "Player disconnected before loadout could be applied.");
            }

            cache.Store(loadout);
            cache.ApplyToLegacySlot(loadout, player.Slot, player.UserId, loadout.LoadedFlags);
            var applied = plugin.ApplyLoadedLoadout(player, loadout.LoadedFlags);

            return new WeaponPaintsReloadResult(
                originalSnapshot.SteamId64,
                applied ? WeaponPaintsReloadStatus.Success : WeaponPaintsReloadStatus.LoadedButNotApplied,
                requestedFlags,
                loadout.LoadedFlags,
                applied,
                applied ? null : "Loadout was cached and will apply on the next eligible game event.");
        }, cancellationToken);
    }

    private SemaphoreSlim GetReloadLock(ulong steamId64)
    {
        lock (_playerReloadLocksLock)
        {
            if (!_playerReloadLocks.TryGetValue(steamId64, out var reloadLock))
            {
                reloadLock = new SemaphoreSlim(1, 1);
                _playerReloadLocks[steamId64] = reloadLock;
            }

            return reloadLock;
        }
    }

    private static CCSPlayerController? FindOnlinePlayer(ulong steamId64)
    {
        return Utilities.GetPlayers().FirstOrDefault(player =>
            IsUsablePlayer(player) && player.SteamID == steamId64);
    }

    private static bool IsUsablePlayer(CCSPlayerController? player)
    {
        return player is { IsValid: true, IsBot: false, IsHLTV: false, UserId: not null, Connected: PlayerConnectedState.Connected };
    }

    private static Task<T> RunOnNextFrameAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (cancellationToken.IsCancellationRequested)
        {
            completion.SetCanceled(cancellationToken);
            return completion.Task;
        }

        var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        completion.Task.ContinueWith(_ => registration.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        Server.NextFrame(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
                return;
            }

            try
            {
                completion.TrySetResult(action());
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        return completion.Task;
    }

    private sealed record OnlineConnection(int Slot, int? UserId);

    private sealed record OnlinePlayerSnapshot(
        ulong SteamId64,
        int Slot,
        int? UserId,
        int Index,
        string? Name,
        string? IpAddress);
}
