using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CozyPot;

internal static class MetalPotsCompatibility
{
    private static FieldInfo? _foodMesh;

    internal static void Apply(Harmony harmony, ICoreClientAPI api)
    {
        Reset();
        if (!api.ModLoader.IsModEnabled("metalpots")) return;

        var blockType = RequireType("MetalPots.Blocks.MPBlockCookedContainer");
        var cacheType = RequireType("MetalPots.Systems.MPMealMeshCache");
        var rendererType = RequireType("MetalPots.BlockEntityRenderer.MetalPotInFirepitRenderer");
        var foodMesh = AccessTools.Field(rendererType, "potWithFoodRef")
            ?? throw new MissingFieldException(rendererType.FullName, "potWithFoodRef");
        var getMesh = AccessTools.Method(cacheType, "GetOrCreateMealInContainerMeshRef",
            new[] { typeof(Block), typeof(CookingRecipe), typeof(ItemStack[]), typeof(Vec3f), typeof(ItemStack) })
            ?? throw new MissingMethodException(cacheType.FullName, "GetOrCreateMealInContainerMeshRef");

        harmony.Patch(getMesh, prefix: new HarmonyMethod(typeof(MetalPotsCompatibility), nameof(UseSharedMesh)));
        foreach (var name in new[] { nameof(BlockCookedContainer.GenMesh), nameof(BlockCookedContainer.GetMeshCacheKey) })
        {
            var method = AccessTools.DeclaredMethod(blockType, name)
                ?? throw new MissingMethodException(blockType.FullName, name);
            harmony.Patch(method, transpiler: new HarmonyMethod(typeof(PotTranslationPatch), nameof(PotTranslationPatch.Transpiler)));
        }

        var constructor = AccessTools.Constructor(rendererType,
            new[] { typeof(ICoreClientAPI), typeof(ItemStack), typeof(BlockPos), typeof(bool) })
            ?? throw new MissingMethodException(rendererType.FullName, ".ctor");
        harmony.Patch(constructor, prefix: new HarmonyMethod(typeof(CookedPotInFirepitPatch), nameof(CookedPotInFirepitPatch.Prefix)));
        _foodMesh = foodMesh;
        api.Logger.Notification("[CozyPot] Metal Pots food levels enabled.");
    }

    private static Type RequireType(string name) => AccessTools.TypeByName(name)
        ?? throw new TypeLoadException($"CozyPot: Metal Pots type {name} was not found.");

    private static bool UseSharedMesh(ItemStack potStack, ref MultiTextureMeshRef? __result)
    {
        if (!PotFoodLevel.IsMetalPot(potStack?.Block) || PotMealMeshCache.Instance is not { } cache) return true;
        __result = cache.GetOrCreate(potStack!);
        return false;
    }

    internal static void SetFirepitMesh(IInFirepitRenderer renderer, MultiTextureMeshRef mesh)
    {
        if (_foodMesh?.DeclaringType?.IsInstanceOfType(renderer) == true) _foodMesh.SetValue(renderer, mesh);
    }

    internal static void Reset() => _foodMesh = null;
}
