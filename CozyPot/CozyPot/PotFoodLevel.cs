using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CozyPot;

internal static class PotFoodLevel
{
    internal static bool IsMetalPot(Block? block) => block?.Code is { Domain: "metalpots" } code
        && (code.PathStartsWith("metalpot-") || code.PathStartsWith("dirtymetalpot-"))
        && code.Path.EndsWith("-cooked", StringComparison.Ordinal);

    internal static bool IsPot(Block? block) => block is BlockCookedContainer && block.Code != null
        && ((block.Code.PathStartsWith("claypot-") && block.Code.Path.EndsWith("-cooked", StringComparison.Ordinal))
            || IsMetalPot(block));

    private static float FillRatio(float servings, float capacity)
    {
        if (capacity <= 0) return 0;
        return Math.Clamp(servings / capacity, 0, 1);
    }

    internal static Vec3f ForStack(float x, float y, float z, ItemStack? stack)
    {
        if (IsPot(stack?.Block))
            y *= FillRatio(stack!.Attributes.GetFloat("quantityServings"), stack.Block.Attributes?["servingCapacity"].AsFloat(6) ?? 6);
        return new Vec3f(x, y, z);
    }

    internal static Vec3f ForMetalStack(float x, float y, float z, ItemStack? stack)
    {
        if (IsMetalPot(stack?.Block))
        {
            const float bottomOffset = 2.5f / 16;
            float ratio = FillRatio(stack!.Attributes.GetFloat("quantityServings"), stack.Block.Attributes?["servingCapacity"].AsFloat(24) ?? 24);
            y = bottomOffset + (y - bottomOffset) * ratio;
        }
        return new Vec3f(x, y, z);
    }

    internal static Vec3f ForBlockEntity(float x, float y, float z, BlockEntityCookedContainer container)
    {
        if (container.Api?.Side == EnumAppSide.Client && IsPot(container.Block))
            y *= FillRatio(container.QuantityServings, container.Block.Attributes?["servingCapacity"].AsFloat(6) ?? 6);
        return new Vec3f(x, y, z);
    }
}
