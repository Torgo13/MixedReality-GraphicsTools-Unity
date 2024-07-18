// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if GT_USE_UGUI
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Microsoft.MixedReality.GraphicsTools
{
    /// <summary>
    /// Allows a 3D mesh to be rendered within a UnityUI canvas. 
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class CanvasElementMesh : MaskableGraphic
    {
        [Tooltip("The source mesh to use for populating the Graphic with vertex information.")]
        [SerializeField]
        [FormerlySerializedAs("Mesh")]
        private UnityEngine.Mesh mesh = null;

        /// <summary>
        /// The source mesh to use for populating the Graphic with vertex information.
        /// </summary>
        public UnityEngine.Mesh Mesh
        {
            get => mesh;
            set
            {
                mesh = value;
                SetVerticesDirty();
            }
        }

        [Tooltip("The main texture to use on the material.")]
        [SerializeField]
        private Texture texture = null;

        /// <summary>
        /// The main texture to use on the material.
        /// </summary>
        public Texture Texture
        {
            get
            {
                if (texture == null)
                {
                    return s_WhiteTexture;
                }

                return texture;
            }
            set
            {
                texture = value;
            }
        }

        [Tooltip("The normalized z-pivot that this mesh rotates around. (compare to RectTransform.pivot)")]
        [SerializeField]
        private float zPivot = 0f;

        /// <summary>
        /// The normalized z-pivot that this mesh rotates around. (compare to RectTransform.pivot)
        /// </summary>
        public float ZPivot { get => zPivot; set => zPivot = value; }

        [Tooltip("Whether this element should preserve its source mesh aspect ratio (scale).")]
        [SerializeField]
        private bool preserveAspect = true;

        /// <summary>
        /// Whether this element should preserve its source mesh aspect ratio (scale).
        /// </summary>
        public bool PreserveAspect
        {
            get => preserveAspect;
            set
            {
                preserveAspect = value;
                SetVerticesDirty();
            }
        }

        private UnityEngine.Mesh previousMesh = null;
        private Color previousColor = Color.white;
        private List<UIVertex> uiVertices = new List<UIVertex>();
        private List<int> uiIndices = new List<int>();

#region UIBehaviour Implementation

#if UNITY_EDITOR
        /// <summary>
        /// Enforces the parent canvas uses normal and tangent attributes.
        /// </summary>
        protected override void OnValidate()
        {
            base.OnValidate();

            EnableVertexAttributes();

            if (texture != null && material != null)
            {
                material.mainTexture = texture;
            }
        }
#endif // UNITY_EDITOR

        /// <summary>
        /// Enforces the parent canvas uses normal and tangent attributes.
        /// </summary>
        protected override void Start()
        {
            base.Start();

            EnableVertexAttributes();
        }

#endregion UIBehaviour Implementation

#region Graphic Implementation

        /// <inheritdoc/>
        public override Texture mainTexture
        {
            get
            {
                return Texture;
            }
        }

        /// <summary>
        /// Callback function when a UI element needs to generate vertices.
        /// </summary>
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            RefreshMesh();

            int uiVerticesCount = uiVertices.Count;
            if (Mesh == null || uiVerticesCount == 0)
            {
                return;
            }

            Vector3 meshSize = Mesh.bounds.size;
            Vector3 rectSize = rectTransform.rect.size;
            rectSize.z = meshSize.z;
            if (preserveAspect)
            {
                float meshRatio = meshSize.x / meshSize.y;
                float rectRatio = rectSize.x / rectSize.y;
                float scaler;

                // Wide
                if (meshSize.x > meshSize.y)
                {
                    scaler = rectRatio > meshRatio ? rectSize.y * meshRatio : rectSize.x;
                }
                else // Tall
                {
                    scaler = rectRatio > meshRatio ? rectSize.y : rectSize.x * (1.0f / meshRatio);
                }

                rectSize = new Vector3(scaler, scaler, scaler);
            }

            Vector3 rectPivot = rectTransform.pivot;
            rectPivot.z = ZPivot;

            using (UnityEngine.Pool.ListPool<UIVertex>.Get(out var uiVerticesTRS))
            {
                uiVerticesTRS.AddRange(uiVertices);

                // Scale, translate and rotate vertices.
                for (int i = 0; i < uiVerticesCount; i++)
                {
                    UIVertex vertex = uiVerticesTRS[i];

                    // Scale the vector from the normalized position to the pivot by the rect size.
                    vertex.position = Vector3.Scale(vertex.position - rectPivot, rectSize);

                    uiVerticesTRS[i] = vertex;
                }

                vh.AddUIVertexStream(uiVerticesTRS, uiIndices);
            }
        }

#endregion Graphic Implementation

        /// <summary>
        /// Determines if vertex attributes within the Mesh need to be re-cached.
        /// </summary>
        [ContextMenu("Refresh Mesh")]
        private void RefreshMesh()
        {
            if (previousMesh != Mesh ||
                previousColor != color ||
                uiVertices.Count == 0)
            {
                uiVertices.Clear();
                uiIndices.Clear();

                if (Mesh != null)
                {
                    List<Vector3> vertices = UnityEngine.Pool.ListPool<Vector3>.Get();
                    Mesh.GetVertices(vertices);
                    List<Color> colors = UnityEngine.Pool.ListPool<Color>.Get();
                    Mesh.GetColors(colors);
                    List<Vector2> uv0s = UnityEngine.Pool.ListPool<Vector2>.Get();
                    Mesh.GetUVs(0, uv0s);
                    List<Vector2> uv1s = UnityEngine.Pool.ListPool<Vector2>.Get();
                    Mesh.GetUVs(1, uv1s);
                    List<Vector2> uv2s = UnityEngine.Pool.ListPool<Vector2>.Get();
                    Mesh.GetUVs(2, uv2s);
                    List<Vector2> uv3s = UnityEngine.Pool.ListPool<Vector2>.Get();
                    Mesh.GetUVs(3, uv3s);
                    List<Vector3> normals = UnityEngine.Pool.ListPool<Vector3>.Get();
                    Mesh.GetNormals(normals);
                    List<Vector4> tangents = UnityEngine.Pool.ListPool<Vector4>.Get();
                    Mesh.GetTangents(tangents);

                    Vector3 rectPivot = new Vector3(0.5f, 0.5f, 0);
                    var bounds = Mesh.bounds;
                    Vector3 meshCenter = bounds.center;
                    Vector3 meshSize = bounds.extents;

                    float scaler = 0.5f / Mathf.Max(meshSize.x, meshSize.y);
                    
                    for (int i = 0, verticesCount = vertices.Count; i < verticesCount; ++i)
                    {
                        // Center the mesh at the origin.
                        // Normalize the mesh in a 1x1x1 cube.
                        // Center the mesh at the pivot.
                        Vector3 position = (vertices[i] - meshCenter) * scaler + rectPivot;
                        UIVertex vertex = new UIVertex
                        {
                            position = position,
                            normal = normals[i],
                            tangent = tangents[i]
                        };

                        if (i < colors.Count)
                        {
                            vertex.color = colors[i] * color;
                        }
                        else
                        {
                            vertex.color = color;
                        }

                        if (i < uv0s.Count)
                        {
                            vertex.uv0 = uv0s[i];
                        }

                        if (i < uv1s.Count)
                        {
                            vertex.uv1 = uv1s[i];
                        }

                        if (i < uv2s.Count)
                        {
                            vertex.uv2 = uv2s[i];
                        }

                        if (i < uv3s.Count)
                        {
                            vertex.uv3 = uv3s[i];
                        }

                        uiVertices.Add(vertex);
                    }

                    UnityEngine.Pool.ListPool<Vector3>.Release(vertices);
                    UnityEngine.Pool.ListPool<Color>.Release(colors);
                    UnityEngine.Pool.ListPool<Vector2>.Release(uv0s);
                    UnityEngine.Pool.ListPool<Vector2>.Release(uv1s);
                    UnityEngine.Pool.ListPool<Vector2>.Release(uv2s);
                    UnityEngine.Pool.ListPool<Vector2>.Release(uv3s);
                    UnityEngine.Pool.ListPool<Vector3>.Release(normals);
                    UnityEngine.Pool.ListPool<Vector4>.Release(tangents);

                    Mesh.GetTriangles(uiIndices, 0);
                }

                previousMesh = Mesh;
                previousColor = color;
            }
        }

        /// <summary>
        /// Ensures the parent canvas has normal and tangent attributes enabled.
        /// </summary>
        private void EnableVertexAttributes()
        {
            var canvas = GetComponentInParent<Canvas>();

            if (canvas != null)
            {
                canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.Normal;
                canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.Tangent;
            }
        }
    }
}
#endif // GT_USE_UGUI
