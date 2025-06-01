// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using UnityEngine;
using UnityEngine.Rendering;

namespace Microsoft.MixedReality.GraphicsTools
{
    /// <summary>
    /// Utility class to help with operations involving accessibility support.
    /// </summary>
    public static class AccessibilityUtilities
    {
        /// <summary>
        /// Shader keyword used by the "Graphics Tools/Text Mesh Pro" to conditionally invert text.
        /// </summary>
        private static readonly string InvertTextColorKeyword = "_INVERT_TEXT_COLOR";

#if OPTIMISATION_SHADERPARAMS
        private static readonly int _SrcBlend = Shader.PropertyToID("_SrcBlend");
        private static readonly int _DstBlend = Shader.PropertyToID("_DstBlend");
#endif // OPTIMISATION_SHADERPARAMS

        /// <summary>
        /// Enabling this will instruct a "Graphics Tools/Text Mesh Pro" based material to display a face color which is an inversion of what it is rendering over top of.
        /// </summary>
        public static void SetTextColorInversion(Material textMaterial, bool Invert)
        {
            if (!StandardShaderUtility.IsUsingGraphicsToolsTextMeshProShader(textMaterial))
            {
                Debug.LogWarningFormat("Failed to set the text color inversion because the material isn't using the {0} shader.",
                                       StandardShaderUtility.GraphicsToolsTextMeshProShaderName);
                return;
            }

            if (Invert)
            {
                textMaterial.EnableKeyword(InvertTextColorKeyword);
#if OPTIMISATION_SHADERPARAMS
                textMaterial.SetFloat(_SrcBlend, (float)BlendMode.OneMinusDstColor);
                textMaterial.SetFloat(_DstBlend, (float)BlendMode.OneMinusSrcColor);
#else
                textMaterial.SetFloat("_SrcBlend", (float)BlendMode.OneMinusDstColor);
                textMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcColor);
#endif // OPTIMISATION_SHADERPARAMS
            }
            else
            {
                textMaterial.DisableKeyword(InvertTextColorKeyword);
#if OPTIMISATION_SHADERPARAMS
                textMaterial.SetFloat(_SrcBlend, (float)BlendMode.One);
                textMaterial.SetFloat(_DstBlend, (float)BlendMode.OneMinusSrcAlpha);
#else
                textMaterial.SetFloat("_SrcBlend", (float)BlendMode.One);
                textMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
#endif // OPTIMISATION_SHADERPARAMS
            }
        }

        /// <summary>
        /// If the "Graphics Tools/Text Mesh Pro" based material is already inverted this method disables inversion, else this method enables inversion.
        /// </summary>
        public static void ToggleTextColorInversion(Material textMaterial)
        {
            bool Invert = textMaterial ? textMaterial.IsKeywordEnabled(InvertTextColorKeyword) : false;

            SetTextColorInversion(textMaterial, !Invert);
        }
    }
}
