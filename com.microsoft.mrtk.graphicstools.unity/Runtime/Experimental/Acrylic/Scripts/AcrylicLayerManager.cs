// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if GT_USE_URP
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Microsoft.MixedReality.GraphicsTools
{
    /// <summary>
    /// Manages creating and updating blurred background maps for use with the acrylic material.
    /// </summary>
    public class AcrylicLayerManager : MonoBehaviour
    {
        private static AcrylicLayerManager instance;

        public static AcrylicLayerManager Instance
        {
            get { return instance; }
        }

        [Experimental]
        [Tooltip("Whether this platforms supports creating a blurred acrylic map")]
        [SerializeField]
        private bool acrylicSupported = true;

        public bool AcrylicSupported
        {
            get { return acrylicSupported; }
            set { acrylicSupported = value; }
        }

        public enum AcrylicMethod { CopyFramebuffer, RenderToTexture }

        [Tooltip("Capture method for background image")]
        [SerializeField]
        private AcrylicMethod captureMethod = AcrylicMethod.CopyFramebuffer;

        [Tooltip("Use 16-bit or 24-bit depth buffer")]
        [SerializeField]
        private bool _24BitDepthBuffer = false;

        public bool UseOnlyMainCamera
        {
            get { return useOnlyMainCamera; }
            private set { useOnlyMainCamera = value; }
        }

        [Tooltip("When true the targetCamera is always updated to be the camera tagged as MainCamera.")]
        [SerializeField]
        private bool useOnlyMainCamera = false;

        [Tooltip("Which camera to add the blur pass(es) to. None applies to all cameras. Setting UseOnlyMainCamera to true will overwrite this.")]
        [SerializeField]
        private Camera targetCamera = null;

        public int RendererIndex
        {
            get { return rendererIndex; }
            set 
            { 
                if (initialized)
                {
                    Debug.LogWarning("Failed to set the render index because the layer manager is already initialized.");
                    return;
                }

                rendererIndex = value; 
            }
        }

        [Tooltip("Which renderer to use in the UniversalRenderPipelineAsset.")]
        [SerializeField]
        private int rendererIndex = 0;

        public enum BlurMethod { Kawase, Dual }

        [Header("Blur")]

        [SerializeField]
        private BlurMethod filterMethod = BlurMethod.Kawase;

        public BlurMethod FilterMethod
        {
            get { return filterMethod; }
            set
            {
                if (initialized)
                {
                    Debug.LogWarning("Failed to set the filter method because the layer manager is already initialized.");
                    return;
                }

                filterMethod = value;
            }
        }

        [Tooltip("Material for kawase blur")]
        [SerializeField]
        private Material kawaseFilterMaterial = null;

        public Material KawaseFilterMaterial
        {
            get { return kawaseFilterMaterial; }
            private set { kawaseFilterMaterial = value; }
        }

        [Tooltip("Material for dual blur filter")]
        [SerializeField]
        private Material dualFilterMaterial = null;

        public Material DualFilterMaterial
        {
            get { return dualFilterMaterial; }
            private set { dualFilterMaterial = value; }
        }

        [Header("Blur Map Update Options")]

        [Tooltip("Whether to automatically update blur map")]
        [SerializeField]
        private bool autoUpdateBlurMap = true;

        public bool AutoUpdateBlurMap
        {
            get { return autoUpdateBlurMap; }
            set { autoUpdateBlurMap = value; }
        }

        [Tooltip("How often to record a new background image for the blur map")]
        [SerializeField]
        [Range(1, 60)]
        private int updatePeriod = 1;

        [Tooltip("Frames over which to blend from old to new blur map.")]
        [SerializeField]
        [Range(0, 60)]
        private int blendFrames = 0;

        [Tooltip("Material used when blending between old and new blur maps.")]
        [SerializeField]
        private Material blendMaterial = null;

        [SerializeField]
        private List<AcrylicLayer.Settings> layers;

        public List<AcrylicLayer.Settings> Layers
        {
            get { return layers; }
            private set { layers = value; }
        }

        [SerializeField]
        [Tooltip("Event called before manager initilizaion.")]
        private UnityEvent onPreInitializeEvent;

        public UnityEvent OnPreInitializeEvent
        {
            get { return onPreInitializeEvent; }
            private set { onPreInitializeEvent = value; }
        }

        #region private properties

        private List<AcrylicLayer> layerData = new List<AcrylicLayer>();
#if UNITY_2021_2_OR_NEWER
        private UniversalRendererData rendererData = null;
#else
        private ForwardRendererData rendererData = null;
#endif
        private bool initialized = false;
        private bool acrylicActive = true;
#if OPTIMISATION
#else
        private const string namePrefix = "AcrylicBlur";
#endif // OPTIMISATION
        private bool ExecuteBeforeRenderAdded = false;
        private Coroutine updateRoutine = null;
        private IntermediateTextureMode previousIntermediateTextureMode;

        #endregion

        #region Monobehavior methods

        private void OnDestroy()
        {
            // Reset the intermediate texture mode.
            if (rendererData != null)
            {
                rendererData.intermediateTextureMode = previousIntermediateTextureMode;
            }

            switch (captureMethod)
            {
                case AcrylicMethod.CopyFramebuffer:
                    RemoveAllLayers();
                    break;

                case AcrylicMethod.RenderToTexture:
                    if (ExecuteBeforeRenderAdded)
                    {
                        RenderPipelineManager.beginCameraRendering -= ExecuteBeforeCameraRender;
                    }
                    break;

                default:
                    break;
            }

            for (int i = 0; i < layerData.Count; ++i)
            {
                layerData[i].Dispose();
            }

            layerData.Clear();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Debug.LogErrorFormat("An instance of the AcrylicLayerManager already exists on gameobject {0}", instance.name);
                return;
            }

            instance = this;

            if (onPreInitializeEvent != null)
            {
                onPreInitializeEvent.Invoke();
            }

            InitializeBlurTexturesToBlack();
        }

        private void Start()
        {
            if (AcrylicSupported)
            {
                Initialize();
            }
        }

        private void OnValidate()
        {
            InitializeBlurTexturesToBlack();
        }

        #endregion

        #region Public methods

        public void EnableLayer(int i)
        {
            if (!AcrylicSupported) return;

            Initialize();
            if (i >= 0 && i < layerData.Count)
            {
                layerData[i].activeCount++;
                if (layerData[i].activeCount == 1)
                {
                    layerData[i].frameCount = 0;
                    layerData[i].firstFrameRendered = false;
                    if (captureMethod == AcrylicMethod.CopyFramebuffer)
                    {
                        UpdateActiveLayers();
                    }

                    StartUpdateRoutine();
                }
            }

#if CUSTOM_URP
            if (rendererData != null)
                rendererData.intermediateTextureMode = IntermediateTextureMode.Always;
#endif // CUSTOM_URP
        }

        public void DisableLayer(int i)
        {
            if (!AcrylicSupported) return;

            Initialize();
            if (i >= 0 && i < layerData.Count && layerData[i].activeCount > 0)
            {
                layerData[i].activeCount--;
                if (layerData[i].activeCount == 0 && rendererData != null)
                {
                    UpdateActiveLayers();
                }
            }

#if CUSTOM_URP
            if (rendererData != null)
                rendererData.intermediateTextureMode = previousIntermediateTextureMode;
#endif // CUSTOM_URP
        }

        public bool LayerVisible(int i)
        {
            return (i >= 0 && i < layerData.Count && layerData[i].activeCount > 0 && rendererData != null);
        }

        public bool AcrylicActive
        {
            get
            {
                return acrylicActive && AcrylicSupported;
            }
            set
            {
                if (value != acrylicActive)
                {
                    acrylicActive = value;
                    if (AcrylicSupported)
                    {
                        UpdateActiveLayers();
                    }
                }
            }
        }

        public void SetTargetCamera(Camera newtargetCamera)
        {
            if (targetCamera != newtargetCamera)
            {
                targetCamera = newtargetCamera;

                foreach (AcrylicLayer layer in layerData)
                {
                    layer.SetTargetCamera(newtargetCamera);
                }
            }
        }

        #endregion

        #region private methods

        private void Initialize()
        {
            if (!initialized && AcrylicSupported)
            {
                initialized = true;
                rendererData = URPUtility.GetRendererData(rendererIndex);

                if (rendererData != null)
                {
                    RemoveExistingAcrylicPasses();

                    // Previously, URP would force rendering to go through an intermediate renderer if the Renderer had any Renderer Features active. On some platforms, this has
                    // significant performance implications so we only want to enable this when we need it (which we do for magnification).
                    previousIntermediateTextureMode = rendererData.intermediateTextureMode;
#if CUSTOM_URP
#else
                    rendererData.intermediateTextureMode = IntermediateTextureMode.Always;
#endif // CUSTOM_URP
                }

                CreateLayers();

                if (captureMethod == AcrylicMethod.RenderToTexture)
                {
                    ExecuteBeforeRenderAdded = true;
                    RenderPipelineManager.beginCameraRendering += ExecuteBeforeCameraRender;
                }
            }
        }

        private void InitializeBlurTexturesToBlack()
        {
            if (layers != null)
            {
                foreach (AcrylicLayer.Settings layer in layers)
                {
                    if (!string.IsNullOrEmpty(layer.blurTextureName))
                    {
                        Shader.SetGlobalTexture(layer.blurTextureName, Texture2D.blackTexture);
                    }
                }
            }
        }

        private void CreateLayers()
        {
            for (int i = 0; i < layers.Count; i++)
            {
                layerData.Add(CreateLayer(layers[i], i));
            }
        }

        private void AddAllLayers()
        {
            if (rendererData != null)
            {
                for (int i = 0; i < layerData.Count; i++)
                {
                    EnableLayer(i);
                }
            }
        }

        private AcrylicLayer CreateLayer(AcrylicLayer.Settings settings, int index)
        {
            AcrylicLayer layer = new AcrylicLayer(targetCamera, settings, index, _24BitDepthBuffer ? 24 : 16, filterMethod == BlurMethod.Dual, kawaseFilterMaterial, dualFilterMaterial);
            if (captureMethod == AcrylicMethod.CopyFramebuffer)
            {
                layer.CreateRendererFeatures();
            }

#if LEGACY_ACRYLIC
#else
            layer.SetAcrylicBlurFeature(rendererData);
#endif // LEGACY_ACRYLIC

            return layer;
        }

        private void UpdateActiveLayers()
        {
            RemoveAllLayers();
            if (AcrylicActive)
                AddActiveLayers();

#if LEGACY_ACRYLIC
#else
            for (int i = 0; i < layerData.Count; i++)
            {
                layerData[i].EnableLayerRendererFeatures(AcrylicActive);

                if (AcrylicActive && layerData[i].activeCount > 0 && layerData[i].CaptureNextFrame)
                {
                    layerData[i].UpdateLayerRendererFeatures(updatePeriod < 2 && autoUpdateBlurMap);
                }
            }
#endif // LEGACY_ACRYLIC
        }

        [System.Diagnostics.Conditional("LEGACY_ACRYLIC")]
        private void RemoveExistingAcrylicPasses()
        {
            List<ScriptableRendererFeature> passes = rendererData.rendererFeatures;
            for (int i = passes.Count - 1; i >= 0; i--)
            {
                if (passes[i].name.Contains("Acrylic"))
                {
#if (UNITY_EDITOR)
                    UnityEditor.AssetDatabase.RemoveObjectFromAsset(passes[i]);
#endif
                    rendererData.rendererFeatures.Remove(passes[i]);
                }
            }
        }

        private bool AnyLayersNeedUpdating()
        {
            for (int i = 0; i < layerData.Count; i++)
            {
                if (layerData[i].activeCount > 0)
                {
                    if (autoUpdateBlurMap || !layerData[i].FirstFrameGenerated) return true;
                }
            }

            return false;
        }

#endregion

        #region Render to texture methods

        private void ExecuteBeforeCameraRender(ScriptableRenderContext context, Camera camera)
        {
            if (captureMethod == AcrylicMethod.RenderToTexture)
            {
                for (int i = 0; i < layerData.Count; i++)
                {
                    if (layerData[i].activeCount > 0 && layerData[i].frameCount == 0)
                    {
                        layerData[i].RenderToTexture(context, camera, updatePeriod, blendFrames, CumulativeLayerMask(i));
                    }
                }
            }
        }

        // returns the union of the current layer mask and all layer masks 'above' it
        private LayerMask CumulativeLayerMask(int layer)
        {
            LayerMask mask = layers[layer].renderLayers;
            for (int i = layer + 1; i < layers.Count; i++)
            {
                mask |= layers[i].renderLayers;
            }

            return mask;
        }

        #endregion

        #region Periodic update methods

        private void StartUpdateRoutine()
        {
            if (updateRoutine == null)
            {
                updateRoutine = StartCoroutine(UpdateRoutine());
            }
        }

        private IEnumerator UpdateRoutine()
        {
#if OPTIMISATION_UNITY
            Camera mainCamera = null;
#endif // OPTIMISATION_UNITY

            while (AnyLayersNeedUpdating())
            {
                bool updateActiveFeatures = false;
#if OPTIMISATION
                for (int i = 0, layerDataCount = layerData.Count; i < layerDataCount; i++)
#else
                for (int i = 0; i < layerData.Count; i++)
#endif // OPTIMISATION
                {
                    if (layerData[i].activeCount > 0)
                    {
                        if (UseOnlyMainCamera)
                        {
#if OPTIMISATION_UNITY
                            if (mainCamera == null)
                                mainCamera = Camera.main;

                            layerData[i].SetTargetCamera(mainCamera);
#else
                            layerData[i].SetTargetCamera(Camera.main);
#endif // OPTIMISATION_UNITY
                        }

                        layerData[i].UpdateFrame(rendererData, captureMethod == AcrylicMethod.CopyFramebuffer, updatePeriod, blendFrames, blendMaterial, autoUpdateBlurMap);
                        if (captureMethod == AcrylicMethod.CopyFramebuffer)
                        {
                            bool inList = layerData[i].InFeaturesList(rendererData);
                            if (layerData[i].CaptureNextFrame)
                            {
                                if (!inList) updateActiveFeatures = true;
                                if (updatePeriod == 1 && autoUpdateBlurMap) layerData[i].ForceCaptureNextFrame();  //needed if updatePeriod changed in editor
                            }
                            else
                            {
                                if (inList) updateActiveFeatures = true;
                            }
                        }
                    }
                }

                if (updateActiveFeatures) UpdateActiveLayers();
                
                yield return null;
            }

            updateRoutine = null;
        }
        #endregion

        #region Copy framebuffer related methods

        [System.Diagnostics.Conditional("LEGACY_ACRYLIC")]
        private void AddActiveLayers()
        {
            if (captureMethod != AcrylicMethod.CopyFramebuffer) return;

            for (int i = 0; i < layerData.Count; i++)
            {
                if (layerData[i].activeCount > 0 && layerData[i].CaptureNextFrame)
                {
                    layerData[i].AddLayerRendererFeatures(rendererData, updatePeriod < 2 && autoUpdateBlurMap);
                }
            }
        }

        [System.Diagnostics.Conditional("LEGACY_ACRYLIC")]
        private void RemoveAllLayers()
        {
            if (captureMethod != AcrylicMethod.CopyFramebuffer) return;

            for (int i = 0; i < layerData.Count; i++)
            {
                layerData[i].RemoveLayerRendererFeatures(rendererData);
            }
        }
        #endregion
    }
}
#endif // GT_USE_URP
