﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using UnityEngine;

// WebGL doesn't support threaded operations.
#if !UNITY_WEBGL
using System.Threading.Tasks;
#endif

namespace Microsoft.MixedReality.GraphicsTools
{
    /// <summary>
    /// Component which can be used to automatically generate smoothed normals on a mesh and pack
    /// those normals into a UV set. Smoothed normals can be used for a variety of effects including
    /// extruding disjoint meshes along a vertex normal. This behavior is designed to be used in conjunction
    /// with the Graphics Tools/Standard shader which assumes smoothed normals are packed into the 3rd UV set.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [AddComponentMenu("Scripts/GraphicsTools/MeshSmoother")]
    public class MeshSmoother : MonoBehaviour
    {
        private const int smoothNormalUVChannel = 2;

        [Tooltip("Should this component automatically smooth normals on awake?")]
        [SerializeField]
        private bool smoothNormalsOnAwake = false;

        private MeshFilter meshFilter = null;
        private UnityEngine.Mesh originalMesh;

        /// <summary>
        /// Helper class to track mesh references.
        /// </summary>
        private class MeshReference
        {
            public UnityEngine.Mesh Mesh;
            private int referenceCount;

            public MeshReference(UnityEngine.Mesh mesh)
            {
                Mesh = mesh;
                referenceCount = 1;
            }

            public void Increment()
            {
                ++referenceCount;
            }

            public void Decrement()
            {
                --referenceCount;
            }

            public bool IsReferenced()
            {
                return referenceCount > 0;
            }
        }

        private static Dictionary<UnityEngine.Mesh, MeshReference> processedMeshes = new Dictionary<UnityEngine.Mesh, MeshReference>();

        /// <summary>
        /// Performs normal smoothing on the current mesh filter associated with this component synchronously.
        /// This method will not try and re-smooth meshes which have already been smoothed.
        /// </summary>
        public void SmoothNormals()
        {
            UnityEngine.Mesh mesh;

            // No need to do any smoothing if this mesh has already been processed.
            if (AcquirePreprocessedMesh(out mesh))
            {
                return;
            }

            var result = CalculateSmoothNormals(mesh.vertices, mesh.normals);
            mesh.SetUVs(smoothNormalUVChannel, result);
        }

