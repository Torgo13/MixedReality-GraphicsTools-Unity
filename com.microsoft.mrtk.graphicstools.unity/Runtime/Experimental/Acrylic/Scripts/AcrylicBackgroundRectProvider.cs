// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#define OPTIMISATION

#if GT_USE_URP
using UnityEngine;
using UnityEngine.UI;

namespace Microsoft.MixedReality.GraphicsTools
{
    /// <summary>
    /// Automatically blurs an image and passes the image and rect size/location into materials which will overlay the rect.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("Scripts/GraphicsTools/AcrylicBackgroundRectProvider")]
    public class AcrylicBackgroundRectProvider : BaseMeshEffect
    {
#if CUSTOM
#else
        [Experimental]
#endif // CUSTOM
        [Tooltip("List of materials to apply the _BlurBackgroundRect and _blurTexture to.")]
        [SerializeField]
        private Material[] materials = null;

        /// <summary>
        /// "List of materials to apply the _BlurBackgroundRect and _blurTexture to."
        /// </summary>
        public Material[] Materials
        {
            get => materials;
            set
            {
                materials = value;
                UpdateMaterialsProperties();
            }
        }

        [Tooltip("List of Graphic components to apply the _BlurBackgroundRect and _blurTexture to. Use this if your target has a material which changes or is instanced at runtime.")]
        [SerializeField]
        private Graphic[] graphics = null;

        /// <summary>
        /// "List of Graphic components to apply the _BlurBackgroundRect and _blurTexture to. Use this if your target has a material which changes or is instanced at runtime."
        /// </summary>
        public Graphic[] Graphics
        {
            get => graphics;
            set
            {
                graphics = value;
                InstanceGraphicComponents();
                UpdateMaterialsProperties();
            }
        }

        /// <summary>
        /// Should Graphics components create unique materials?
        /// </summary>
        public bool UseInstanceMaterials
        {
            get { return instanceMaterials; }
            set
            {
                if (instanceMaterials != value)
                {
                    instanceMaterials = value;

                    InstanceGraphicComponents();
                    UpdateMaterialsProperties();
                }
            }
        }

        [Tooltip("Should Graphics components create unique materials?")]
        [SerializeField]
        private bool instanceMaterials = false;

        [Tooltip("The index of the layer in the AcrylicLayerManager to copy settings from.")]
        [SerializeField]
        private int layerIndex = 0;

        [Tooltip("The material to use when copying the source texture to a render texture for blurring. (If none is specified, uses the default Unity blit material.)")]
        [SerializeField]
        private Material blitMaterial = null;

        [Tooltip("The destination color to copy the source texture onto when using a custom blit material.")]
        [SerializeField]
        private Color blitColor = Color.black;

        /// <summary>
        /// Access to the pre-blurred texture.
        /// </summary>
        public Texture SourceTexture
        {
            get
            {
                Texture output = null;
#if OPTIMISATION_TRYGET
                if (TryGetComponent<Image>(out var image))
#else
                Image image = GetComponent<Image>();

                if (image != null)
#endif // OPTIMISATION_TRYGET
                {
#if OPTIMISATION
                    if (image.sprite != null)
                    {
                        output = image.sprite.texture;
                    }
#else
                    output = (image.sprite != null) ? image.sprite.texture : null;
#endif // OPTIMISATION
                }
                else
                {
#if OPTIMISATION_TRYGET
                    if (TryGetComponent<RawImage>(out var rawImage))
#else
                    RawImage rawImage = GetComponent<RawImage>();

                    if (rawImage != null)
#endif // OPTIMISATION_TRYGET
                    {
                        output = rawImage.texture;
                    }
                }

                return output;
            }
        }

        /// <summary>
        /// Access to the result of BlurImageTexture.
        /// </summary>
        public Texture BlurredTexture
        {
            get => source;
        }

