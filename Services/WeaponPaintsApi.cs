using WeaponPaints.API;

namespace WeaponPaints.Services;

internal sealed class WeaponPaintsApi(LoadoutReloadService reloadService) : IWeaponPaintsApi
{
    public Task<WeaponPaintsReloadResult> ReloadPlayerAsync(
        ulong steamId64,
        WeaponPaintsReloadFlags flags = WeaponPaintsReloadFlags.All,
        CancellationToken cancellationToken = default)
    {
        return reloadService.ReloadPlayerAsync(steamId64, flags, cancellationToken);
    }

    public Task<IReadOnlyList<WeaponPaintsReloadResult>> ReloadOnlinePlayersAsync(
        WeaponPaintsReloadFlags flags = WeaponPaintsReloadFlags.All,
        CancellationToken cancellationToken = default)
    {
        return reloadService.ReloadOnlinePlayersAsync(flags, cancellationToken);
    }

    public bool IsPlayerOnline(ulong steamId64)
    {
        return reloadService.IsPlayerOnline(steamId64);
    }
}
