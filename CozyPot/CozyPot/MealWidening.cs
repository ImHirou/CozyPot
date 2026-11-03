using System;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CozyPot;

internal static class MealWidening
{
    internal const float Voxel = 1 / 16f;
    private const float Threshold = 1.5f * Voxel;

    internal const float PerSide = Voxel / 2;

    internal readonly record struct Widen(float CenterX, float CenterZ, float ScaleX, float ScaleZ);

    internal static bool ShouldWiden(float lift) => lift <= -Threshold + 1e-4f;

    internal static Widen? Compute(MeshData mesh, int fromVertex, float by)
    {
        if (fromVertex >= mesh.VerticesCount) return null;

        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        for (int i = fromVertex; i < mesh.VerticesCount; i++)
        {
            float x = mesh.xyz[i * 3], z = mesh.xyz[i * 3 + 2];
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minZ = Math.Min(minZ, z);
            maxZ = Math.Max(maxZ, z);
        }

        float halfX = (maxX - minX) / 2, halfZ = (maxZ - minZ) / 2;
        if (halfX < 1e-4f || halfZ < 1e-4f) return null;
        return new Widen(minX + halfX, minZ + halfZ, (halfX + by) / halfX, (halfZ + by) / halfZ);
    }

    internal static void Apply(MeshData mesh, Widen widen)
    {
        for (int i = 0; i < mesh.VerticesCount; i++)
        {
            mesh.xyz[i * 3] = widen.CenterX + (mesh.xyz[i * 3] - widen.CenterX) * widen.ScaleX;
            mesh.xyz[i * 3 + 2] = widen.CenterZ + (mesh.xyz[i * 3 + 2] - widen.CenterZ) * widen.ScaleZ;
        }
    }
}

[HarmonyPatch]
internal static class BakedMealWideningPatch
{
    [ThreadStatic] private static Block? _pot;
    [ThreadStatic] private static Vec3f? _foodTranslate;

    [HarmonyPatch(typeof(MealMeshCache), nameof(MealMeshCache.GenMealInContainerMesh))]
    [HarmonyPrefix]
    private static void Remember(Block containerBlock, Vec3f? foodTranslate)
    {
        _pot = containerBlock;
        _foodTranslate = foodTranslate;
    }

    [HarmonyPatch(typeof(MealMeshCache), nameof(MealMeshCache.GenMealInContainerMesh))]
    [HarmonyFinalizer]
    private static void Forget()
    {
        _pot = null;
        _foodTranslate = null;
    }

    [HarmonyPatch(typeof(MealMeshCache), nameof(MealMeshCache.GenMealMesh))]
    [HarmonyPostfix]
    private static void WidenMeal(MeshData? __result)
    {
        if (__result == null || _foodTranslate == null || _pot is not BlockCookedContainer pot
            || !PotFoodLevel.IsPot(pot) || PotFoodLevel.IsMetalPot(pot)) return;
        _pot = null;

        if (!MealWidening.ShouldWiden(_foodTranslate.Y - pot.yoff / 16)) return;
        if (MealWidening.Compute(__result, 0, MealWidening.PerSide) is { } widen)
            MealWidening.Apply(__result, widen);
    }
}
