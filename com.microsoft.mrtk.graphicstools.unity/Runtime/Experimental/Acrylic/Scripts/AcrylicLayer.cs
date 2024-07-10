// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if GT_USE_URP
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering.Universal;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// TODO - [Cameron-Micka] remove obsolete API.
#pragma warning disable 0618

namespace Microsoft.MixedReality.GraphicsTools
{
    /// <summary>
    /// Class representing a single acrylic layer, used in conjunction with AcrylicLayerManager
    /// </summary>

    public class AcrylicLayer : IDisposable
    {
        [System.Serializable]
        public class Settings
        {
            [Tooltip("When to copy the framebuffer in the rendering pipeline. No effect when render-to-texture is used.")]
            public RenderPassEvent captureEvent = RenderPassEvent.AfterRenderingPostProcessing;
            [Tooltip("If not nothing, creates render object features for the specified layers.")]
            public LayerMask renderLayers;
            [Range(2, 7)]
            public int blurPasses = 5;
            [Range(0, 2)]
            public int downSample = 2;
            public string blurTextureName;

            [Header("Advanced")]
            [Tooltip("The name of the render feature to add the blur after (or before) to enforce custom sorting. Adds to the end of the render feature list when empty.")]
            public string targetRenderFeatureName = string.Empty;

            public enum AddMode { After, Before }
            [Tooltip("When a Target Render Feature Name is specified, should it be added before or after the feature in the list?")]
            public AddMode targetRenderFeatureAddMode = AddMode.After;
        }

        public int activeCount;
        public int frameCount;
        public bool firstFrameRendered;

        private Camera targetCamera;
        private AcrylicBlurFeature blur;
        private ScriptableRendererFeature renderOpaque;
        private ScriptableRendererFeature renderTransparent;
        private RenderTexture renderTarget1;
        private RenderTexture renderTarget2;
        private RenderTexture[] blendSource;
        private int blendSourceIndex;
        private RenderTexture blendTarget;
        private bool blurred;
        private bool immediateBlur;

        private Settings settings;
        private int index;
        private int depthBits;
        private Material kawaseBlur;
        private bool useDualBlur;
        private AcrylicFilterDual dualBlur = null;

        private static CommandBuffer cmd = null;

        private struct ShaderPropertyId
        {
            public static readonly int AcrylicBlurOffset = Shader.PropertyToID("_AcrylicBlurOffset");
            public static readonly int AcrylicBlurSource = Shader.PropertyToID("_AcrylicBlurSource");
            public static readonly int AcrylicBlendTexture = Shader.PropertyToID("_AcrylicBlendTexture");
            public static readonly int AcrylicBlendFraction = Shader.PropertyToID("_AcrylicBlendFraction");
            public const string AcrylicBlurPasses = "AcrylicBlurPasses";
            public const string BlendSource = "BlendSource";
            public const string BlendTarget = "BlendTarget";
            public const string Destination = "Destination";
            public const string RenderTarget1 = "RenderTarget1";
            public const string RenderTarget2 = "RenderTarget2";
        }

