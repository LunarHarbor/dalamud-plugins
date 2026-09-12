namespace DeepDungeonTracker;

public static class ServiceUtility
{
    public static bool IsSolo => Service.PartyList.Length <= 1;

    public static int PartySize => System.Math.Max(1, Service.PartyList.Length);

    public static bool IsSupportedParty => PartySize <= 4;

    public static string ConfigDirectory => System.IO.Path.Combine(Service.PluginInterface.ConfigDirectory.FullName, "PilgrimDuo");
}
