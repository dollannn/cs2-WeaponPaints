namespace WeaponPaints.API;

public sealed record WeaponPaintsReloadResult(
    ulong SteamId64,
    WeaponPaintsReloadStatus Status,
    WeaponPaintsReloadFlags RequestedFlags,
    WeaponPaintsReloadFlags ReloadedFlags,
    bool Applied,
    string? Message = null)
{
    public bool Success => Status == WeaponPaintsReloadStatus.Success && Applied;
    public bool Loaded => Status is WeaponPaintsReloadStatus.Success or WeaponPaintsReloadStatus.LoadedButNotApplied;
}