        #region Public methods
        public AcrylicLayer(Camera _targetCamera, Settings _settings, int _index, int _depthBits, bool _useDualBlur, Material _kawaseBlur, Material _dualBlur)
        {
            activeCount = 0;
            frameCount = 0;
            targetCamera = _targetCamera;
            renderTarget1 = null;
            renderTarget2 = null;
            blendTarget = null;
            blendSource = new RenderTexture[2];
            blendSource[0] = null;
            blendSource[1] = null;
            blendSourceIndex = 0;
            firstFrameRendered = false;
            blurred = false;
            immediateBlur = false;

            settings = _settings;
            index = _index;
            depthBits = _depthBits;
            useDualBlur = _useDualBlur;
            kawaseBlur = _kawaseBlur;
            if (useDualBlur) dualBlur = new AcrylicFilterDual(_dualBlur);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (blur != null)
            {
                DestroyScriptableObject(blur);
                blur = null;
            }

            if (renderOpaque != null)
            {
                DestroyScriptableObject(renderOpaque);
                renderOpaque = null;
            }

            if (renderTransparent != null)
            {
                DestroyScriptableObject(renderTransparent);
                renderTransparent = null;
            }

            if (dualBlur != null)
            {
                dualBlur.Dispose();
                dualBlur = null;
            }
        }

#if UNITY_2021_2_OR_NEWER
        public void AddLayerRendererFeatures(UniversalRendererData rendererData, bool updateEveryFrame)
#else
        public void AddLayerRendererFeatures(ForwardRendererData rendererData, bool updateEveryFrame)
#endif
        {
            if (rendererData == null) return;

            int insertIndex = -1;

            if (!string.IsNullOrEmpty(settings.targetRenderFeatureName))
            {
                insertIndex = rendererData.rendererFeatures.FindIndex(x => x.name == settings.targetRenderFeatureName);
            }

            if (insertIndex == -1)
            {
                insertIndex = rendererData.rendererFeatures.Count - 1;
            }

            if (settings.targetRenderFeatureAddMode == Settings.AddMode.After)
            {
                ++insertIndex;
            }

            rendererData.rendererFeatures.Insert(insertIndex, blur);

            if (renderOpaque != null)
            {
                ++insertIndex;
                rendererData.rendererFeatures.Insert(insertIndex, renderOpaque);
                rendererData.opaqueLayerMask = rendererData.opaqueLayerMask & ~settings.renderLayers;
            }
            if (renderTransparent != null)
            {
                ++insertIndex;
                rendererData.rendererFeatures.Insert(insertIndex, renderTransparent);
                rendererData.transparentLayerMask = rendererData.transparentLayerMask & ~settings.renderLayers;
            }

            blur.SetMaterialTexture(!firstFrameRendered || updateEveryFrame);
            immediateBlur = !firstFrameRendered || updateEveryFrame;
            blur.ApplyBlur(immediateBlur);

            RenderTexture captureTarget = null;
            if (!updateEveryFrame || useDualBlur)
            {
                if (renderTarget1 == null) InitRenderTargets(1, 1, depthBits);
                captureTarget = renderTarget1;
            }
            blur.SetStorageTexture(captureTarget);
            blur.rendered = false;
            blurred = false;

            rendererData.SetDirty();
        }

        public void ForceCaptureNextFrame()
        {
            if (blur!=null)
            {
                if (!useDualBlur) blur.SetStorageTexture(null);
                blur.SetMaterialTexture(true);
                blur.rendered = false;
                blur.ApplyBlur(true);
                blur.blur = settings.blurPasses;
                blur.downSample = settings.downSample;
                immediateBlur = true;
            }
        }

#if UNITY_2021_2_OR_NEWER
        public void RemoveLayerRendererFeatures(UniversalRendererData rendererData)
#else
        public void RemoveLayerRendererFeatures(ForwardRendererData rendererData)
#endif
        {
            if (rendererData == null) return;

            rendererData.rendererFeatures.Remove(blur);

            if (renderOpaque != null)
            {
                LayerMask opaqueMask = rendererData.opaqueLayerMask;
                rendererData.rendererFeatures.Remove(renderOpaque);
                opaqueMask |= settings.renderLayers;
                rendererData.opaqueLayerMask = opaqueMask;
            }
            if (renderTransparent != null)
            {
                LayerMask transparentMask = rendererData.transparentLayerMask;
                rendererData.rendererFeatures.Remove(renderTransparent);
                transparentMask |= settings.renderLayers;
                rendererData.transparentLayerMask = transparentMask;
            }

            rendererData.SetDirty();
        }

        public void SwapRenderTargets()
        {
            (renderTarget1, renderTarget2) = (renderTarget2, renderTarget1);
        }

        public int DownSamplePowerOf2 => PowerOf2(settings.downSample);

        public bool CaptureNextFrame => frameCount == 0;

