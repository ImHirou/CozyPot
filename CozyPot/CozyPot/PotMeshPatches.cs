using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CozyPot;

[HarmonyPatch]
internal static class PotTranslationPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(BlockCookedContainer), nameof(BlockCookedContainer.GenMesh));
        yield return AccessTools.Method(typeof(BlockCookedContainer), nameof(BlockCookedContainer.GetMeshCacheKey));
        yield return AccessTools.Method(typeof(BlockEntityCookedContainer), nameof(BlockEntityCookedContainer.GenMesh));
    }

    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        var vectorConstructor = AccessTools.Constructor(typeof(Vec3f), new[] { typeof(float), typeof(float), typeof(float) });
        bool blockEntity = original.DeclaringType == typeof(BlockEntityCookedContainer);
        bool metalPot = original.DeclaringType?.FullName == "MetalPots.Blocks.MPBlockCookedContainer";
        var helper = AccessTools.Method(typeof(PotFoodLevel), blockEntity ? nameof(PotFoodLevel.ForBlockEntity)
            : metalPot ? nameof(PotFoodLevel.ForMetalStack) : nameof(PotFoodLevel.ForStack));
        int replacements = 0;

        foreach (var instruction in instructions)
        {
            if (instruction.opcode != OpCodes.Newobj || !Equals(instruction.operand, vectorConstructor))
            {
                yield return instruction;
                continue;
            }

            var loadContext = new CodeInstruction(blockEntity ? OpCodes.Ldarg_0 : OpCodes.Ldarg_1);
            loadContext.MoveLabelsFrom(instruction);
            loadContext.MoveBlocksFrom(instruction);
            yield return loadContext;
            if (!blockEntity) yield return new CodeInstruction(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(ItemSlot), nameof(ItemSlot.Itemstack)));
            yield return new CodeInstruction(OpCodes.Call, helper);
            replacements++;
        }

        if (replacements != 1)
            throw new InvalidOperationException($"CozyPot: expected one food offset in {original}, found {replacements}.");
    }
}

[HarmonyPatch(typeof(BlockCookedContainer), nameof(BlockCookedContainer.OnBeforeRender))]
internal static class PotItemRenderPatch
{
    private static bool Prefix(ItemStack itemstack, ref ItemRenderInfo renderinfo)
    {
        if (!PotFoodLevel.IsPot(itemstack.Block) || PotMealMeshCache.Instance is not { } cache) return true;
        var mesh = cache.GetOrCreate(itemstack);
        if (mesh != null) renderinfo.ModelRef = mesh;
        return false;
    }
}

[HarmonyPatch(typeof(MealMeshCache), nameof(MealMeshCache.GetOrCreateMealInContainerMeshRef), typeof(IBlockMealContainer), typeof(ItemStack), typeof(Vec3f))]
internal static class StackMealMeshPatch
{
    private static bool Prefix(ItemStack containerStack, ref MultiTextureMeshRef? __result)
    {
        if (!PotFoodLevel.IsPot(containerStack?.Block) || PotMealMeshCache.Instance is not { } cache) return true;
        __result = cache.GetOrCreate(containerStack!);
        return false;
    }
}

[HarmonyPatch(typeof(MealMeshCache), nameof(MealMeshCache.GetOrCreateMealInContainerMeshRef), typeof(Block), typeof(CookingRecipe), typeof(ItemStack[]), typeof(Vec3f))]
internal static class RecipeMealMeshPatch
{
    private static bool Prefix(Block containerBlock, CookingRecipe? forRecipe, ItemStack?[]? contentStacks, ref MultiTextureMeshRef? __result)
    {
        if (!PotFoodLevel.IsPot(containerBlock) || PotMealMeshCache.Instance is not { } cache) return true;
        __result = cache.GetOrCreate((BlockCookedContainer)containerBlock, forRecipe, contentStacks, 0);
        return false;
    }
}

[HarmonyPatch(typeof(BlockEntityCookedContainer), nameof(BlockEntityCookedContainer.FromTreeAttributes))]
internal static class PlacedPotSyncPatch
{
    private static void Prefix(BlockEntityCookedContainer __instance, ref MeshData? ___currentMesh)
    {
        if (__instance.Api?.Side == EnumAppSide.Client && PotFoodLevel.IsPot(__instance.Block)) ___currentMesh = null;
    }
}

[HarmonyPatch(typeof(PotInFirepitRenderer), MethodType.Constructor, typeof(ICoreClientAPI), typeof(ItemStack), typeof(BlockPos), typeof(bool) )]
internal static class CookedPotInFirepitPatch
{
    internal static void Prefix(ItemStack stack, ref bool isInOutputSlot)
    {
        if (PotFoodLevel.IsPot(stack.Block)) isInOutputSlot = true;
    }
}

[HarmonyPatch(typeof(BlockEntityFirepit), "UpdateRenderer")]
internal static class FirepitPotSyncPatch
{
    private static readonly AccessTools.FieldRef<PotInFirepitRenderer, MultiTextureMeshRef> FoodMesh =
        AccessTools.FieldRefAccess<PotInFirepitRenderer, MultiTextureMeshRef>("potWithFoodRef");

    private static void Postfix(BlockEntityFirepit __instance, FirepitContentsRenderer? ___renderer)
    {
        if (___renderer?.contentStackRenderer is not { } renderer || PotMealMeshCache.Instance is not { } cache) return;

        var stack = __instance.inputStack ?? __instance.outputStack;
        if (!PotFoodLevel.IsPot(stack?.Block) || cache.GetOrCreate(stack!) is not { } mesh) return;

        if (renderer is PotInFirepitRenderer potRenderer) FoodMesh(potRenderer) = mesh;
        else MetalPotsCompatibility.SetFirepitMesh(renderer, mesh);
    }
}
