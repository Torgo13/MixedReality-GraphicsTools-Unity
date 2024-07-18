// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if GT_USE_URP
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if !CUSTOM_URP
using System.Reflection;
#endif

namespace Microsoft.MixedReality.GraphicsTools
{
    /// <summary>
    /// URP utility class for commonly used constants, types and convenience methods.
    /// </summary>
    public static class URPUtility
    {
#if !CUSTOM_URP
        /// <summary>
        /// The universal render pipeline can have multiple renderers, this method returns the 
        /// ScriptableRendererData (features and settings) for a renderer at a given index.
        /// 
        /// Note, this data is not public so our only resort is brittle reflection to access 
        /// the data programmatically.
        /// </summary>
#if UNITY_2021_2_OR_NEWER
        public static UniversalRendererData GetRendererData(int rendererIndex)
#else
        public static ForwardRendererData GetRendererData(int rendererIndex)
#endif
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

            if (pipeline != null)
            {
                var propertyInfo = pipeline.GetType().GetField("m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic);

                if (propertyInfo != null)
                {
                    var renderers = propertyInfo.GetValue(pipeline) as ScriptableRendererData[];

                    if (renderers != null)
                    {
                        if (rendererIndex < renderers.Length)
                        {
#if UNITY_2021_2_OR_NEWER
                            return renderers[rendererIndex] as UniversalRendererData;
#else
                            return renderers[rendererIndex] as ForwardRendererData;
#endif
                        }
#if UNITY_EDITOR || DEBUG
                        else
                        {
                            Debug.LogError($"GetRendererData failed because rendererIndex is out of range. {renderers.Length} renderer(s) exist but index {rendererIndex} requested.");
                        }
#endif
                    }
#if UNITY_EDITOR || DEBUG
                    else
                    {
                        Debug.LogError("GetRendererData failed because Unity changed the internals of m_RendererDataList. Please file a bug!");
                    }
#endif
                }
#if UNITY_EDITOR || DEBUG
                else
                {
                    Debug.LogError("GetRendererData failed because Unity changed the internals of UniversalRenderPipelineAsset. Please file a bug!");
                }
#endif
            }
#if UNITY_EDITOR || DEBUG
            else
            {
                Debug.LogWarning("GetRendererData failed because the current pipeline is not set or not a UniversalRenderPipelineAsset");
            }
#endif

            return null;
        }
#else
        /// <summary>
        /// The universal render pipeline can have multiple renderers, this method returns the 
        /// ScriptableRendererData (features and settings) for a renderer at a given index.
        /// </summary>
#if UNITY_2021_2_OR_NEWER
        public static UniversalRendererData GetRendererData(int rendererIndex)
#else
        public static ForwardRendererData GetRendererData(int rendererIndex)
#endif
        {
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline)
            {
                var renderers = pipeline.rendererDataList;
                if (rendererIndex < renderers.Length)
                {
#if UNITY_2021_2_OR_NEWER
                    return renderers[rendererIndex] as UniversalRendererData;
#else
                    return renderers[rendererIndex] as ForwardRendererData;
#endif
                }
#if UNITY_EDITOR || DEBUG
                else
                {
                    Debug.LogError($"GetRendererData failed because rendererIndex is out of range. {renderers.Length} renderer(s) exist but index {rendererIndex} requested.");
                }
#endif
            }
#if UNITY_EDITOR || DEBUG
            else
            {
                Debug.LogWarning("GetRendererData failed because the current pipeline is not set or not a UniversalRenderPipelineAsset");
            }
#endif

            return null;
        }
#endif
    }
}
#endif // GT_USE_URP
