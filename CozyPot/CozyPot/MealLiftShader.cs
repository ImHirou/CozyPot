using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace CozyPot;

[HarmonyPatch(typeof(ShaderRegistry), "LoadShaderProgram")]
internal static class MealLiftShader
{
    internal const string UniformName = "cozypotMealLift";
    internal const string CenterUniformName = "cozypotMealCenter";

    private static readonly HashSet<string> Passes = new() { "standard", "gui", "entityanimated", "helditem", "shadowmapentityanimated" };

    private static readonly Regex PositionInput =
        new(@"layout\s*\(\s*location\s*=\s*0\s*\)\s*in\s+vec3\s+vertexPositionIn\s*;");

    private static readonly Regex MainStart = new(@"void\s+main\s*\(\s*(void)?\s*\)\s*\{");

    private const string Declarations = """
        layout(location = 0) in vec3 cozypotVertexPositionIn;
        uniform vec4 cozypotMealLift;
        uniform vec2 cozypotMealCenter;
        vec3 vertexPositionIn;
        """;

    private const string LiftCode = """

            vertexPositionIn = cozypotVertexPositionIn;
            if (cozypotMealLift.y != 0.0 && gl_VertexID >= int(cozypotMealLift.x)) {
                vertexPositionIn.y += cozypotMealLift.y;
                vertexPositionIn.xz = cozypotMealCenter + (vertexPositionIn.xz - cozypotMealCenter) * cozypotMealLift.zw;
            }

        """;

    private static void Postfix(ShaderProgram program)
    {
        // Vanilla programs leave AssetDomain unset (null means "game"); mod programs set their own domain.
        if (program.AssetDomain is not (null or "game") || !Passes.Contains(program.PassName)) return;

        var shader = program.VertexShader;
        string? code = shader?.Code;
        if (code == null || code.Contains(UniformName)) return;

        if (!PositionInput.IsMatch(code) || !MainStart.IsMatch(code))
        {
            ScreenManager.Platform.Logger.Warning("[CozyPot] Shader {0} has an unexpected layout, pot food levels will not be lowered in it.", program.PassName);
            return;
        }

        code = PositionInput.Replace(code, Declarations, 1);
        shader!.Code = MainStart.Replace(code, match => match.Value + LiftCode, 1);
        ScreenManager.Platform.Logger.Notification("[CozyPot] Pot food lift added to shader {0}.", program.PassName);
    }
}

[HarmonyPatch(typeof(RenderAPIBase), nameof(RenderAPIBase.RenderMultiTextureMesh))]
internal static class MealLiftRenderPatch
{
    private static bool Prefix(RenderAPIBase __instance, MultiTextureMeshRef mmr, string textureSampleName, int textureNumber)
    {
        if (mmr is not PotMealMeshRef pot) return true;

        var shader = ((IRenderAPI)__instance).CurrentActiveShader;
        if (shader == null) return true;

        bool lift = shader.HasUniform(MealLiftShader.UniformName);
        if (lift) shader.Uniform(MealLiftShader.CenterUniformName, pot.CenterX, pot.CenterZ);
        for (int i = 0; i < pot.meshrefs.Length; i++)
        {
            shader.BindTexture2D(textureSampleName, pot.textureids[i], textureNumber);
            if (lift) shader.Uniform(MealLiftShader.UniformName, pot.MealStarts[i], pot.Lift, pot.ScaleX, pot.ScaleZ);
            __instance.RenderMesh(pot.meshrefs[i]);
        }
        if (lift) shader.Uniform(MealLiftShader.UniformName, 0f, 0f, 1f, 1f);
        return false;
    }
}
