// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if GT_USE_URP
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// TODO - [Cameron-Micka] remove obsolete API.
#pragma warning disable 0618

namespace Microsoft.MixedReality.GraphicsTools
{
    /// <summary>
    /// Render pass implementation for the AcrylicBlur renderer feature
    /// </summary>

    class AcrylicBlurRenderPass : ScriptableRenderPass
    {
        public bool setMaterialTexture = false;
        private string profilerLabel;
#if UNITY_2022_1_OR_NEWER
        private RTHandle target1;
        private RTHandle target2;
#else
        private RenderTargetHandle target1;
        private RenderTargetHandle target2;
#endif
        private int downSample;
        private int passes;
        private string textureName;
        private Material blurMaterial;
        private Vector2 pixelSize;
        private RenderTexture providedTexture;
        private bool blur;
        private AcrylicFilterDual blurFilter;

#if OPTIMISATION_SHADERPARAMS
        private struct ShaderPropertyId
        {
            public static readonly int AcrylicInfo = Shader.PropertyToID("_AcrylicInfo");
            public static readonly int AcrylicBlurOffset = Shader.PropertyToID("_AcrylicBlurOffset");
            public static readonly int AcrylicBlurSource = Shader.PropertyToID("_AcrylicBlurSource");
        }
#endif // OPTIMISATION_SHADERPARAMS

        public AcrylicBlurRenderPass(string _profilerLabel, int _downSamplePasses, int _passes, Material material, string _textureName, bool _blur, RenderTexture _texture, AcrylicFilterDual _blurFilter)
        {
            profilerLabel = _profilerLabel;
            passes = _passes;
            textureName = _textureName;
            blurMaterial = material;
            providedTexture = _texture;

            downSample = 1;
            int i = _downSamplePasses;
            while (i > 0)
            {
                downSample *= 2;
                i--;
            }
            blur = _blur;
            blurFilter = _blurFilter;
        }

        private Vector4 info = Vector4.zero;

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            int width = cameraTextureDescriptor.width / downSample;
            int height = cameraTextureDescriptor.height / downSample;
            int slices = cameraTextureDescriptor.volumeDepth;

            pixelSize = new Vector2(1.0f / width, 1.0f / height);

            info.x = (cameraTextureDescriptor.vrUsage == VRTextureUsage.TwoEyes) ? 1.0f : 0.5f;
            info.y = slices > 1 ? 1.0f : 0.5f;
            info.z = 1.0f;
            info.w = 1.0f;

            ConfigureTempRenderTarget(ref target1, profilerLabel + "RenderTarget1", width, height, slices, cmd);
            ConfigureTempRenderTarget(ref target2, profilerLabel + "RenderTarget2", width, height, slices, cmd);

#if BUGFIX
#else
            if (providedTexture != null)
            {
#endif // BUGFIX
                if (providedTexture == null)
                {
                    providedTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
                    providedTexture.filterMode = FilterMode.Bilinear;
                }
                else
                {
                    if (width != providedTexture.width || height != providedTexture.height)
                    {
                        providedTexture.Release();
                        providedTexture.width = width;
                        providedTexture.height = height;
                        providedTexture.Create();
                    }
                }

                // TODO Release providedTexture
                //AcrylicLayer.InitRenderTexture(ref providedTexture, width, height, 0);

                if (setMaterialTexture)
                {
                    cmd.SetGlobalTexture(textureName, providedTexture);
                }
#if BUGFIX
#else
            }
#endif // BUGFIX
        }

#if UNITY_2022_1_OR_NEWER
        private static RenderTargetIdentifier GetIdentifier(RTHandle target)
        {
            return Shader.PropertyToID(target.name);
        }
#else
        private static RenderTargetIdentifier GetIdentifier(RenderTargetHandle target)
        {
            return target.Identifier();
        }
#endif

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get(profilerLabel);
            cmd.Clear();

            var handle = providedTexture == null ? GetIdentifier(target1) : providedTexture;
            var renderer = renderingData.cameraData.renderer;
#if UNITY_2022_1_OR_NEWER
            var colorTargetHandle = renderer.cameraColorTargetHandle;
#else
            var colorTargetHandle = renderer.cameraColorTarget;
#endif

#if OPTIMISATION_SHADERPARAMS
            cmd.SetGlobalVector(ShaderPropertyId.AcrylicInfo, info);
#else
            cmd.SetGlobalVector("_AcrylicInfo", info);
#endif // OPTIMISATION_SHADERPARAMS

            if (downSample == 1)
            {
                cmd.Blit(colorTargetHandle, handle);
            }
            else if (downSample == 2)
            {
#if OPTIMISATION_SHADERPARAMS
                cmd.SetGlobalVector(ShaderPropertyId.AcrylicBlurOffset, Vector2.zero);
#else
                cmd.SetGlobalVector("_AcrylicBlurOffset", Vector2.zero);
#endif // OPTIMISATION_SHADERPARAMS

                LocalBlit(cmd, colorTargetHandle, handle, blurMaterial);
            }
            else
            {
#if OPTIMISATION_SHADERPARAMS
                cmd.SetGlobalVector(ShaderPropertyId.AcrylicBlurOffset, 0.25f * pixelSize);
#else
                cmd.SetGlobalVector("_AcrylicBlurOffset", 0.25f * pixelSize);
#endif // OPTIMISATION_SHADERPARAMS

                LocalBlit(cmd, colorTargetHandle, handle, blurMaterial);
            }

