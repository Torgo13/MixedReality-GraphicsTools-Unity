// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if GT_USE_UGUI
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
#if UNITY_2021_1_OR_NEWER
using UnityEngine.Pool;
#endif
using UnityEngine.UI;

namespace Microsoft.MixedReality.GraphicsTools
{
    /// <summary>
    /// Overrides the RectMask2D.PerformClipping method to add extra checks before doing exhaustive culling on
    /// each maskable target.
    /// </summary>
    public class RectMask2DFast : RectMask2D
    {
        private HashSet<IClippable> clipTargets = null;
        private HashSet<MaskableGraphic> maskableTargets = null;
        private int lastclipTargetsCount = 0;
        private int lastmaskableTargetsCount = 0;
        private bool shouldRecalculateClipRects = false;

        private Canvas cachedCanvas = null;
        private Vector3[] cachedCorners = new Vector3[4];
        private Rect lastClipRectCanvasSpace = new Rect();
        private Vector2Int lastSoftness = new Vector2Int();
        private List<RectMask2D> clippers = new List<RectMask2D>();

#if OPTIMISATION
#if USING_REFLECTION
        private static readonly System.Func<RectMask2DFast, HashSet<IClippable>> GetClipTargets = ClipTargetsDelegate();
        private static System.Func<RectMask2DFast, HashSet<IClippable>> ClipTargetsDelegate()
        {
            const BindingFlags bindFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var param = System.Linq.Expressions.Expression.Parameter(typeof(RectMask2DFast), "instance");
            return System.Linq.Expressions.Expression.Lambda<System.Func<RectMask2DFast, HashSet<IClippable>>>(
                System.Linq.Expressions.Expression.Convert(System.Linq.Expressions.Expression.Field(
                System.Linq.Expressions.Expression.Convert(param, typeof(RectMask2D)), typeof(RectMask2D)
                .GetField("m_ClipTargets", bindFlags)!), typeof(HashSet<IClippable>)), param).Compile();
        }

        private static readonly System.Func<RectMask2DFast, HashSet<MaskableGraphic>> GetMaskableTargets = MaskableTargetsDelegate();
        private static System.Func<RectMask2DFast, HashSet<MaskableGraphic>> MaskableTargetsDelegate()
        {
            const BindingFlags bindFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var param = System.Linq.Expressions.Expression.Parameter(typeof(RectMask2DFast), "instance");
            return System.Linq.Expressions.Expression.Lambda<System.Func<RectMask2DFast, HashSet<MaskableGraphic>>>(
                System.Linq.Expressions.Expression.Convert(System.Linq.Expressions.Expression.Field(
                System.Linq.Expressions.Expression.Convert(param, typeof(RectMask2D)), typeof(RectMask2D)
                .GetField("m_MaskableTargets", bindFlags)!), typeof(HashSet<MaskableGraphic>)), param).Compile();
        }
#else
        /// <summary>
        /// This is a helper class to allow the binding code to manipulate the internal fields of
        /// <see cref="RectMask2D"/>. The field order below must not be changed.
        /// </summary>
        [UnityEngine.Scripting.Preserve]
        internal class RectMask2DPrivateFieldAccess : UnityEngine.EventSystems.UIBehaviour, IClipper, ICanvasRaycastFilter
        {
            internal RectangularVertexClipper m_VertexClipper;
            internal RectTransform m_RectTransform;
            internal HashSet<MaskableGraphic> m_MaskableTargets;
            internal HashSet<IClippable> m_ClipTargets;
            internal bool m_ShouldRecalculateClipRects;
            internal List<RectMask2D> m_Clippers;
            internal Rect m_LastClipRectCanvasSpace;
            internal bool m_ForceClip;
            internal Vector4 m_Padding;
            internal Vector2Int m_Softness;
            internal Canvas m_Canvas;
            internal Vector3[] m_Corners;

            public void PerformClipping()
            {
                throw new System.NotImplementedException();
            }

            public bool IsRaycastLocationValid(Vector2 sp, Camera eventCamera)
            {
                throw new System.NotImplementedException();
            }
        }
#endif // USING_REFLECTION
#endif // OPTIMISATION

        #region MonoBehaviour Implementation

