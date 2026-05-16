namespace WeaponPaints.API;

public static class WeaponPaintsApiProvider
{
    private static IWeaponPaintsApi? _current;

    public static IWeaponPaintsApi Api { get; } = new ForwardingWeaponPaintsApi();

    public static void SetCurrent(IWeaponPaintsApi? current)
    {
        Volatile.Write(ref _current, current);
    }

    private sealed class ForwardingWeaponPaintsApi : IWeaponPaintsApi
    {
        public Task<WeaponPaintsReloadResult> ReloadPlayerAsync(
            ulong steamId64,
            WeaponPaintsReloadFlags flags = WeaponPaintsReloadFlags.All,
            CancellationToken cancellationToken = default)
        {
            var current = Volatile.Read(ref _current);
            return current?.ReloadPlayerAsync(steamId64, flags, cancellationToken) ??
                   Task.FromResult(NotReady(steamId64, flags));
        }

        public Task<IReadOnlyList<WeaponPaintsReloadResult>> ReloadOnlinePlayersAsync(
            WeaponPaintsReloadFlags flags = WeaponPaintsReloadFlags.All,
            CancellationToken cancellationToken = default)
        {
            var current = Volatile.Read(ref _current);
            return current?.ReloadOnlinePlayersAsync(flags, cancellationToken) ??
                   Task.FromResult<IReadOnlyList<WeaponPaintsReloadResult>>([]);
        }

        public bool IsPlayerOnline(ulong steamId64)
        {
            return Volatile.Read(ref _current)?.IsPlayerOnline(steamId64) ?? false;
        }

        private static WeaponPaintsReloadResult NotReady(ulong steamId64, WeaponPaintsReloadFlags flags)
        {
            return new WeaponPaintsReloadResult(
                steamId64,
                WeaponPaintsReloadStatus.PluginNotReady,
                flags,
                WeaponPaintsReloadFlags.None,
                false,
                "WeaponPaints is not ready.");
        }
    }
}
