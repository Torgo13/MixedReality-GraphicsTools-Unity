// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Microsoft.MixedReality.GraphicsTools
{
    /// <summary>
    /// Utility class to aid in taking screenshots via menu items and public APIs. Screenshots can
    /// be capture at various resolutions and with the current camera's clear color or a transparent
    /// clear color for use in easy post compositing of images.
    /// </summary>
    public static class ScreenshotUtilities
    {
        private static void CaptureScreenshot1x()
        {
            CaptureScreenshot(GetScreenshotPath(), 1);
        }

        private static void CaptureScreenshot1xAlphaComposite()
        {
            CaptureScreenshot(GetScreenshotPath(), 1, true);
        }

        private static void CaptureScreenshot2x()
        {
            CaptureScreenshot(GetScreenshotPath(), 2);
        }

        private static void CaptureScreenshot2xAlphaComposite()
        {
            CaptureScreenshot(GetScreenshotPath(), 2, true);
        }

        private static void CaptureScreenshot4x()
        {
            CaptureScreenshot(GetScreenshotPath(), 4);
        }

        private static void CaptureScreenshot4xAlphaComposite()
        {
            CaptureScreenshot(GetScreenshotPath(), 4, true);
        }

        /// <summary>
        /// Captures a screenshot with the current main camera's clear color.
        /// </summary>
        /// <param name="path">The path to save the screenshot to.</param>
        /// <param name="superSize">The multiplication factor to apply to the native resolution.</param>
        /// <param name="transparentClearColor">True if the captured screenshot should have a transparent clear color. Which can be used for screenshot overlays.</param>
        /// <param name="camera">The optional camera to take the screenshot from.</param>
        /// <returns>True on successful screenshot capture, false otherwise.</returns>
        public static bool CaptureScreenshot(string path, int superSize = 1, bool transparentClearColor = false, Camera camera = null)
        {
            if (string.IsNullOrEmpty(path) || superSize <= 0)
            {
                return false;
            }

            // Make sure we have a valid camera to render from.
            if (camera == null)
            {
                camera = Camera.main;

                if (camera == null)
                {
                    camera = UnityEngine.Object.FindObjectOfType<Camera>();

                    if (camera == null)
                    {
#if UNITY_EDITOR || DEBUG
                        Debug.LogError("Failed to find any cameras to capture a screenshot from.");
#endif
                        return false;
                    }

#if UNITY_EDITOR || DEBUG
                    Debug.LogWarning($"Capturing screenshot from a camera named \"{camera.name}\" because there is no camera tagged \"MainCamera\".");
#endif
                }
            }

            // Create a camera clone.
            Camera renderCamera = new GameObject().AddComponent<Camera>();
            renderCamera.CopyFrom(camera);
            renderCamera.orthographic = camera.orthographic;
            Transform cameraTransform = camera.transform;
            renderCamera.transform.SetPositionAndRotation(cameraTransform.position, cameraTransform.rotation);
            renderCamera.clearFlags = transparentClearColor ? CameraClearFlags.Color : camera.clearFlags;
            Color cameraBackgroundColor = camera.backgroundColor;
            renderCamera.backgroundColor = transparentClearColor
                ? Color.black
                : new Color(cameraBackgroundColor.r, cameraBackgroundColor.g, cameraBackgroundColor.b, 1.0f);

            UniversalAdditionalCameraData renderCameraData = renderCamera.GetUniversalAdditionalCameraData();
            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            renderCameraData.renderPostProcessing = cameraData.renderPostProcessing;
            renderCameraData.allowHDROutput = cameraData.allowHDROutput;
            renderCameraData.dithering = cameraData.dithering;
            renderCameraData.antialiasing = AntialiasingMode.None;

            // Create a render texture for the camera clone to render into.
            int width = Screen.width * superSize;
            int height = Screen.height * superSize;
            RenderTexture renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 8
            };
            renderCamera.targetTexture = renderTexture;

            // Render from the camera clone.
            renderCamera.Render();

            // Copy the render from the camera and save it to disk.
            Texture2D outputTexture = new Texture2D(width, height, TextureFormat.ARGB32, false);
            RenderTexture previousRenderTexture = RenderTexture.active;
            RenderTexture.active = renderTexture;
            outputTexture.ReadPixels(new Rect(0.0f, 0.0f, width, height), 0, 0);
            outputTexture.Apply();
            RenderTexture.active = previousRenderTexture;

            try
            {
                File.WriteAllBytes(path, outputTexture.EncodeToPNG());
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(outputTexture);
                UnityEngine.Object.DestroyImmediate(renderCamera.gameObject);
                UnityEngine.Object.DestroyImmediate(renderTexture);
            }

#if UNITY_EDITOR
            Debug.LogFormat("Screenshot captured to: {0}", path);
#endif
            return true;
        }

        /// <summary>
        /// Gets a directory which is safe for saving screenshots.
        /// </summary>
        /// <returns>A directory safe for saving screenshots.</returns>
        public static string GetScreenshotDirectory()
        {
            return Application.persistentDataPath;
        }

        /// <summary>
        /// Gets a unique screenshot path with a file name based on date and time.
        /// </summary>
        /// <returns>A unique screenshot path.</returns>
        public static string GetScreenshotPath()
        {
            return Path.Combine(GetScreenshotDirectory(), $"{Application.productName} {DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png");
        }
    }
}
