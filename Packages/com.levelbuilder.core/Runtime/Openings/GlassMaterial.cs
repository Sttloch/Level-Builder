using UnityEngine;
using UnityEngine.Rendering;

namespace LevelBuilder
{
    /// <summary>Builds a transparent glass material from a pipeline's lit material without referencing pipeline packages.</summary>
    public static class GlassMaterial
    {
        public static readonly Color Tint = new Color(0.6f, 0.8f, 0.9f, 0.3f);

        /// <summary>
        /// A DontSave clone of <paramref name="basedOn"/> set up as transparent for its shader (Standard, URP Lit,
        /// HDRP Lit). Null <paramref name="basedOn"/> falls back to the Standard shader; returns null if that is
        /// unavailable. The caller owns (and destroys) the result.
        /// </summary>
        public static Material Create(Material basedOn)
        {
            Material m;
            if (basedOn != null) m = new Material(basedOn);
            else
            {
                var sh = Shader.Find("Standard");
                if (sh == null) return null;
                m = new Material(sh);
            }
            m.name = (basedOn != null ? basedOn.name : "Standard") + " (Glass)";
            m.hideFlags = HideFlags.DontSave;

            string shader = m.shader != null ? m.shader.name : "";
            if (shader == "Universal Render Pipeline/Lit")
            {
                SetFloat(m, "_Surface", 1f);
                SetFloat(m, "_Blend", 0f);
                SetFloat(m, "_SrcBlend", (float)BlendMode.SrcAlpha);
                SetFloat(m, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                SetFloat(m, "_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetShaderPassEnabled("DepthOnly", false);
            }
            else if (shader == "HDRP/Lit")
            {
                // Best effort (unverified here): standard alpha blend, no depth write, no depth/motion passes.
                SetFloat(m, "_SurfaceType", 1f);
                SetFloat(m, "_BlendMode", 0f);
                SetFloat(m, "_SrcBlend", (float)BlendMode.SrcAlpha);
                SetFloat(m, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                SetFloat(m, "_ZWrite", 0f);
                SetFloat(m, "_ZTestDepthEqualForOpaque", (float)CompareFunction.LessEqual);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.EnableKeyword("_BLENDMODE_ALPHA");
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetShaderPassEnabled("DepthOnly", false);
                m.SetShaderPassEnabled("MotionVectors", false);
            }
            else
            {
                // Standard: what the inspector does for Rendering Mode = Transparent.
                SetFloat(m, "_Mode", 3f);
                SetFloat(m, "_SrcBlend", (float)BlendMode.One);
                SetFloat(m, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                SetFloat(m, "_ZWrite", 0f);
                m.DisableKeyword("_ALPHATEST_ON");
                m.DisableKeyword("_ALPHABLEND_ON");
                m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                m.SetOverrideTag("RenderType", "Transparent");
            }

            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Tint);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Tint);
            m.renderQueue = (int)RenderQueue.Transparent; // 3000
            return m;
        }

        static void SetFloat(Material m, string name, float v)
        {
            if (m.HasProperty(name)) m.SetFloat(name, v);
        }
    }
}