        private Canvas canvas = null;
        private RenderTexture source = null;
        private RenderTexture destination = null;
#if OPTIMISATION_SHADERPARAMS
        private static readonly int rectNameID = Shader.PropertyToID("_BlurBackgroundRect");
        private static readonly int textureID = Shader.PropertyToID("_blurTexture");
#else
        private int rectNameID = 0;
        private int textureID = 0;
#endif // OPTIMISATION_SHADERPARAMS
        private bool hasBlurred = false;

        /// <summary>
        /// Blurs the image at startup.
        /// </summary>
        protected override void Start()
        {
            base.Start();

            // Avoid blurring twice if another script called BlurImageTexture before start.
            if (!hasBlurred)
            {
                BlurImageTexture();
            }
        }

        /// <summary>
        /// Updates the material properties each frame.
        /// </summary>
        protected void Update()
        {
            UpdateMaterialsProperties();
        }

        /// <summary>
        /// Cleans up all rendering resources.
        /// </summary>
        protected override void OnDestroy()
        {
            base.OnDestroy();

            if (source != null)
            {
#if OPTIMISATION
                RenderTexture.ReleaseTemporary(source);
#else
                source.Release();
#endif // OPTIMISATION
                source = null;
            }

            if (destination != null)
            {
#if OPTIMISATION
                RenderTexture.ReleaseTemporary(destination);
#else
                destination.Release();
#endif // OPTIMISATION
                destination = null;
            }

            // Restore the blit material to its initial state in case it was dirtied during BlurImageTexture.
            MaterialRestorer.Restore(blitMaterial);
        }

        /// <summary>
        /// Update materials whenever the mesh is modified.
        /// </summary>
        public override void ModifyMesh(VertexHelper vh)
        {
            UpdateMaterialsProperties();
        }

