// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if URP_COMPATIBILITY_MODE
#if GT_USE_URP
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Microsoft.MixedReality.GraphicsTools
{
    public class ClearRenderTarget : ScriptableRendererFeature
    {
        [System.Serializable]
        public class PassSettings
        {
            public RenderPassEvent RenderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            public bool ClearDepth = true;
            public bool ClearColor = true;
            public Color BackgroundColor = Color.black;
        }

        [SerializeField]
        private PassSettings settings = new PassSettings();
        private ClearRenderTargetPass pass;

        /// <inheritdoc/>
        public override void Create()
        {
            pass = new ClearRenderTargetPass(settings);
        }

        /// <inheritdoc/>
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(pass);
        }
    }
}
#endif // GT_USE_URP
#endif // URP_COMPATIBILITY_MODE