        /// <inheritdoc />
        protected override void OnEnable()
        {
            base.OnEnable();
            shouldRecalculateClipRects = true;
            ForceClip = true;
        }

#if UNITY_EDITOR
        /// <inheritdoc />
        protected override void OnValidate()
        {
            base.OnValidate();
            shouldRecalculateClipRects = true;
            ForceClip = true;
        }
#endif
        /// <inheritdoc />
        protected override void OnDidApplyAnimationProperties()
        {
            base.OnDidApplyAnimationProperties();

            shouldRecalculateClipRects = true;
            ForceClip = true;
        }

#endregion MonoBehaviour Implementation

        #region RectMask2D Implementation

        /// <inheritdoc />
        protected override void OnTransformParentChanged()
        {
            base.OnTransformParentChanged();
            shouldRecalculateClipRects = true;
        }

        /// <inheritdoc />
        protected override void OnCanvasHierarchyChanged()
        {
            cachedCanvas = null;
            base.OnCanvasHierarchyChanged();
            shouldRecalculateClipRects = true;
        }

        /// <summary>
        /// Improves the base class method by:
        /// - Checks if the canvas renderer has moved before exhaustive culling.
        /// - Interleaves UpdateClipSoftness so objects are not iterated over twice.
        /// - Adds a OnSetClipRect callback for derived classes to use.
        /// </summary>
        public override void PerformClipping()
        {
            // Not calling the base class method intentionally to provide a more optimal version.

            Initialize();

            if (ReferenceEquals(Canvas, null))
            {
                return;
            }

            //TODO See if an IsActive() test would work well here or whether it might cause unexpected side effects (re case 776771)

            // if the parents are changed
            // or something similar we
            // do a recalculate here
            if (shouldRecalculateClipRects || ForceClip)
            {
                MaskUtilities.GetRectMasksForClip(this, clippers);
                shouldRecalculateClipRects = false;
            }

            // get the compound rects from
            // the clippers that are valid
            bool validRect = true;
            Rect clipRect = Clipping.FindCullAndClipWorldRect(clippers, out validRect);

            // If the mask is in ScreenSpaceOverlay/Camera render mode, its content is only rendered when its rect
            // overlaps that of the root canvas.
            RenderMode renderMode = Canvas.rootCanvas.renderMode;
            bool maskIsCulled =
                (renderMode == RenderMode.ScreenSpaceCamera || renderMode == RenderMode.ScreenSpaceOverlay) &&
                !clipRect.Overlaps(RootCanvasRect, true);

            if (maskIsCulled)
            {
                // Children are only displayed when inside the mask. If the mask is culled, then the children
                // inside the mask are also culled. In that situation, we pass an invalid rect to allow callees
                // to avoid some processing.
                clipRect = Rect.zero;
                validRect = false;
            }

            if (clipRect != lastClipRectCanvasSpace || softness != lastSoftness)
            {
                foreach (IClippable clipTarget in clipTargets)
                {
                    clipTarget.SetClipRect(clipRect, validRect);
                    clipTarget.SetClipSoftness(softness);
                }

                foreach (MaskableGraphic maskableTarget in maskableTargets)
                {
                    maskableTarget.SetClipRect(clipRect, validRect);
                    maskableTarget.SetClipSoftness(softness);
                    OnSetClipRect(maskableTarget);

                    maskableTarget.Cull(clipRect, validRect);
                }
            }
            else if (ForceClip)
            {
                foreach (IClippable clipTarget in clipTargets)
                {
                    clipTarget.SetClipRect(clipRect, validRect);
                    clipTarget.SetClipSoftness(softness);
                }

                foreach (MaskableGraphic maskableTarget in maskableTargets)
                {
                    maskableTarget.SetClipRect(clipRect, validRect);
                    maskableTarget.SetClipSoftness(softness);
                    OnSetClipRect(maskableTarget);

                    if (maskableTarget.canvasRenderer.hasMoved)
                    {
                        maskableTarget.Cull(clipRect, validRect);
                    }
                }
            }
            else
            {
                foreach (MaskableGraphic maskableTarget in maskableTargets)
                {
                    // Added check back from https://github.com/Unity-Technologies/uGUI/blob/2019.1/UnityEngine.UI/UI/Core/RectMask2D.cs#L227
                    // even though case 1170399 says this can be an issue - hasMoved is not a valid check when animating on pivot of the object
                    if (!maskableTarget.canvasRenderer.hasMoved)
                    {
                        continue;
                    }

                    maskableTarget.Cull(clipRect, validRect);
                }
            }

            ForceClip = false;
            lastClipRectCanvasSpace = clipRect;
            lastSoftness = softness;
        }