            if (blur)
            {
                if (blurFilter == null || providedTexture == null)
                {
                    QueueBlurPasses(cmd, BlurWidths(passes));
                }
                else
                {
                    blurFilter.QueueBlur(cmd, providedTexture, passes);
                }
            }

            if (providedTexture == null && setMaterialTexture)
            {
                cmd.SetGlobalTexture(textureName, GetIdentifier(target1));
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        private void QueueBlurPasses(CommandBuffer cmd, float[] widths)
        {
            for (int i = 0; i < widths.Length; i++)
            {
#if OPTIMISATION_SHADERPARAMS
                cmd.SetGlobalVector(ShaderPropertyId.AcrylicBlurOffset, (0.5f + widths[i]) * pixelSize);
#else
                cmd.SetGlobalVector("_AcrylicBlurOffset", (0.5f + widths[i]) * pixelSize);
#endif // OPTIMISATION_SHADERPARAMS

                if (providedTexture != null && i == widths.Length - 1)
                {
                    LocalBlit(cmd, GetIdentifier(target1), providedTexture, blurMaterial);
                }
                else if (providedTexture != null && i == 0)
                {
                    LocalBlit(cmd, providedTexture, GetIdentifier(target1), blurMaterial);
                }
                else
                {
                    LocalBlit(cmd, GetIdentifier(target1), GetIdentifier(target2), blurMaterial);
                    SwapTempTargets();
                }
            }
        }

        //TODO: replace with new URP Blit() when that's working with multiview
        private void LocalBlit(CommandBuffer cmd, RenderTargetIdentifier source, RenderTargetIdentifier target, Material material)
        {
            cmd.SetRenderTarget(target);

#if OPTIMISATION_SHADERPARAMS
            cmd.SetGlobalTexture(ShaderPropertyId.AcrylicBlurSource, source);
#else
            cmd.SetGlobalTexture("_AcrylicBlurSource", source);
#endif // OPTIMISATION_SHADERPARAMS

            cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, material);
        }

        private void SwapTempTargets()
        {
#if OPTIMISATION
            (target1, target2) = (target2, target1);
#else
            var rttmp = target1;
            target1 = target2;
            target2 = rttmp;
#endif // OPTIMISATION
        }

#if UNITY_2022_1_OR_NEWER
        private void ConfigureTempRenderTarget(ref RTHandle target, string id, int width, int height, int slices, CommandBuffer cmd)
        {
            target = RTHandles.Alloc(id, name: id);
            if (slices > 1)
            {
                cmd.GetTemporaryRTArray(Shader.PropertyToID(target.name), width, height, slices, 0, FilterMode.Bilinear);
            }
            else
            {
                cmd.GetTemporaryRT(Shader.PropertyToID(target.name), width, height, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32);
            }

            ConfigureTarget(target);
        }
#else
        private void ConfigureTempRenderTarget(ref RenderTargetHandle target, string id, int width, int height, int slices, CommandBuffer cmd)
        {
            target.Init(id);
            if (slices > 1)
            {
                cmd.GetTemporaryRTArray(target.id, width, height, slices, 0, FilterMode.Bilinear);
            }
            else
            {
                cmd.GetTemporaryRT(target.id, width, height, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32);
            }

            ConfigureTarget(target.Identifier());
        }
#endif


        public static float[] BlurWidths(int passes)
        {
            switch (passes)
            {
                case 2:
                    return new float[] { 0.0f, 0.0f };

                case 3:
                    return new float[] { 0.0f, 1.0f, 1.0f };

                case 4:
                    return new float[] { 0.0f, 1.0f, 1.0f, 2.0f };

                case 5:
                    return new float[] { 0.0f, 1.0f, 2.0f, 2.0f, 3.0f };

                case 6:
                    return new float[] { 0.0f, 1.0f, 2.0f, 3.0f, 4.0f, 4.0f, 5.0f };

                default:
                    return new float[] { 0.0f, 1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 7.0f, 8.0f, 9.0f, 10.0f };
            }
        }

#if OPTIMISATION_LISTPOOL
        public static void BlurWidths(ref System.Collections.Generic.List<float> widths, int passes)
        {
            switch (passes)
            {
                case 2:
                    widths.Add(0.0f);
                    widths.Add(0.0f);
                    break;
                case 3:
                    widths.Add(0.0f);
                    widths.Add(1.0f);
                    widths.Add(1.0f);
                    break;
                case 4:
                    widths.Add(0.0f);
                    widths.Add(1.0f);
                    widths.Add(1.0f);
                    widths.Add(2.0f);
                    break;

                case 5:
                    widths.Add(0.0f);
                    widths.Add(1.0f);
                    widths.Add(2.0f);
                    widths.Add(2.0f);
                    widths.Add(3.0f);
                    break;
                case 6:
                    widths.Add(0.0f);
                    widths.Add(1.0f);
                    widths.Add(2.0f);
                    widths.Add(3.0f);
                    widths.Add(4.0f);
                    widths.Add(4.0f);
                    widths.Add(5.0f);
                    break;
                default:
                    widths.Add(0.0f);
                    widths.Add(1.0f);
                    widths.Add(2.0f);
                    widths.Add(3.0f);
                    widths.Add(4.0f);
                    widths.Add(5.0f);
                    widths.Add(7.0f);
                    widths.Add(8.0f);
                    widths.Add(9.0f);
                    widths.Add(10.0f);
                    break;
            }
        }
#endif // OPTIMISATION_LISTPOOL
    }
}
#endif // GT_USE_URP
