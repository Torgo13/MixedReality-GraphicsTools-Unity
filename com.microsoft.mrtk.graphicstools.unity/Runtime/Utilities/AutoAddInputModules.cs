// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if GT_USE_UGUI
using UnityEngine;
using UnityEngine.EventSystems;

namespace Microsoft.MixedReality.GraphicsTools
{
    /// <summary>
    /// Ensures that an input system module exists for legacy input system projects. 
    /// </summary>
    public class AutoAddInputModules : MonoBehaviour
    {
        private void OnValidate()
        {
            // Check if a valid input module exists.
            if (TryGetComponent<EventSystem>(out var eventSystem))
            {
                if (eventSystem.currentInputModule == null)
                {
                    // If the app is using the legacy input system and not the "new" one (they can be used at the same time). 
                    // Then add the default input module.
#if ENABLE_LEGACY_INPUT_MANAGER && !ENABLE_INPUT_SYSTEM

                    if (!gameObject.TryGetComponent<StandaloneInputModule>(out var _))
                    {
                        gameObject.AddComponent<StandaloneInputModule>();
                    }
#endif
                }
            }
        }
    }
}
#endif // GT_USE_UGUI