        public void CreateRendererFeatures()
        {
            blur = CreateBlurFeature(Cysharp.Text.ZString.Concat("Acrylic Blur", index), settings.captureEvent, kawaseBlur, targetCamera);

            if (settings.renderLayers.value != 0)
            {
                renderOpaque = CreateRenderObjectsFeature(Cysharp.Text.ZString.Concat("Post Acrylic Blur  Opaque ", index), RenderQueueType.Opaque, settings.captureEvent);
                renderTransparent = CreateRenderObjectsFeature(Cysharp.Text.ZString.Concat("Post Acrylic Blur Transparent ", index), RenderQueueType.Transparent, settings.captureEvent);
            }
        }

        public bool FirstFrameGenerated => firstFrameRendered && blurred;

#if UNITY_2021_2_OR_NEWER
        public void UpdateFrame(UniversalRendererData rendererData, bool copyFramebuffer, int updatePeriod, int blendFrames, Material blendMaterial, bool autoUpdate)
#else
        public void UpdateFrame(ForwardRendererData rendererData, bool copyFramebuffer, int updatePeriod, int blendFrames, Material blendMaterial, bool autoUpdate)
#endif
        {
            if (!firstFrameRendered && copyFramebuffer && blur.rendered)
            {
                firstFrameRendered = true;
                if (blendFrames > 0 && updatePeriod>1 && renderTarget1!=null && autoUpdate)
                    SetBlendSource(renderTarget1, true);
            }

            if (firstFrameRendered)
            {
                if (!blurred && copyFramebuffer && blur.rendered)
                {
                    blurred = immediateBlur;
                    if (blurred)
                        SwapRenderTargets();
                }

                if (!blurred && updatePeriod > 1 && autoUpdate)
                {
                    ApplyBlur();
                    if (blendFrames > 0)
                    {
                        SetBlendSource(renderTarget1, false);
                    }
                    else
                    {
                        Shader.SetGlobalTexture(settings.blurTextureName, renderTarget1);
                        SwapRenderTargets();
                    }
                    blurred = true;
                }
                if (blendMaterial != null && blendFrames > 0)
                {
                    float blend = Mathf.Clamp01((float)frameCount / blendFrames);
                    BlendLayer(blend, blendMaterial);
                }
                ++frameCount;
                if (autoUpdate)
                {
                    frameCount = frameCount % updatePeriod;
                }
            }
        }

#if UNITY_2021_2_OR_NEWER
        public bool InFeaturesList(UniversalRendererData rendererData)
#else
        public bool InFeaturesList(ForwardRendererData rendererData)
#endif
        {
            return rendererData != null && blur != null && rendererData.rendererFeatures.Contains(blur);
        }

        public void RenderToTexture(ScriptableRenderContext context, Camera mainCamera, int updatePeriod, int blendFrames, LayerMask hiddenLayers)
        {
#if ENABLE_PROFILER
            Profiler.BeginSample($"AcrylicLayer{index}_RenderToTexture");
#endif
            int camWidth = mainCamera.scaledPixelWidth;
            int camHeight = mainCamera.scaledPixelHeight;

            int downSample = DownSamplePowerOf2;
            int newWidth = camWidth / downSample;
            int newHeight = camHeight / downSample;

            InitRenderTargets(newWidth, newHeight, depthBits);

            LayerMask previousCullingMask = mainCamera.cullingMask;
            RenderTexture previousTargetTexture = mainCamera.targetTexture;

            mainCamera.cullingMask = previousCullingMask & (~hiddenLayers);
            mainCamera.targetTexture = renderTarget1;

            UnityEngine.Rendering.Universal.UniversalRenderPipeline.RenderSingleCamera(context, mainCamera);

            mainCamera.cullingMask = previousCullingMask;
            mainCamera.targetTexture = previousTargetTexture;

            blurred = updatePeriod < 2 || !firstFrameRendered;

            if (blurred)
            {
                ApplyBlur();
                if (blendFrames > 0)
                {
                    SetBlendSource(renderTarget1, !firstFrameRendered);
                }
                else
                {
                    Shader.SetGlobalTexture(settings.blurTextureName, renderTarget1);
                    SwapRenderTargets();
                }
            }
            if (!firstFrameRendered)
                firstFrameRendered = true;

#if ENABLE_PROFILER
            Profiler.EndSample();
#endif
        }

#if UNITY_2021_2_OR_NEWER
        public void MakeFeaturesPersistent(UniversalRendererData rendererData)
#else
        public void MakeFeaturesPersistent(ForwardRendererData rendererData)
#endif
        {
            MakeFeaturePersistent(rendererData, blur);
            if (renderOpaque != null)
            {
                MakeFeaturePersistent(rendererData, renderOpaque);
            }
            if (renderTransparent != null)
            {
                MakeFeaturePersistent(rendererData, renderTransparent);
            }
        }

#endregion

