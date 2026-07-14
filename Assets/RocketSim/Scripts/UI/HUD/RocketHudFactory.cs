// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/UI/HUD/RocketHudFactory.cs
// Purpose: Creates a fallback runtime Canvas, flight labels, curriculum labels,
// and gimbal indicator when no hand-authored HUD is assigned in the scene.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    internal static class RocketHudFactory
    {
        /// <summary>
        /// Creates the runtime HUD canvas and wires its generated text and gimbal controls.
        /// </summary>
        public static RocketHUD CreateRuntimeHud(out TextMeshProUGUI selectorLabel)
        {
            var canvasGo = new GameObject("RocketHUD_RuntimeCanvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasGo.AddComponent<GraphicRaycaster>();

            var panel = new GameObject("HUD_Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasGo.transform, false);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(18f, -18f);
            panelRect.sizeDelta = new Vector2(520f, 410f);
            panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);

            var runtimeHud = canvasGo.AddComponent<RocketHUD>();
            runtimeHud.altText = CreateHudText(panelRect, "ALT", 16f, -18f);
            runtimeHud.vVelText = CreateHudText(panelRect, "V-VEL", 16f, -48f);
            runtimeHud.hVelText = CreateHudText(panelRect, "H-VEL", 16f, -78f);
            runtimeHud.thrustText = CreateHudText(panelRect, "THRUST", 16f, -108f);
            runtimeHud.currentFuelText = CreateHudText(panelRect, "FUEL", 16f, -138f);
            runtimeHud.gimbalText = CreateHudText(panelRect, "GIMBAL", 16f, -168f);
            selectorLabel = CreateHudText(panelRect, "ROCKET_LABEL", 16f, -202f);
            runtimeHud.curriculumTitleText = CreateHudText(panelRect, "CURRICULUM_TITLE", 16f, -238f, 486f, 18f);
            runtimeHud.curriculumProgressText = CreateHudText(panelRect, "CURRICULUM_PROGRESS", 16f, -266f, 486f, 18f);
            runtimeHud.curriculumRateText = CreateHudText(panelRect, "CURRICULUM_RATE", 16f, -294f, 486f, 18f);
            runtimeHud.curriculumPrimaryText = CreateHudText(panelRect, "CURRICULUM_PRIMARY", 16f, -322f, 486f, 18f);
            runtimeHud.curriculumSecondaryText = CreateHudText(panelRect, "CURRICULUM_SECONDARY", 16f, -350f, 486f, 18f);
            runtimeHud.curriculumTertiaryText = CreateHudText(panelRect, "CURRICULUM_TERTIARY", 16f, -378f, 486f, 18f);

            var gimbalFrame = new GameObject("Gimbal_Frame", typeof(RectTransform), typeof(Image));
            gimbalFrame.transform.SetParent(panelRect, false);
            var frameRect = gimbalFrame.GetComponent<RectTransform>();
            frameRect.anchorMin = new Vector2(1f, 1f);
            frameRect.anchorMax = new Vector2(1f, 1f);
            frameRect.pivot = new Vector2(0.5f, 0.5f);
            frameRect.anchoredPosition = new Vector2(-72f, -82f);
            frameRect.sizeDelta = new Vector2(110f, 110f);
            gimbalFrame.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);

            var dot = new GameObject("Gimbal_Dot", typeof(RectTransform), typeof(Image));
            dot.transform.SetParent(frameRect, false);
            var dotRect = dot.GetComponent<RectTransform>();
            dotRect.anchorMin = new Vector2(0.5f, 0.5f);
            dotRect.anchorMax = new Vector2(0.5f, 0.5f);
            dotRect.pivot = new Vector2(0.5f, 0.5f);
            dotRect.sizeDelta = new Vector2(12f, 12f);
            dot.GetComponent<Image>().color = new Color(0.2f, 0.85f, 1f, 1f);
            runtimeHud.gimbalDot = dotRect;
            runtimeHud.uiMovementScale = 6f;

            canvasGo.SetActive(false);
            return runtimeHud;
        }

        /// <summary>
        /// Creates one positioned TextMeshPro label inside the runtime HUD panel.
        /// </summary>
        static TextMeshProUGUI CreateHudText(
            RectTransform parent,
            string name,
            float x,
            float y,
            float width = 300f,
            float fontSize = 20f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, 28f);

            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = name;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }
    }
}
