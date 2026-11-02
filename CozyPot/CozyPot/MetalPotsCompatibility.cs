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
    private const string CacheTypeName = "MetalPots.Systems.MPMealMeshCache";
    private static FieldInfo? _foodMesh;
    private static MethodInfo? _getMesh;

    internal static void Apply(Harmony harmony, ICoreClientAPI api)
    {
        Reset();
        if (!api.ModLoader.IsModEnabled("metalpots")) return;

        var blockType = RequireType("MetalPots.Blocks.MPBlockCookedContainer");
        var cacheType = RequireType(CacheTypeName);
        var rendererType = RequireType("MetalPots.BlockEntityRenderer.MetalPotInFirepitRenderer");
        _foodMesh = AccessTools.Field(rendererType, "potWithFoodRef")
            ?? throw new MissingFieldException(rendererType.FullName, "potWithFoodRef");
        _getMesh = AccessTools.Method(cacheType, "GetOrCreateMealInContainerMeshRef",
            new[] { typeof(Block), typeof(CookingRecipe), typeof(ItemStack[]), typeof(Vec3f), typeof(ItemStack) })
            ?? throw new MissingMethodException(cacheType.FullName, "GetOrCreateMealInContainerMeshRef");

        harmony.Patch(_getMesh, prefix: new HarmonyMethod(typeof(MetalPotsCompatibility), nameof(AdjustFoodTranslation)));
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
        api.Logger.Notification("[CozyPot] Metal Pots food levels enabled.");
    }

    private static Type RequireType(string name) => AccessTools.TypeByName(name)
        ?? throw new TypeLoadException($"CozyPot: Metal Pots type {name} was not found.");

    private static void AdjustFoodTranslation(ItemStack potStack, ref Vec3f foodTranslate)
    {
        if (PotFoodLevel.IsMetalPot(potStack?.Block))
        {
            var offset = foodTranslate ?? new Vec3f(0, ((BlockCookedContainer)potStack!.Block).yoff / 16, 0);
            foodTranslate = PotFoodLevel.ForMetalStack(offset.X, offset.Y, offset.Z, potStack);
        }
    }

    internal static bool UpdateFirepitMesh(ICoreClientAPI api, ItemStack stack, IInFirepitRenderer renderer)
    {
        if (_foodMesh?.DeclaringType?.IsInstanceOfType(renderer) != true || _getMesh == null) return false;

        var block = (BlockCookedContainer)stack.Block;
        var cache = api.ModLoader.GetModSystem(CacheTypeName);
        var mesh = _getMesh.Invoke(cache, new object?[]
        {
            block, block.GetCookingRecipe(api.World, stack), block.GetNonEmptyContents(api.World, stack),
            new Vec3f(0, block.yoff / 16, 0), stack
        });
        if (mesh != null) _foodMesh.SetValue(renderer, mesh);
        return true;
    }

    internal static void Reset()
    {
        _foodMesh = null;
        _getMesh = null;
    }
}
