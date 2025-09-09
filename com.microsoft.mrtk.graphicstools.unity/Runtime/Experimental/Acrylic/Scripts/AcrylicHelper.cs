// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if GT_USE_URP
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Microsoft.MixedReality.GraphicsTools
{
#if OPTIMISATION_SHADERPARAMS
    static class ShaderPropertyId
    {
        public static readonly int AcrylicBlurOffset = Shader.PropertyToID("_AcrylicBlurOffset");
        public static readonly int AcrylicBlurSource = Shader.PropertyToID("_AcrylicBlurSource");

        #region AcrylicBlurRenderPass
        public static readonly int AcrylicInfo = Shader.PropertyToID("_AcrylicInfo");
        #endregion // AcrylicBlurRenderPass

        #region AcrylicFilterDual
        public static readonly int AcrylicHalfPixel = Shader.PropertyToID("_AcrylicHalfPixel");
        #endregion // AcrylicFilterDual

        #region AcrylicLayer
        public static readonly int AcrylicBlendTexture = Shader.PropertyToID("_AcrylicBlendTexture");
        public static readonly int AcrylicBlendFraction = Shader.PropertyToID("_AcrylicBlendFraction");
        #endregion // AcrylicLayer
    }
#endif // OPTIMISATION_SHADERPARAMS

    /// <summary>
    /// Helper component that automatically enables/disables the specified acrylic layer when an object is enabled/disabled
    /// (notifying the acrylic layer manager and updating the object's material).  Attach to any object that uses an acrylic material.
    /// EnableLayer() & DisableLayer() methods can be used to explicitly enable or disable the designated layer.
    /// </summary>

    public class AcrylicHelper : MonoBehaviour
    {
        [Experimental]
        [SerializeField]
        [Range(0, 1)]
        private int blurLayer = 0;

        private bool useAcrylic = false;
        private Graphic cachedGraphic = null;
        private Coroutine initCoroutine = null;

        #region Monobehavior methods

        private void OnEnable()
        {
#if LEGACY_ACRYLIC
#else
            AcrylicLayerManager.Instance.AcrylicActive = true;
#endif // LEGACY_ACRYLIC

            initCoroutine = StartCoroutine(WaitForAcrylicLayerManager());
        }

        private void OnDisable()
        {
#if LEGACY_ACRYLIC
#else
            AcrylicLayerManager.Instance.AcrylicActive = false;
#endif // LEGACY_ACRYLIC

            if (initCoroutine != null)
            {
                StopCoroutine(initCoroutine);
                initCoroutine = null;
            }
            else
            {
                DisableLayer();
            }
        }

        #endregion

        #region public methods

        /// <summary>
        /// Adds a reference to the current blur layer.
        /// </summary>
        public void EnableLayer()
        {
            if (AcrylicLayerManager.Instance != null)
            {
                AcrylicLayerManager.Instance.EnableLayer(blurLayer);
            }
        }

        /// <summary>
        /// Removes a reference from the current blur layer.
        /// </summary>
        public void DisableLayer()
        {
            if (AcrylicLayerManager.Instance != null)
            {
                AcrylicLayerManager.Instance.DisableLayer(blurLayer);
            }
        }

        #endregion

        #region private methods

        private void UpdateMaterialState()
        {
            if (cachedGraphic == null)
            {
                cachedGraphic = GetComponent<Graphic>();
            }

            if (cachedGraphic != null)
            {
                useAcrylic = AcrylicLayerManager.Instance != null && AcrylicLayerManager.Instance.AcrylicActive;
                SetMaterialState(cachedGraphic.material, "_BLUR_TEXTURE_ENABLE_", useAcrylic && blurLayer == 0);
                SetMaterialState(cachedGraphic.material, "_BLUR_TEXTURE_2_ENABLE_", useAcrylic && blurLayer == 1);
                cachedGraphic.SetMaterialDirty();
            }
        }

#if OPTIMISATION_STATIC
        static
#endif // OPTIMISATION_STATIC
        private void SetMaterialState(Material m, string keyword, bool enable)
        {
            if (enable)
            {
                m.EnableKeyword(keyword);
            }
            else
            {
                m.DisableKeyword(keyword);
            }
        }

        private IEnumerator WaitForAcrylicLayerManager()
        {
            // wait for the AcrylicLayerManager to exist
            while (AcrylicLayerManager.Instance == null)
            {
                yield return null;
            }

            // then update material/layer
            UpdateMaterialState();
            EnableLayer();

            initCoroutine = null;
        }
        #endregion
    }
}
#endif // GT_USE_URP