        /// <summary>
        /// Iterates over all materials and graphics and applies the latest blur properties.
        /// </summary>
        public void UpdateMaterialsProperties()
        {
            if (!Application.isPlaying)
            {
                return;
            }

#if OPTIMISATION
            bool canvasFound = canvas != null;
            if (!canvasFound)
#else
            if (canvas == null)
#endif // OPTIMISATION
            {
                canvas = GetComponentInParent<Canvas>();
                canvasFound = canvas != null;
#if OPTIMISATION_SHADERPARAMS
#else
                rectNameID = Shader.PropertyToID("_BlurBackgroundRect");
                textureID = Shader.PropertyToID("_blurTexture");
#endif // OPTIMISATION_SHADERPARAMS
            }

#if OPTIMISATION
            if (canvasFound && transform is RectTransform rectTransform)
            {
#else
            if (canvas != null)
            {
                var rectTransform = transform as RectTransform;
#endif // OPTIMISATION
                Vector3 minCorner = TransformToCanvas(rectTransform.rect.min);
                Vector3 maxCorner = TransformToCanvas(rectTransform.rect.max);
                Vector4 rect = new Vector4(minCorner.x, minCorner.y, maxCorner.x, maxCorner.y);

                if (materials != null)
                {
#if OPTIMISATION
                    for (int i = 0, materialsLength = materials.Length; i < materialsLength; i++)
                    {
                        Material material = materials[i];
#else
                    foreach (Material material in materials)
                    {
#endif // OPTIMISATION
                        if (material != null)
                        {
                            material.SetVector(rectNameID, rect);
                            material.SetTexture(textureID, source);
                        }
                    }
                }

                if (graphics != null)
                {
#if OPTIMISATION
                    for (int i = 0, graphicsLength = graphics.Length; i < graphicsLength; i++)
                    {
                        Graphic graphic = graphics[i];
#else
                    foreach (Graphic graphic in graphics)
                    {
#endif // OPTIMISATION
                        if (graphic != null && graphic.materialForRendering != null)
                        {
                            graphic.materialForRendering.SetVector(rectNameID, rect);
                            graphic.materialForRendering.SetTexture(textureID, source);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Blurs the texture used by the local Image or RawImage component based on settings in the AcrylicLayerManager.
        /// </summary>
        public bool BlurImageTexture()
        {
            if (!Application.isPlaying)
            {
                return false;
            }

            // Find the texture to blur on local components.
            Texture textureToBlur = SourceTexture;

            if (textureToBlur == null)
            {
#if UNITY_EDITOR || DEBUG
                Debug.LogWarningFormat("Failed to find a texture to blur on {0} Image or RawImage components.", gameObject.name);
#endif // UNITY_EDITOR || DEBUG

                return false;
            }

            // Cache a local render target so that we aren't constantly creating new ones.
            int width = textureToBlur.width;
            int height = textureToBlur.height;

#if OPTIMISATION
            AcrylicLayer.InitRenderTexture(ref source, width, height, 0, gameObject.name);
#else
            if (source == null)
            {
                source = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
                source.name = gameObject.name;
            }
            else if (source.width != width || source.height != height)
            {
                source.Release();
                source.width = width;
                source.height = height;
                source.Create();
            }
#endif // OPTIMISATION

            // Blit the texture into the source render texture.
            // Note, using Blit rather than CopyTexture because the source texture is often compressed.
            if (blitMaterial != null)
            {
                MaterialRestorer.Capture(blitMaterial);
                blitMaterial.mainTexture = textureToBlur;
                blitMaterial.color = blitColor;
                UnityEngine.Graphics.Blit(textureToBlur, source, blitMaterial);
            }
            else
            {
                UnityEngine.Graphics.Blit(textureToBlur, source);
            }

            // Acquire the AcrylicLayerManager to copy settings from.
            if (AcrylicLayerManager.Instance == null)
            {
#if UNITY_EDITOR || DEBUG
                Debug.LogWarning("An AcrylicLayerManager does not exist. The image texture will not be blurred.");
#endif // UNITY_EDITOR || DEBUG

                return false;
            }

            if (AcrylicLayerManager.Instance.Layers.Count < layerIndex)
            {
#if UNITY_EDITOR || DEBUG
                Debug.LogWarningFormat("The AcrylicLayerManager does not contain enough layers. Request layer {0} but contains {1} layers. The image texture will not be blurred.",
                                       layerIndex,
                                       AcrylicLayerManager.Instance.Layers.Count);
#endif // UNITY_EDITOR || DEBUG

                return false;
            }

            AcrylicLayer layer = new AcrylicLayer(null,
                                                  AcrylicLayerManager.Instance.Layers[layerIndex],
                                                  0,
                                                  0,
                                                  AcrylicLayerManager.Instance.FilterMethod == AcrylicLayerManager.BlurMethod.Dual,
                                                  AcrylicLayerManager.Instance.KawaseFilterMaterial,
                                                  AcrylicLayerManager.Instance.DualFilterMaterial);

            layer.ApplyBlur(ref source, ref destination);

            layer.Dispose();

            InstanceGraphicComponents();
            UpdateMaterialsProperties();

            hasBlurred = true;

            return true;
        }

        private void InstanceGraphicComponents()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (!instanceMaterials)
            {
                return;
            }

            if (graphics == null)
            {
                return;
            }
            
#if OPTIMISATION
            for (int i = 0, graphicsLength = graphics.Length; i < graphicsLength; i++)
            {
                Graphic graphic = graphics[i];
#else
            foreach (Graphic graphic in graphics)
            {
#endif // OPTIMISATION
                if (graphic != null)
                {
                    if (!MaterialInstance.IsInstance(graphic.material))
                    {
                        graphic.material = MaterialInstance.Instance(graphic.material);
                    }
                }
            }
        }

        private Vector3 TransformToCanvas(Vector3 pos)
        {
            Vector3 posWorld = transform.TransformPoint(pos);
            return canvas.transform.InverseTransformPoint(posWorld);
        }
    }
}
#endif
