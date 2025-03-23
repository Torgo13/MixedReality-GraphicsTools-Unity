﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using UnityEngine;

namespace Microsoft.MixedReality.GraphicsTools
{
    /// <summary>
    /// Extensions methods for the Unity Component class.
    /// This also includes some component-related extensions for the GameObject class.
    /// </summary>
    public static class ComponentExtensions
    {
        /// <summary>
        /// Ensure that a component of type <typeparamref name="T"/> exists on the GameObject.
        /// If it doesn't exist, creates it.
        /// </summary>
        /// <typeparam name="T">Type of the component.</typeparam>
        /// <param name="component">A component on the GameObject for which a component of type <typeparamref name="T"/> should exist.</param>
        /// <returns>The component that was retrieved or created.</returns>
        public static T EnsureComponent<T>(this Component component) where T : Component
        {
            return EnsureComponent<T>(component.gameObject);
        }

        /// <summary>
        /// Find the first component of type <typeparamref name="T"/> in the ancestors of the GameObject of the specified component.
        /// </summary>
        /// <typeparam name="T">Type of component to find.</typeparam>
        /// <param name="component">Component for which its GameObject's ancestors must be considered.</param>
        /// <param name="includeSelf">Indicates whether the specified GameObject should be included.</param>
        /// <returns>The component of type <typeparamref name="T"/>. Null if none was found.</returns>
        public static T FindAncestorComponent<T>(this Component component, bool includeSelf = true) where T : Component
        {
            var transform = component.transform;
            
            if (!includeSelf)
                transform = transform.parent;
            
            while (transform != null)
            {
                if (transform.TryGetComponent<T>(out var ancestorComponent))
                    return ancestorComponent;
                
                transform = transform.parent;
            }
            
            return null;
        }

        /// <summary>
        /// Ensure that a component of type <typeparamref name="T"/> exists on the GameObject.
        /// If it doesn't exist, creates it.
        /// </summary>
        /// <typeparam name="T">Type of the component.</typeparam>
        /// <param name="gameObject">GameObject on which the component should be.</param>
        /// <returns>The component that was retrieved or created.</returns>
        /// <remarks>
        /// This extension has to remain in this class as it is required by the <see cref="EnsureComponent{T}(Component)"/> method
        /// </remarks>
        public static T EnsureComponent<T>(this GameObject gameObject) where T : Component
        {
            T foundComponent = gameObject.GetComponent<T>();
            return foundComponent == null ? gameObject.AddComponent<T>() : foundComponent;
        }

        /// <summary>
        /// Ensure that a component of type exists on the GameObject.
        /// If it doesn't exist, creates it.
        /// </summary>
        /// <param name="gameObject">GameObject on which the component should be.</param>
        /// <param name="component">A component on the GameObject for which a component of type should exist.</param>
        /// <returns>The component that was retrieved or created.</returns>
        public static Component EnsureComponent(this GameObject gameObject, Type component)
        {
            var foundComponent = gameObject.GetComponent(component);
            return foundComponent == null ? gameObject.AddComponent(component) : foundComponent;
        }
    }
}