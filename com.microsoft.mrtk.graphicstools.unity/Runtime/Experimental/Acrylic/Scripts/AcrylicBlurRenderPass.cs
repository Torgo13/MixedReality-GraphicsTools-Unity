// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if URP_COMPATIBILITY_MODE
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
        private readonly string profilerLabelRenderTarget1;
        private readonly string profilerLabelRenderTarget2;
        private readonly int Target1Identifier;
        private readonly int Target2Identifier;
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

#if OPTIMISATION_SHADERPARAMS
            profilerLabelRenderTarget1 = _profilerLabel + "RenderTarget1";
            profilerLabelRenderTarget2 = _profilerLabel + "RenderTarget2";
            Target1Identifier = Shader.PropertyToID(profilerLabelRenderTarget1);
            Target2Identifier = Shader.PropertyToID(profilerLabelRenderTarget2);
#endif // OPTIMISATION_SHADERPARAMS
        }

        private Vector4 info = Vector4.zero;

#if UNITY_6000_0_OR_NEWER
        [System.Obsolete]
#endif
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

#if OPTIMISATION_SHADERPARAMS
            ConfigureTempRenderTarget(ref target1, profilerLabelRenderTarget1, width, height, slices, cmd,
                Target1Identifier, in cameraTextureDescriptor);

            ConfigureTempRenderTarget(ref target2, profilerLabelRenderTarget2, width, height, slices, cmd,
                Target2Identifier, in cameraTextureDescriptor);
#else
            ConfigureTempRenderTarget(ref target1, profilerLabel + "RenderTarget1", width, height, slices, cmd);
            ConfigureTempRenderTarget(ref target2, profilerLabel + "RenderTarget2", width, height, slices, cmd);
#endif // OPTIMISATION_SHADERPARAMS

#if BUGFIX
#else
            if (providedTexture != null)
#endif // BUGFIX
            {
#if OPTIMISATION
                AcrylicLayer.InitRenderTextureTemp(ref providedTexture, width, height, 0, string.Empty);
#else
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
#endif // OPTIMISATION

                if (setMaterialTexture)
                {
                    cmd.SetGlobalTexture(textureName, providedTexture);
                }
            }
        }

#if UNITY_2022_1_OR_NEWER
#if OPTIMISATION_SHADERPARAMS
        private RenderTargetIdentifier GetIdentifier(RTHandle target)
        {
            return target == target1
                ? Target1Identifier
                : Target2Identifier;
        }
#else
        private static RenderTargetIdentifier GetIdentifier(RTHandle target)
        {
            return Shader.PropertyToID(target.name);
        }
#endif // OPTIMISATION_SHADERPARAMS
#else
        private static RenderTargetIdentifier GetIdentifier(RenderTargetHandle target)
        {
            return target.Identifier();
        }
#endif

#if UNITY_6000_0_OR_NEWER
        [System.Obsolete]
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
#if UNITY_2022_3_OR_NEWER
                Blitter.BlitTexture(cmd, colorTargetHandle, handle, default, pass: 0);
#else
                cmd.Blit(colorTargetHandle, handle);
#endif // UNITY_2022_3_OR_NEWER
            }
            else if (downSample == 2)
            {
#if OPTIMISATION_SHADERPARAMS
                cmd.SetGlobalVector(ShaderPropertyId.AcrylicBlurOffset, Vector4.zero);
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

#if OPTIMISATION_LISTPOOL
        private void QueueBlurPasses(CommandBuffer cmd, Unity.Collections.NativeArray<float> widths)
#else
        private void QueueBlurPasses(CommandBuffer cmd, float[] widths)
#endif // OPTIMISATION_LISTPOOL
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
            var rttmp = target1;
            target1 = target2;
            target2 = rttmp;
        }

#if UNITY_2022_1_OR_NEWER
#if OPTIMISATION_SHADERPARAMS
        private void ConfigureTempRenderTarget(ref RTHandle target, string id, int width, int height, int slices, CommandBuffer cmd,
            int nameID, in RenderTextureDescriptor cameraTextureDescriptor)
        {
            _ = RenderingUtils.ReAllocateIfNeeded(ref target, cameraTextureDescriptor, name: id);

            if (slices > 1)
            {
                cmd.GetTemporaryRTArray(nameID, width, height, slices, 0, FilterMode.Bilinear);
            }
            else
            {
                cmd.GetTemporaryRT(nameID, width, height, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32);
            }

            ConfigureTarget(target);
        }
#else
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
#endif // OPTIMISATION_SHADERPARAMS
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

#if OPTIMISATION_LISTPOOL
        public static Unity.Collections.NativeArray<float> BlurWidths(int passes)
        {
            Unity.Collections.NativeArray<float> widths;

            switch (passes)
            {
                case 2:
                    widths = new Unity.Collections.NativeArray<float>(2,
                        Unity.Collections.Allocator.Temp);
                    break;
                case 3:
                    widths = new Unity.Collections.NativeArray<float>(3,
                        Unity.Collections.Allocator.Temp, Unity.Collections.NativeArrayOptions.UninitializedMemory);
                    widths[0] = 0.0f;
                    widths[1] = 1.0f;
                    widths[2] = 1.0f;
                    break;
                case 4:
                    widths = new Unity.Collections.NativeArray<float>(4,
                        Unity.Collections.Allocator.Temp, Unity.Collections.NativeArrayOptions.UninitializedMemory);
                    widths[0] = 0.0f;
                    widths[1] = 1.0f;
                    widths[2] = 1.0f;
                    widths[3] = 2.0f;
                    break;
                case 5:
                    widths = new Unity.Collections.NativeArray<float>(5,
                        Unity.Collections.Allocator.Temp, Unity.Collections.NativeArrayOptions.UninitializedMemory);
                    widths[0] = 0.0f;
                    widths[1] = 1.0f;
                    widths[2] = 2.0f;
                    widths[3] = 2.0f;
                    widths[4] = 3.0f;
                    break;
                case 6:
                    widths = new Unity.Collections.NativeArray<float>(7,
                        Unity.Collections.Allocator.Temp, Unity.Collections.NativeArrayOptions.UninitializedMemory);
                    widths[0] = 0.0f;
                    widths[1] = 1.0f;
                    widths[2] = 2.0f;
                    widths[3] = 3.0f;
                    widths[4] = 4.0f;
                    widths[5] = 4.0f;
                    widths[6] = 5.0f;
                    break;
                default:
                    widths = new Unity.Collections.NativeArray<float>(10,
                        Unity.Collections.Allocator.Temp, Unity.Collections.NativeArrayOptions.UninitializedMemory);
                    widths[0] = 0.0f;
                    widths[1] = 1.0f;
                    widths[2] = 2.0f;
                    widths[3] = 3.0f;
                    widths[4] = 4.0f;
                    widths[5] = 5.0f;
                    widths[6] = 7.0f;
                    widths[7] = 8.0f;
                    widths[8] = 9.0f;
                    widths[9] = 10.0f;
                    break;
            }

            return widths;
        }
#else
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
#endif // OPTIMISATION_LISTPOOL
    }
}
#endif // GT_USE_URP
#endif // URP_COMPATIBILITY_MODE
