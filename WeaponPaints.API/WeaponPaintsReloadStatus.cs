namespace WeaponPaints.API;

public enum WeaponPaintsReloadStatus
{
    Success = 0,
    LoadedButNotApplied = 1,
    PlayerNotOnline = 2,
    InvalidPlayer = 3,
    PluginNotReady = 4,
    Failed = 5
}
