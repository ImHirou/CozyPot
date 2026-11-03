using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CozyPot;

internal sealed class PotMealMeshRef : MultiTextureMeshRef
{
    internal readonly int[] MealStarts;
    internal readonly float Lift;
    internal readonly float CenterX, CenterZ, ScaleX = 1, ScaleZ = 1;

    internal PotMealMeshRef(MultiTextureMeshRef shared, int[] mealStarts, float lift, MealWidening.Widen? widen)
        : base(shared.meshrefs, shared.textureids)
    {
        MealStarts = mealStarts;
        Lift = lift;
        if (widen is not { } w || !MealWidening.ShouldWiden(lift)) return;
        (CenterX, CenterZ, ScaleX, ScaleZ) = (w.CenterX, w.CenterZ, w.ScaleX, w.ScaleZ);
    }
}

internal sealed class PotMealMeshCache : ModSystem
{
    private const float MetalMealScale = 2f;

    private static readonly AccessTools.FieldRef<MultiTextureMeshRef, bool> Disposed =
        AccessTools.FieldRefAccess<MultiTextureMeshRef, bool>("disposed");

    private readonly object sync = new();
    private readonly Dictionary<string, Entry> entries = new();
    private ICoreClientAPI? capi;

    internal static PotMealMeshCache? Instance { get; private set; }

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        Instance = this;
        api.Event.LeaveWorld += Clear;
    }

    public override void Dispose()
    {
        Clear();
        if (Instance == this) Instance = null;
        base.Dispose();
    }

    internal MultiTextureMeshRef? GetOrCreate(ItemStack potStack)
    {
        if (capi == null || potStack.Block is not BlockCookedContainer block) return null;
        return GetOrCreate(block, block.GetCookingRecipe(capi.World, potStack),
            block.GetNonEmptyContents(capi.World, potStack), PotFoodLevel.FoodLift(potStack));
    }

    internal MultiTextureMeshRef? GetOrCreate(BlockCookedContainer block, CookingRecipe? recipe, ItemStack?[]? contents, float lift)
    {
        if (capi == null || contents == null) return null;

        string key = MeshKey(block, recipe, contents);
        lock (sync)
        {
            if (!entries.TryGetValue(key, out var entry))
            {
                if (Environment.CurrentManagedThreadId != RuntimeEnv.MainThreadId)
                {
                    if (Environment.CurrentManagedThreadId == capi.TesselationThreadId)
                        capi.Event.EnqueueMainThreadTask(() => GetOrCreate(block, recipe, contents, lift), "cozypot-mealmesh");
                    throw new RetryTesselationException("CozyPot: pot meal meshes can only be uploaded on the main thread.");
                }
                entry = entries[key] = CreateEntry(block, recipe, contents);
            }

            if (entry.MealStarts == null || Math.Abs(lift) < 1e-5f) return entry.Shared;

            int liftKey = BitConverter.SingleToInt32Bits(lift);
            if (!entry.Levels.TryGetValue(liftKey, out var level))
                entry.Levels[liftKey] = level = new PotMealMeshRef(entry.Shared, entry.MealStarts, lift, entry.Widen);
            return level;
        }
    }

    private Entry CreateEntry(BlockCookedContainer block, CookingRecipe? recipe, ItemStack?[] contents)
    {
        var shape = block.Shape;
        var shapeLoc = shape.Base.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
        capi!.Tesselator.TesselateShape("meal", Shape.TryGet(capi, shapeLoc), out var mesh,
            capi.Tesselator.GetTextureSource(block), new Vec3f(shape.rotateX, shape.rotateY, shape.rotateZ));

        int potFaces = mesh.TextureIndicesCount;
        int potVertices = mesh.VerticesCount;
        var meal = capi.ModLoader.GetModSystem<MealMeshCache>().GenMealMesh(recipe, contents, new(0, block.yoff / 16, 0));

        int[]? mealStarts = null;
        MealWidening.Widen? widen = null;
        if (meal is { VerticesCount: > 0 })
        {
            if (PotFoodLevel.IsMetalPot(block))
                meal.Scale(new Vec3f(0.5f, 0.5f, 0.5f), MetalMealScale, MetalMealScale, MetalMealScale);
            mesh.AddMeshData(meal);
            if (!PotFoodLevel.IsMetalPot(block)) widen = MealWidening.Compute(mesh, potVertices, MealWidening.PerSide);
            mealStarts = MealStarts(mesh, potFaces, potVertices);
            if (mealStarts == null)
                capi.Logger.Warning("[CozyPot] Unexpected mesh layout for {0}, food level will not be lowered.", block.Code);
        }

        return new Entry(capi.Render.UploadMultiTextureMesh(mesh), mealStarts, widen);
    }

    private static int[]? MealStarts(MeshData mesh, int potFaces, int potVertices)
    {
        var starts = new int[mesh.TextureIds.Length];
        if (starts.Length == 1)
        {
            starts[0] = potVertices;
            return starts;
        }

        if (potFaces * mesh.VerticesPerFace != potVertices) return null;
        for (int face = 0; face < potFaces; face++)
            starts[mesh.TextureIndices[face]] += mesh.VerticesPerFace;
        return starts;
    }

    private static string MeshKey(Block block, CookingRecipe? recipe, ItemStack?[] contents)
    {
        var key = new StringBuilder(block.Code.ToShortString()).Append('|').Append(recipe?.Code);
        foreach (var stack in contents)
        {
            if (stack == null) continue;
            if (stack.Collectible.Code.Path == "rot") return block.Code.ToShortString() + "|rotten";
            key.Append('|').Append(stack.Collectible.Code.ToShortString());
        }
        return key.ToString();
    }

    private void Clear()
    {
        lock (sync)
        {
            foreach (var entry in entries.Values)
            {
                entry.Shared.Dispose();
                foreach (var level in entry.Levels.Values) Disposed(level) = true;
            }
            entries.Clear();
        }
    }

    private sealed class Entry(MultiTextureMeshRef shared, int[]? mealStarts, MealWidening.Widen? widen)
    {
        internal readonly MealWidening.Widen? Widen = widen;
        internal readonly MultiTextureMeshRef Shared = shared;
        internal readonly int[]? MealStarts = mealStarts;
        internal readonly Dictionary<int, PotMealMeshRef> Levels = new();
    }
}
