namespace WeaponPaints.API;

public interface IWeaponPaintsApi
{
    Task<WeaponPaintsReloadResult> ReloadPlayerAsync(
        ulong steamId64,
        WeaponPaintsReloadFlags flags = WeaponPaintsReloadFlags.All,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WeaponPaintsReloadResult>> ReloadOnlinePlayersAsync(
        WeaponPaintsReloadFlags flags = WeaponPaintsReloadFlags.All,
        CancellationToken cancellationToken = default);

    bool IsPlayerOnline(ulong steamId64);
}
