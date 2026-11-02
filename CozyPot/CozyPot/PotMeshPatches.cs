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
        yield return AccessTools.Method(typeof(BlockCookedContainer), nameof(BlockCookedContainer.OnBeforeRender));
        yield return AccessTools.Method(typeof(BlockCookedContainer), nameof(BlockCookedContainer.GenMesh));
        yield return AccessTools.Method(typeof(BlockCookedContainer), nameof(BlockCookedContainer.GetMeshCacheKey));
        yield return AccessTools.Method(typeof(BlockEntityCookedContainer), nameof(BlockEntityCookedContainer.GenMesh));
        yield return AccessTools.Constructor(typeof(PotInFirepitRenderer), new[] { typeof(ICoreClientAPI), typeof(ItemStack), typeof(BlockPos), typeof(bool) });
    }

    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        var vectorConstructor = AccessTools.Constructor(typeof(Vec3f), new[] { typeof(float), typeof(float), typeof(float) });
        bool blockEntity = original.DeclaringType == typeof(BlockEntityCookedContainer);
        bool slot = !blockEntity && (original.Name is nameof(BlockCookedContainer.GenMesh) or nameof(BlockCookedContainer.GetMeshCacheKey));
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

            var loadContext = new CodeInstruction(blockEntity ? OpCodes.Ldarg_0 : slot ? OpCodes.Ldarg_1 : OpCodes.Ldarg_2);
            loadContext.MoveLabelsFrom(instruction);
            loadContext.MoveBlocksFrom(instruction);
            yield return loadContext;
            if (slot) yield return new CodeInstruction(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(ItemSlot), nameof(ItemSlot.Itemstack)));
            yield return new CodeInstruction(OpCodes.Call, helper);
            replacements++;
        }

        if (replacements != 1)
            throw new InvalidOperationException($"CozyPot: expected one food offset in {original}, found {replacements}.");
    }
}

[HarmonyPatch(typeof(MealMeshCache), nameof(MealMeshCache.GetOrCreateMealInContainerMeshRef), typeof(IBlockMealContainer), typeof(ItemStack), typeof(Vec3f))]
internal static class StoredPotMeshPatch
{
    private static void Prefix(ItemStack containerStack, ref Vec3f? foodTranslate)
    {
        if (PotFoodLevel.IsPot(containerStack.Block))
        {
            var offset = foodTranslate ?? new Vec3f(0, ((BlockCookedContainer)containerStack.Block).yoff / 16, 0);
            foodTranslate = PotFoodLevel.ForStack(offset.X, offset.Y, offset.Z, containerStack);
        }
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
        if (__instance.Api is not ICoreClientAPI capi || ___renderer?.contentStackRenderer == null)
            return;

        var stack = __instance.inputStack ?? __instance.outputStack;
        if (!PotFoodLevel.IsPot(stack?.Block)) return;

        if (MetalPotsCompatibility.UpdateFirepitMesh(capi, stack!, ___renderer.contentStackRenderer)) return;
        if (___renderer.contentStackRenderer is not PotInFirepitRenderer potRenderer) return;

        var block = (BlockCookedContainer)stack!.Block;
        var cache = capi.ModLoader.GetModSystem<MealMeshCache>();
        var mesh = cache.GetOrCreateMealInContainerMeshRef(block, stack, new Vec3f(0, block.yoff / 16, 0));
        if (mesh != null) FoodMesh(potRenderer) = mesh;
    }
}
