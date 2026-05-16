namespace WeaponPaints.API;

[Flags]
public enum WeaponPaintsReloadFlags
{
    None = 0,
    Weapons = 1 << 0,
    Knife = 1 << 1,
    Gloves = 1 << 2,
    Agent = 1 << 3,
    Music = 1 << 4,
    Pins = 1 << 5,
    All = Weapons | Knife | Gloves | Agent | Music | Pins
}