        #region Blur and blend methods
        public void ApplyBlur()
        {
            ApplyBlur(ref renderTarget1, ref renderTarget2);
        }

        public void ApplyBlur(ref RenderTexture source, ref RenderTexture destination)
        {
            if (source == null)
            {
#if UNITY_EDITOR || DEBUG
                Debug.LogWarning("Null blur source texture.");
#endif
                return;
            }
            if (useDualBlur)
            {
                dualBlur.ApplyBlur(Cysharp.Text.ZString.Concat("AcrylicLayer", index, "_Blur"), source, settings.blurPasses);
            }
            else
            {
                InitRenderTexture(ref destination, source.width, source.height, depthBits, ShaderPropertyId.Destination);
                InitCommandBuffer();
#if ENABLE_PROFILER
                Profiler.BeginSample(Cysharp.Text.ZString.Concat("AcrylicLayer", index, "_Blur"));
#endif
                cmd.Clear();

                int width = source.width;
                int height = source.height;
                Vector2 pixelSize = new Vector2(1.0f / width, 1.0f / height);

                var widths = UnityEngine.Pool.ListPool<float>.Get();
                AcrylicBlurRenderPass.BlurWidths(ref widths, settings.blurPasses);
                for (int i = 0, widthsCount = widths.Count; i < widthsCount; i++)
                {
                    cmd.SetGlobalVector(ShaderPropertyId.AcrylicBlurOffset, (0.5f + widths[i]) * pixelSize);
                    LocalBlit(cmd, source, destination, kawaseBlur);

                    (source, destination) = (destination, source);
                }
                UnityEngine.Pool.ListPool<float>.Release(widths);
                Graphics.ExecuteCommandBuffer(cmd);
#if ENABLE_PROFILER
                Profiler.EndSample();
#endif
            }
        }

        public void SetBlendSource(RenderTexture input, bool both)
        {
            InitRenderTexture(ref blendSource[blendSourceIndex], input.width, input.height, 0, ShaderPropertyId.BlendSource);
            InitCommandBuffer();
            cmd.Clear();
            cmd.Blit(input, blendSource[blendSourceIndex]);
            if (both)
            {
                InitRenderTexture(ref blendSource[1 - blendSourceIndex], input.width, input.height, 0, ShaderPropertyId.BlendSource);
                cmd.Blit(input, blendSource[1 - blendSourceIndex]);
            }
            Graphics.ExecuteCommandBuffer(cmd);
            blendSourceIndex = 1 - blendSourceIndex;
        }

        public void BlendLayer(float blend, Material blendMaterial)
        {
            if (blendSource[0] == null || blendSource[1] == null) return;
            int src0 = blendSourceIndex;
            int src1 = 1 - blendSourceIndex;
            if (blend <= 0.0f)
            {
                Shader.SetGlobalTexture(settings.blurTextureName, blendSource[src0]);
            }
            else if (blend >= 1.0f)
            {
                Shader.SetGlobalTexture(settings.blurTextureName, blendSource[src1]);
            }
            else
            {
                InitRenderTexture(ref blendTarget, blendSource[src1].width, blendSource[src1].height, 0, ShaderPropertyId.BlendTarget);
                InitCommandBuffer();
                cmd.Clear();
                cmd.SetGlobalTexture(ShaderPropertyId.AcrylicBlendTexture, blendSource[src1]);
                cmd.SetGlobalFloat(ShaderPropertyId.AcrylicBlendFraction, blend);
                cmd.Blit(blendSource[src0], blendTarget, blendMaterial);
                Graphics.ExecuteCommandBuffer(cmd);
                Shader.SetGlobalTexture(settings.blurTextureName, blendTarget);
            }
        }