        #endregion RectMask2D Implementation

        /// <summary>
        /// Checks if all clip/mask targets needs to be re-culled.
        /// Setting this to true will force all clip/mask targets to update their culling state next frame.
        /// </summary>
        public bool ForceClip
        {
            get
            {
                // This is an imprecise check if a clip or mask target gets added then removed on the same frame.
                // But... the alternative is we reflect into m_ForceClip base member which would be a per frame allocation due to it being a value type.
                // If this check is return false negatives in your scenario, then set ForceClip to true.
                return clipTargets.Count != lastclipTargetsCount ||
                       maskableTargets.Count != lastmaskableTargetsCount;
            }
            set
            {
                if (value == true)
                {
                    lastclipTargetsCount = 0;
                    lastmaskableTargetsCount = 0;
                }
                else
                {
                    Initialize();

                    lastclipTargetsCount = clipTargets.Count;
                    lastmaskableTargetsCount = maskableTargets.Count;
                }
            }
        }

        /// <summary>
        /// Callback whenever the clip rect is mutated.
        /// </summary>
        protected virtual void OnSetClipRect(MaskableGraphic maskableTarget) { }

        private void Initialize()
        {
            // Check if we have already initialized.
            if (clipTargets != null)
            {
                return;
            }

            // Many of the properties we need access to for clipping are not exposed. So, we have to do reflection to get access to them.
#if OPTIMISATION
#if USING_REFLECTION
            clipTargets = GetClipTargets(this);
            maskableTargets = GetMaskableTargets(this);
#else
            var rectMask2D = (RectMask2D)this;
            var rectMask2DAccess = Unity.Collections.LowLevel.Unsafe.UnsafeUtility.As<RectMask2D, RectMask2DPrivateFieldAccess>(ref rectMask2D);

            clipTargets = rectMask2DAccess.m_ClipTargets;
            maskableTargets = rectMask2DAccess.m_MaskableTargets;
#endif // USING_REFLECTION
#else
            BindingFlags bindFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            clipTargets = (HashSet<IClippable>)typeof(RectMask2D).GetField("m_ClipTargets", bindFlags).GetValue(this);
            maskableTargets = (HashSet<MaskableGraphic>)typeof(RectMask2D).GetField("m_MaskableTargets", bindFlags).GetValue(this);
#endif // OPTIMISATION
        }

        private Canvas Canvas
        {
            get
            {
                if (cachedCanvas == null)
                {
#if UNITY_2021_1_OR_NEWER
                    var list = ListPool<Canvas>.Get();
                    gameObject.GetComponentsInParent(false, list);

                    if (list.Count > 0)
                        cachedCanvas = list[list.Count - 1];
                    else
                        cachedCanvas = null;

                    ListPool<Canvas>.Release(list);
#else
                    var list = gameObject.GetComponentsInParent<Canvas>(false);
                    if (list.Length > 0)
                        cachedCanvas = list[list.Length - 1];
                    else
                        cachedCanvas = null;
#endif
                }

                return cachedCanvas;
            }
        }

        private Rect RootCanvasRect
        {
            get
            {
                rectTransform.GetWorldCorners(cachedCorners);

                if (!ReferenceEquals(Canvas, null))
                {
                    Canvas rootCanvas = Canvas.rootCanvas;
                    for (int i = 0; i < 4; ++i)
                        cachedCorners[i] = rootCanvas.transform.InverseTransformPoint(cachedCorners[i]);
                }

                return new Rect(cachedCorners[0].x, cachedCorners[0].y, cachedCorners[2].x - cachedCorners[0].x, cachedCorners[2].y - cachedCorners[0].y);
            }
        }
    }
}
#endif // GT_USE_UGUI
