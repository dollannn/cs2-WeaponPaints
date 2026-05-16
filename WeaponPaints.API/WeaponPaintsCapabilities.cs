using CounterStrikeSharp.API.Core.Capabilities;

namespace WeaponPaints.API;

public static class WeaponPaintsCapabilities
{
    public const string CapabilityName = "weaponpaints:api";

    public static PluginCapability<IWeaponPaintsApi> Capability { get; } = new(CapabilityName);

    public static IWeaponPaintsApi? TryGet()
    {
        try
        {
            return Capability.Get();
        }
        catch
        {
            return null;
        }
    }
}