        public void SetTargetCamera(Camera newTargetCamera)
        {
            targetCamera = newTargetCamera;

            if (blur != null)
            {
                blur.targetCamera = newTargetCamera;
            }
        }

        #endregion

        #region private methods

        private void InitCommandBuffer()
        {
            if (cmd == null)
            {
                cmd = new CommandBuffer();
                cmd.name = ShaderPropertyId.AcrylicBlurPasses;
            }
        }

        private int PowerOf2(int i)
        {
            int powerOf2 = 1;
            while (i > 0)
            {
                i--;
                powerOf2 *= 2;
            }
            return powerOf2;
        }

        private void InitRenderTargets(int newWidth, int newHeight, int depth)
        {
            InitRenderTexture(ref renderTarget1, newWidth, newHeight, depth, ShaderPropertyId.RenderTarget1);
            InitRenderTexture(ref renderTarget2, newWidth, newHeight, depth, ShaderPropertyId.RenderTarget2);
        }

        private static void InitRenderTexture(ref RenderTexture texture, int newWidth, int newHeight, int depth, string name)
        {
            if (texture == null)
            {
                texture = new RenderTexture(newWidth, newHeight, depth, RenderTextureFormat.ARGB32);
                texture.name = name;
            }
            else if (texture.width != newWidth || texture.height != newHeight)
            {
                texture.Release();
                texture.width = newWidth;
                texture.height = newHeight;
                texture.Create();
            }
        }

        private AcrylicBlurFeature CreateBlurFeature(string name, RenderPassEvent blurMapCreation, Material blurMaterial, Camera targetCamera)
        {
            AcrylicBlurFeature blur = null;
            if (blurMaterial != null)
            {
                blur = ScriptableObject.CreateInstance<AcrylicBlurFeature>();
                blur.name = name;
                blur.renderPassEvent = blurMapCreation;
                blur.blur = settings.blurPasses;
                blur.downSample = settings.downSample;
                blur.textureName = settings.blurTextureName;
                blur.blurMaterial = blurMaterial;
                blur.targetCamera = targetCamera;
                if (useDualBlur) blur.SetBlurMethod(dualBlur);
            }
            return blur;
        }

        private ScriptableRendererFeature CreateRenderObjectsFeature(string name, RenderQueueType queue, RenderPassEvent blurMapCreation)
        {
            RenderObjects r = ScriptableObject.CreateInstance<RenderObjects>();
            r.name = name;
            r.settings.Event = blurMapCreation;
            r.settings.filterSettings.RenderQueueType = queue;
            r.settings.filterSettings.LayerMask = settings.renderLayers;
            return r;
        }

#if UNITY_2021_2_OR_NEWER
        private void MakeFeaturePersistent(UniversalRendererData rendererData, ScriptableRendererFeature feature)
#else
        private void MakeFeaturePersistent(ForwardRendererData rendererData, ScriptableRendererFeature feature)
#endif
        {
#if UNITY_EDITOR
            if (rendererData != null && feature != null)
            {
                UnityEditor.AssetDatabase.AddObjectToAsset(feature, rendererData);
                UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long _);
            }
#endif
        }

        private MaterialPropertyBlock blitProperties = new MaterialPropertyBlock();
        private void LocalBlit(CommandBuffer cmd, RenderTexture source, RenderTexture target, Material material)
        {
            cmd.SetRenderTarget(target);
            blitProperties.SetTexture(ShaderPropertyId.AcrylicBlurSource, source);
            cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, material, 0, 0, blitProperties);
        }

        private void DestroyScriptableObject(UnityEngine.Object o)
        {
            if (Application.isPlaying)
            {
                ScriptableObject.Destroy(o);
            }
            else
            {
                ScriptableObject.DestroyImmediate(o);
            }
        }

        #endregion
    }
}
#endif // GT_USE_URP
