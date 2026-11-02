using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace CozyPot;

public class CozyPotModSystem : ModSystem
{
    private const string HarmonyId = "cozypot.client.foodlevel";
    private Harmony? harmony;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        harmony = new Harmony(HarmonyId);
        try
        {
            harmony.PatchAll(typeof(CozyPotModSystem).Assembly);
            api.Logger.Notification("[CozyPot] Client pot food levels enabled.");
        }
        catch
        {
            harmony.UnpatchAll(HarmonyId);
            throw;
        }
    }

    public override void Dispose()
    {
        harmony?.UnpatchAll(HarmonyId);
        harmony = null;
        base.Dispose();
    }
}