        /// <summary>
        /// Performs normal smoothing on the current mesh filter associated with this component asynchronously.
        /// This method will not try and re-smooth meshes which have already been smoothed.
        /// </summary>
        /// <returns>A task which will complete once normal smoothing is finished.</returns>
#if !UNITY_WEBGL
        public Task SmoothNormalsAsync()
        {
            UnityEngine.Mesh mesh;

            // No need to do any smoothing if this mesh has already been processed.
            if (AcquirePreprocessedMesh(out mesh))
            {
                return Task.CompletedTask;
            }

            // Create a copy of the vertices and normals and apply the smoothing in an async task.
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var asyncTask = Task.Run(() => CalculateSmoothNormals(vertices, normals));

            // Once the async task is complete, apply the smoothed normals to the mesh on the main thread.
            return asyncTask.ContinueWith((i) =>
            {
                mesh.SetUVs(smoothNormalUVChannel, i.Result);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
#endif

        #region MonoBehaviour Implementation

        /// <summary>
        /// Applies smoothing asynchronously if specified by the inspector property.
        /// </summary>
        private void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();

            if (smoothNormalsOnAwake)
            {
                // WebGL doesn't support threaded operations.
#if UNITY_WEBGL
                SmoothNormals();
#else
                SmoothNormalsAsync();
#endif
            }
        }

        /// <summary>
        /// Clean up any meshes which were created if no longer referenced.
        /// </summary>
        private void OnDestroy()
        {
            if (originalMesh != null)
            {
                meshFilter.sharedMesh = originalMesh;
            }

            MeshReference meshReference;
            var sharedMesh = meshFilter.sharedMesh;

            if (sharedMesh != null &&
                processedMeshes.TryGetValue(sharedMesh, out meshReference))
            {
                meshReference.Decrement();

                if (!meshReference.IsReferenced())
                {
                    Destroy(meshReference.Mesh);
                    processedMeshes.Remove(sharedMesh);
                }
            }
        }

        /// <summary>
        /// Clean up dictionary before quit play mode if enabled Reload Domain
        /// More information: https://docs.unity3d.com/Manual/DomainReloading.html
        /// </summary>
        private void OnApplicationQuit()
        {
            processedMeshes.Clear();
        }

        #endregion MonoBehaviour Implementation

        /// <summary>
        /// Safely acquires a mesh for processing. Checks for meshes which have already been processed and increments reference counts.
        /// </summary>
        /// <param name="mesh">A reference to the mesh which was already processed or is ready to be processed. Null if the mesh cannot be processed.</param>
        /// <returns>True if the mesh was already processed, false otherwise.</returns>
        private bool AcquirePreprocessedMesh(out UnityEngine.Mesh mesh)
        {
            if (meshFilter == null)
            {
                meshFilter = GetComponent<MeshFilter>();
            }

            // No mesh filter, mesh cannot be processed, so return a null mesh.
            if (meshFilter == null)
            {
                mesh = null;

                return true;
            }

            originalMesh = meshFilter.sharedMesh;

            // No mesh , mesh cannot be processed, so return a null mesh.
            if (originalMesh == null)
            {
                mesh = null;

                return true;
            }

            // A non-readable mesh cannot be processed, so return a null mesh.
            if (originalMesh.isReadable == false)
            {
                Debug.LogWarning($"Mesh smoothing failed because {originalMesh.name} is not readable. Check \"Read/Write Enabled\" in the mesh's import settings.");
                mesh = null;

                return true;
            }

            MeshReference meshReference;

            // If this mesh has already been processed, apply the preprocessed mesh and increment the reference count.
            if (processedMeshes.TryGetValue(originalMesh, out meshReference))
            {
                meshReference.Increment();
                mesh = meshReference.Mesh;
                meshFilter.mesh = mesh;

                return true;
            }

            // Clone the mesh, and create a mesh reference which can be keyed off either the original mesh or cloned mesh.
            mesh = meshFilter.mesh;
            meshReference = new MeshReference(mesh);
            processedMeshes[mesh] = meshReference;
            processedMeshes[originalMesh] = meshReference;

            return false;
        }

        /// <summary>
        /// This method groups vertices in a mesh that share the same location in space then averages the normals of those vertices.
        /// For example, if you imagine the 3 vertices that make up one corner of a cube. Normally there will be 3 normals facing in the direction
        /// of each face that touches that corner. This method will take those 3 normals and average them into a normal that points in the
        /// direction from the center of the cube to the corner of the cube.
        /// </summary>
        /// <param name="vertices">A list of vertices that represent a mesh.</param>
        /// <param name="normals">A list of normals that correspond to each vertex passed in via the vertices param.</param>
        /// <returns>A list of normals which are smoothed, or averaged, based on share vertex position.</returns>
        private static List<Vector3> CalculateSmoothNormals(Vector3[] vertices, Vector3[] normals)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();

            // Group all vertices that share the same location in space.
            var groupedVerticies = new Dictionary<Vector3, List<KeyValuePair<int, Vector3>>>();
            for (int i = 0; i < vertices.Length; ++i)
            {
                var vertex = vertices[i];
                List<KeyValuePair<int, Vector3>> group;

                if (!groupedVerticies.TryGetValue(vertex, out group))
                {
                    group = new List<KeyValuePair<int, Vector3>>();
                    groupedVerticies[vertex] = group;
                }

                group.Add(new KeyValuePair<int, Vector3>(i, vertex));
            }

            var smoothNormals = new List<Vector3>(normals);

            // If we don't hit the degenerate case of each vertex is its own group (no vertices shared a location), average the normals of each group.
            if (groupedVerticies.Count != vertices.Length)
            {
                foreach (var group in groupedVerticies)
                {
                    var smoothingGroup = group.Value;

                    // No need to smooth a group of one.
                    if (smoothingGroup.Count != 1)
                    {
                        var smoothedNormal = Vector3.zero;

                        foreach (var vertex in smoothingGroup)
                        {
                            smoothedNormal += normals[vertex.Key];
                        }

                        smoothedNormal.Normalize();

                        foreach (var vertex in smoothingGroup)
                        {
                            smoothNormals[vertex.Key] = smoothedNormal;
                        }
                    }
                }
            }

            Debug.LogFormat("CalculateSmoothNormals took {0} ms on {1} vertices.", watch.ElapsedMilliseconds, vertices.Length);
            return smoothNormals;
        }
    }
}
