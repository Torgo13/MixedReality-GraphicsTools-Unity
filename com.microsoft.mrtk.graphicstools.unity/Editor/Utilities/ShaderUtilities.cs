// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if OPTIMISATION
#else
using Microsoft.CSharp;
#endif // OPTIMISATION

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Microsoft.MixedReality.GraphicsTools.Editor
{
    /// <summary>
    /// General utility methods to help with shader development and usage.
    /// </summary>
    public static class ShaderUtilities
    {
        /// <summary>
        /// CanvasMaterialAnimator formatting strings.
        /// </summary>
        private static readonly string ClassBody =
 @"// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using UnityEngine;

namespace Microsoft.MixedReality.GraphicsTools
{{
    /// <summary>
    /// This class was auto generated via Assets > Graphics Tools > Generate Canvas Material Animator.
    /// Use Unity's animation system to animate fields on this class to drive material properties on CanvasRenderers.
    /// Version={0}
    /// </summary>
    public class {1} : CanvasMaterialAnimatorBase
    {{{2}

        /// <inheritdoc/>
        public override void InitializeFromMaterial(Material material)
        {{{3}
        }}

        /// <inheritdoc/>
        public override void ApplyToMaterial(Material material)
        {{{4}
        }}

        /// <inheritdoc/>
        public override string GetTargetShaderName()
        {{{5}
        }}
    }}
}}
";
        private static readonly string PropertyBody =
@"           /// <summary>
           /// Shader property.
           /// </summary>;
           public {0} {1} = {2};";
        private static readonly string PropertyBodyRange =
 @"           /// <summary>
           /// Shader property.
           /// </summary>;
           [Range({3}, {4})] public {0} {1} = {2};";
        private static readonly string PropertyIDBody =
 @"           /// <summary>
           /// Shader property ID.
           /// </summary>
           public static int {0} = Shader.PropertyToID(""{1}"");";

        private static readonly string FromMaterialBody = "            {0} = material.{1}({2});";
        private static readonly string FromMaterialBodyCast = "            {0} = ({3})material.{1}({2});";
        private static readonly string ToMaterialBody = "            material.{0}({1}, {2});";
        private static readonly string ToMaterialBodyCast = "            material.{0}({1}, ({3}){2});";
        private static readonly string TargetShaderNameBody = "{0}            return \"{1}\";";
        private static readonly string PropertyID = "{0}ID";
        private static readonly string FloatPostfix = "f";
        private static readonly string PropertyHeader =
@"
           [Header(""{0}"")]";

        /// <summary>
        /// Creates a new component which contains serialized properties for each of the shader's exposed properties and methods to set and apply their state.
        /// </summary>
        public static void GenerateCanvasMaterialAnimator(Shader shader, bool saveToParentDirectory = false, bool saveToCanvasDirectory = false)
        {
            // Generate the properties and methods.
            string properties = string.Empty;
            string fromMaterial = string.Empty;
            string toMaterial = string.Empty;
            string targetShaderName = string.Format(TargetShaderNameBody, Environment.NewLine, shader.name);
#if UNITY_6000_0_OR_NEWER
            int count = shader.GetPropertyCount();
#else
            int count = ShaderUtil.GetPropertyCount(shader);
#endif // UNITY_6000_0_OR_NEWER

            for (int i = 0; i < count; ++i)
            {
#if UNITY_6000_0_OR_NEWER
                string name = shader.GetPropertyName(i);
#else
                string name = ShaderUtil.GetPropertyName(shader, i);
#endif // UNITY_6000_0_OR_NEWER
                string nameID = string.Format(PropertyID, name);
#if UNITY_6000_0_OR_NEWER
                ShaderPropertyType type = shader.GetPropertyType(i);
#else
                ShaderUtil.ShaderPropertyType type = ShaderUtil.GetPropertyType(shader, i);
#endif // UNITY_6000_0_OR_NEWER
                string[] propertyAttributes = shader.GetPropertyAttributes(i);

                string headerAttribute = Array.Find(propertyAttributes, IsHeaderAttribute);
                if (!string.IsNullOrEmpty(headerAttribute))
                {
                    properties += Environment.NewLine;
                    properties += string.Format(PropertyHeader, GetHeaderName(headerAttribute));
                }

                // Textures.
#if UNITY_6000_0_OR_NEWER
                if (type == ShaderPropertyType.Texture)
#else
                if (type == ShaderUtil.ShaderPropertyType.TexEnv)
#endif // UNITY_6000_0_OR_NEWER
                {
#if UNITY_6000_0_OR_NEWER
                    TextureDimension dimension = shader.GetPropertyTextureDimension(i);
#else
                    TextureDimension dimension = ShaderUtil.GetTexDim(shader, i);
#endif // UNITY_6000_0_OR_NEWER
                    string typeName = TextureDimensionToTypeName(dimension);
                    properties += Environment.NewLine;
                    properties += string.Format(PropertyBody, typeName, name, "null");

                    properties += Environment.NewLine;
                    properties += string.Format(PropertyIDBody, nameID, name);

                    fromMaterial += Environment.NewLine;
                    fromMaterial += string.Format(FromMaterialBodyCast, name, ShaderPropertyTypeToGettor(type), nameID, typeName);
                    toMaterial += Environment.NewLine;
                    toMaterial += string.Format(ToMaterialBodyCast, ShaderPropertyTypeToSettor(type), nameID, name, typeName);
                }
                else  // All other types. Colors, floats, vectors, etc.
                {
                    string defaultValue, minValue = null, maxValue = null;
#if UNITY_6000_0_OR_NEWER
                    if (type == ShaderPropertyType.Float)
                    {
                        defaultValue = shader.GetPropertyDefaultFloatValue(i) + FloatPostfix;
                    }
                    else if (type == ShaderPropertyType.Range)
                    {
                        defaultValue = shader.GetPropertyDefaultFloatValue(i) + FloatPostfix;
                        Vector2 range = shader.GetPropertyRangeLimits(i);
                        minValue = range.x + FloatPostfix;
                        maxValue = range.y + FloatPostfix;
                    }
#else
                    if (type == ShaderUtil.ShaderPropertyType.Float)
                    {
                        defaultValue = ShaderUtil.GetRangeLimits(shader, i, 0).ToString() + FloatPostfix;
                    }
                    else if (type == ShaderUtil.ShaderPropertyType.Range)
                    {
                        defaultValue = ShaderUtil.GetRangeLimits(shader, i, 0).ToString() + FloatPostfix;
                        minValue = ShaderUtil.GetRangeLimits(shader, i, 1).ToString() + FloatPostfix;
                        maxValue = ShaderUtil.GetRangeLimits(shader, i, 2) + FloatPostfix;
                    }
#endif // UNITY_6000_0_OR_NEWER
                    else
                    {
                        defaultValue = ShaderPropertyTypeToDefault(type);
                    }

                    properties += Environment.NewLine;

                    if (minValue == null || maxValue == null)
                    {
                        properties += string.Format(PropertyBody, ShaderPropertyTypeToTypeName(type), name, defaultValue);
                    }
                    else
                    {
                        properties += string.Format(PropertyBodyRange, ShaderPropertyTypeToTypeName(type), name, defaultValue, minValue, maxValue);
                    }

                    properties += Environment.NewLine;
                    properties += string.Format(PropertyIDBody, nameID, name);

                    fromMaterial += Environment.NewLine;
                    fromMaterial += string.Format(FromMaterialBody, name, ShaderPropertyTypeToGettor(type), nameID);
                    toMaterial += Environment.NewLine;
                    toMaterial += string.Format(ToMaterialBody, ShaderPropertyTypeToSettor(type), nameID, name);
                }
            }

            try
            {
                // Save a new component out as a C# class.
#if OPTIMISATION
                const
#endif // OPTIMISATION
                string version = "0.1.0";
                string assetPath = AssetDatabase.GetAssetPath(shader);

                string className = SanitizeIdentifier("CanvasMaterialAnimator" + Path.GetFileNameWithoutExtension(assetPath));

                string classText = string.Format(ClassBody, version, className, properties, fromMaterial, toMaterial, targetShaderName);

                string directory = Path.GetDirectoryName(assetPath);

                if (saveToParentDirectory)
                {
                    directory = Path.Combine(directory, "..");
                }

                if (saveToCanvasDirectory)
                {
                    directory = Path.Combine(directory, "Canvas");

                    Directory.CreateDirectory(directory);
                }

                string path = Path.Combine(directory, string.Format("{0}.cs", className));

                File.WriteAllText(path, classText);

                AssetDatabase.Refresh();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// Menu item to right click on a shader asset in the Project window and call GenerateCanvasMaterialAnimator on that specific shader.
        /// </summary>
        [MenuItem("Assets/Graphics Tools/Generate Canvas Material Animator")]
        private static void GenerateCanvasMaterialAnimator()
        {
            foreach (UnityEngine.Object selection in Selection.objects)
            {
                Shader shader = selection as Shader;

                if (shader == null)
                {
                    Debug.LogWarningFormat("The selection {0} is not a shader.", selection.name);
                    continue;
                }

                GenerateCanvasMaterialAnimator(shader);
            }
        }

        /// <summary>
        /// Ensures a shader asset was right clicked on.
        /// </summary>
        [MenuItem("Assets/Graphics Tools/Generate Canvas Material Animator", true)]
        private static bool ValidateGenerateCanvasMaterialAnimator()
        {
            foreach (UnityEngine.Object selection in Selection.objects)
            {
#if OPTIMISATION
                if (selection is Shader)
#else
                if (selection.GetType() == typeof(Shader))
#endif // OPTIMISATION
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Toggles if text color inversion is enabled on the material.
        /// </summary>
        [MenuItem("Assets/Graphics Tools/Accessibility/Toggle Text Color Inversion")]
        private static void ToggleTextColorInversion()
        {
            foreach (UnityEngine.Object selection in Selection.objects)
            {
                Material material = selection as Material;

                if (material == null)
                {
                    Debug.LogWarningFormat("The selection {0} is not a material.", selection.name);
                    continue;
                }

                AccessibilityUtilities.ToggleTextColorInversion(material);
            }
        }

        /// <summary>
        /// Ensures a material asset was right clicked on.
        /// </summary>
        [MenuItem("Assets/Graphics Tools/Accessibility/Toggle Text Color Inversion", true)]
        private static bool ValidateToggleTextColorInversion()
        {
            foreach (UnityEngine.Object selection in Selection.objects)
            {
                if (selection.GetType() == typeof(Material))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Menu item to call GenerateCanvasMaterialAnimator for each shader within Assets/Runtime/Shaders that does not have "Non-Canvas" in the name.
        /// </summary>
        [MenuItem("Window/Graphics Tools/Canvas Material Animators/Generate All")]
        private static void CanvasMaterialAnimatorsGenerateAll()
        {
            string[] paths = Directory.GetFiles(string.Format("Packages/{0}/Runtime/Shaders", DevelopmentUtilities.PackageName), "*.shader");

            foreach (string path in paths)
            {
                Shader shader = (Shader)AssetDatabase.LoadAssetAtPath(path, typeof(Shader));

                if (shader != null && !shader.name.Contains("Non-Canvas"))
                {
                    GenerateCanvasMaterialAnimator(shader, true, true);
                }
            }
        }

        /// <summary>
        /// Menu item validation.
        /// </summary>
        [MenuItem("Window/Graphics Tools/Canvas Material Animators/Generate All", true)]
        private static bool ValidateCanvasMaterialAnimatorsGenerateAll()
        {
            return DevelopmentUtilities.IsPackageMutable();
        }

        /// <summary>
        /// ShaderPropertyType to C# type conversion.
        /// </summary>
#if UNITY_6000_0_OR_NEWER
        private static string ShaderPropertyTypeToTypeName(ShaderPropertyType type)
#else
        private static string ShaderPropertyTypeToTypeName(ShaderUtil.ShaderPropertyType type)
#endif // UNITY_6000_0_OR_NEWER
        {
            switch (type)
            {
#if UNITY_6000_0_OR_NEWER
                case ShaderPropertyType.Color: return "Color";
                case ShaderPropertyType.Vector: return "Vector4";
                default:
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range: return "float";
                case ShaderPropertyType.Texture: return "Texture";
#else
                case ShaderUtil.ShaderPropertyType.Color: return "Color";
                case ShaderUtil.ShaderPropertyType.Vector: return "Vector4";
                default:
                case ShaderUtil.ShaderPropertyType.Float:
                case ShaderUtil.ShaderPropertyType.Range: return "float";
                case ShaderUtil.ShaderPropertyType.TexEnv: return "Texture";
#endif // UNITY_6000_0_OR_NEWER
            }
        }

        /// <summary>
        /// ShaderPropertyType to Unity material getter method.
        /// </summary>
#if UNITY_6000_0_OR_NEWER
        private static string ShaderPropertyTypeToGettor(ShaderPropertyType type)
#else
        private static string ShaderPropertyTypeToGettor(ShaderUtil.ShaderPropertyType type)
#endif // UNITY_6000_0_OR_NEWER
        {
            switch (type)
            {
#if UNITY_6000_0_OR_NEWER
                case ShaderPropertyType.Color: return "GetColor";
                case ShaderPropertyType.Vector: return "GetVector";
                default:
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range: return "GetFloat";
                case ShaderPropertyType.Texture: return "GetTexture";
#else
                case ShaderUtil.ShaderPropertyType.Color: return "GetColor";
                case ShaderUtil.ShaderPropertyType.Vector: return "GetVector";
                default:
                case ShaderUtil.ShaderPropertyType.Float:
                case ShaderUtil.ShaderPropertyType.Range: return "GetFloat";
                case ShaderUtil.ShaderPropertyType.TexEnv: return "GetTexture";
#endif // UNITY_6000_0_OR_NEWER
            }
        }

        /// <summary>
        /// ShaderPropertyType to Unity material setter method.
        /// </summary>
#if UNITY_6000_0_OR_NEWER
        private static string ShaderPropertyTypeToSettor(ShaderPropertyType type)
#else
        private static string ShaderPropertyTypeToSettor(ShaderUtil.ShaderPropertyType type)
#endif // UNITY_6000_0_OR_NEWER
        {
            switch (type)
            {
#if UNITY_6000_0_OR_NEWER
                case ShaderPropertyType.Color: return "SetColor";
                case ShaderPropertyType.Vector: return "SetVector";
                default:
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range: return "SetFloat";
                case ShaderPropertyType.Texture: return "SetTexture";
#else
                case ShaderUtil.ShaderPropertyType.Color: return "SetColor";
                case ShaderUtil.ShaderPropertyType.Vector: return "SetVector";
                default:
                case ShaderUtil.ShaderPropertyType.Float:
                case ShaderUtil.ShaderPropertyType.Range: return "SetFloat";
                case ShaderUtil.ShaderPropertyType.TexEnv: return "SetTexture";
#endif // UNITY_6000_0_OR_NEWER
            }
        }

        /// <summary>
        /// TextureDimension to C# type conversion. 
        /// </summary>
        private static string TextureDimensionToTypeName(TextureDimension dimension)
        {
            switch (dimension)
            {
                default:
                case TextureDimension.Tex2D: return "Texture2D";
                case TextureDimension.Tex3D: return "Texture3D";
                case TextureDimension.Cube: return "Cubemap";
                case TextureDimension.Tex2DArray: return "Texture2DArray";
                case TextureDimension.CubeArray: return "CubemapArray";
            }
        }

        /// <summary>
        /// ShaderPropertyType to default C# type value.
        /// </summary>
#if UNITY_6000_0_OR_NEWER
        private static string ShaderPropertyTypeToDefault(ShaderPropertyType type)
#else
        private static string ShaderPropertyTypeToDefault(ShaderUtil.ShaderPropertyType type)
#endif // UNITY_6000_0_OR_NEWER
        {
            switch (type)
            {
#if UNITY_6000_0_OR_NEWER
                case ShaderPropertyType.Color: return "Color.white";
                case ShaderPropertyType.Vector: return "Vector4.zero";
                default:
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range: return "0.0f";
                case ShaderPropertyType.Texture: return "null";
#else
                case ShaderUtil.ShaderPropertyType.Color: return "Color.white";
                case ShaderUtil.ShaderPropertyType.Vector: return "Vector4.zero";
                default:
                case ShaderUtil.ShaderPropertyType.Float:
                case ShaderUtil.ShaderPropertyType.Range: return "0.0f";
                case ShaderUtil.ShaderPropertyType.TexEnv: return "null";
#endif // UNITY_6000_0_OR_NEWER
            }
        }

        /// <summary>
        /// Modifies a string so that it adheres to C# identifier rules: https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/identifier-names
        /// </summary>
        private static string SanitizeIdentifier(string input)
        {
#if OPTIMISATION
            bool isValid = System.CodeDom.Compiler.CodeDomProvider.CreateProvider("C#").IsValidIdentifier(input);
#else
            bool isValid = CSharpCodeProvider.CreateProvider("C#").IsValidIdentifier(input);
#endif // OPTIMISATION

            if (!isValid)
            {
                // Remove invalid characters.
                Regex regex = new Regex(@"[^\p{Ll}\p{Lu}\p{Lt}\p{Lo}\p{Nd}\p{Nl}\p{Mn}\p{Mc}\p{Cf}\p{Pc}\p{Lm}]");
                input = regex.Replace(input, string.Empty);

                // Ensure class/variable name begins with a letter or underscore.
                if (!char.IsLetter(input, 0))
                {
                    input = input.Insert(0, "_");
                }
            }

            return RemoveWhitespace(input);
        }

        /// <summary>
        /// Removes all white space from a string (spaces, tabs, etc.).
        /// </summary>
        private static string RemoveWhitespace(string input)
        {
            return new string(input.ToCharArray().Where(c => !Char.IsWhiteSpace(c)).ToArray());
        }

        /// <summary>
        /// Checks if string is a Header attribute
        /// </summary>
        private static bool IsHeaderAttribute(string a)
        {
            // Regex to check if attribute is type, Header
            return Regex.IsMatch(a, @"Header\(.*\)");
        }

        /// <summary>
        /// Returns the Header name as a string
        /// </summary>
        private static string GetHeaderName(string headerAttribute)
        {
#if OPTIMISATION_ORDINAL
            int startPosition = headerAttribute.IndexOf('(', StringComparison.Ordinal) + 1;
            int wordLength = headerAttribute.IndexOf(")", startPosition, StringComparison.Ordinal) - startPosition;
#else
            int startPosition = headerAttribute.IndexOf("(") + 1;
            int wordLength = headerAttribute.IndexOf(")", startPosition) - startPosition;
#endif // OPTIMISATION_ORDINAL

            return headerAttribute.Substring(startPosition, wordLength);
        }
    }
}
