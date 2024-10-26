// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if GT_USE_URP
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// TODO - [Cameron-Micka] remove obsolete API.
#pragma warning disable 0618

namespace Microsoft.MixedReality.GraphicsTools
{
    /// <summary>
    /// Methods to perform dual filter bluring.
    /// </summary>
    public class AcrylicFilterDual : IDisposable
    {
        private int lastWidth;
        private int lastHeight;
        private int lastIterations;
        private List<RenderTexture> buffers;
        private Material filterMaterial;

        private const int blurOffset = 1;

#if OPTIMISATION_SHADERPARAMS
        private struct ShaderPropertyId
        {
            public static readonly int AcrylicBlurOffset = Shader.PropertyToID("_AcrylicBlurOffset");
            public static readonly int AcrylicHalfPixel = Shader.PropertyToID("_AcrylicHalfPixel");
            public static readonly int AcrylicBlurSource = Shader.PropertyToID("_AcrylicBlurSource");
        }
#endif // OPTIMISATION_SHADERPARAMS

        public AcrylicFilterDual(Material _material)
        {
            filterMaterial = _material;
            lastWidth = 0;
            lastHeight = 0;
            lastIterations = 0;
            buffers = new List<RenderTexture>();
        }

#if OPTIMISATION_IDISPOSABLE
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            FreeBuffers();
        }
#else
        public void Dispose()
        {
            FreeBuffers();
        }
#endif // OPTIMISATION_IDISPOSABLE

        public void QueueBlur(CommandBuffer cmd, RenderTexture image, int iterations)
        {
            if (image.width != lastWidth || image.height != lastHeight || iterations != lastIterations)
            {
                FreeBuffers();
                InitBuffers(image.width, image.height, iterations);
                lastWidth = image.width;
                lastHeight = image.height;
                lastIterations = iterations;
            }

            // Can't blur anything smaller than a 3x3 pixel image.
            if (buffers.Count == 0)
            {
                return;
            }

#if OPTIMISATION_SHADERPARAMS
            cmd.SetGlobalVector(ShaderPropertyId.AcrylicBlurOffset, Vector2.one * blurOffset);
#else
            cmd.SetGlobalVector("_AcrylicBlurOffset", Vector2.one * blurOffset);
#endif // OPTIMISATION_SHADERPARAMS
            
#if OPTIMISATION
            for (int i = 0, buffersCount = Mathf.Min(iterations, buffers.Count); i < buffersCount; i++)
#else
            for (int i = 0; (i < iterations) && (i < buffers.Count); i++)
#endif // OPTIMISATION
            {
#if OPTIMISATION_SHADERPARAMS
                cmd.SetGlobalVector(ShaderPropertyId.AcrylicHalfPixel, new Vector2(0.5f / buffers[i].width, 0.5f / buffers[i].height));
#else
                cmd.SetGlobalVector("_AcrylicHalfPixel", new Vector2(0.5f / buffers[i].width, 0.5f / buffers[i].height));
#endif // OPTIMISATION_SHADERPARAMS

                RenderTexture from = (i == 0) ? image : buffers[i - 1];
                LocalBlit(cmd, from, buffers[i], filterMaterial, 0);
            }

            for (int i = Mathf.Min(iterations - 1, buffers.Count - 1); i >= 0; i--)
            {
                RenderTexture to = (i == 0) ? image : buffers[i - 1];
#if OPTIMISATION_SHADERPARAMS
                cmd.SetGlobalVector(ShaderPropertyId.AcrylicHalfPixel, new Vector2(0.5f / to.width, 0.5f / to.height));
#else
                cmd.SetGlobalVector("_AcrylicHalfPixel", new Vector2(0.5f / to.width, 0.5f / to.height));
#endif // OPTIMISATION_SHADERPARAMS

                LocalBlit(cmd, buffers[i], to, filterMaterial, 1);
            }
        }

        public void ApplyBlur(string profileLabel, RenderTexture image, int iterations)
        {
            Profiler.BeginSample(profileLabel);
            CommandBuffer cmd = new CommandBuffer();
            QueueBlur(cmd, image, iterations);
            Graphics.ExecuteCommandBuffer(cmd);
            Profiler.EndSample();
        }

        private void InitBuffers(int width, int height, int iterations)
        {
            int nextWidth = (width + 1) / 2;
            int nextHeight = (height + 1) / 2;
            for (int i = 0; i < iterations && nextWidth > 1 && nextHeight > 1; i++)
            {
#if OPTIMISATION
                buffers.Add(RenderTexture.GetTemporary(nextWidth, nextHeight, 0, RenderTextureFormat.ARGB32));
#else
                buffers.Add(new RenderTexture(nextWidth, nextHeight, 0, RenderTextureFormat.ARGB32));
#endif // OPTIMISATION

                nextWidth = (nextWidth + 1) / 2;
                nextHeight = (nextHeight + 1) / 2;
            }
        }

        private void FreeBuffers()
        {
            for (int i = 0; i < buffers.Count; i++)
            {
#if OPTIMISATION
                RenderTexture.ReleaseTemporary(buffers[i]);
#else
                UnityEngine.Object.Destroy(buffers[i]);
#endif // OPTIMISATION
            }

            buffers.Clear();
        }

        private void LocalBlit(CommandBuffer cmd, RenderTexture source, RenderTexture target, Material material, int pass)
        {
            cmd.SetRenderTarget(target);
#if OPTIMISATION_SHADERPARAMS
            cmd.SetGlobalTexture(ShaderPropertyId.AcrylicBlurSource, source);
#else
            cmd.SetGlobalTexture("_AcrylicBlurSource", source);
#endif // OPTIMISATION_SHADERPARAMS

            cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, material, 0, pass);
        }
    }
}
#endif // GT_USE_URP
