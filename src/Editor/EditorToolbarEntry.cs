using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CollabCharting
{
    internal static class EditorToolbarEntry
    {
        private const string ButtonName = "CollabChartingToolbarButton";
        private const float SplitGap = 8f;
        private static Button? currentButton;
        private static scnEditor? installedEditor;

        public static void Tick()
        {
            if (!Main.Settings.EnableEditorToolbarButton || ADOBase.editor == null)
            {
                return;
            }

            if (currentButton == null || installedEditor != ADOBase.editor)
            {
                Install(ADOBase.editor);
            }
        }

        public static void Install(scnEditor editor)
        {
            if (!Main.Settings.EnableEditorToolbarButton || editor == null)
            {
                return;
            }

            Button source = editor.buttonHelp != null ? editor.buttonHelp : editor.buttonFileActionDropdown;
            if (source == null)
            {
                Main.Mod?.Logger.Warning("Collab toolbar button source not found.");
                return;
            }

            Transform parent = source.transform.parent;
            RemoveStaleButtons(parent);

            Transform existing = parent.Find(ButtonName);
            GameObject buttonObject = existing != null
                ? existing.gameObject
                : Object.Instantiate(source.gameObject, parent);

            buttonObject.name = ButtonName;
            buttonObject.SetActive(source.gameObject.activeSelf);
            buttonObject.transform.SetSiblingIndex(Mathf.Min(source.transform.GetSiblingIndex() + 1, parent.childCount - 1));

            currentButton = buttonObject.GetComponent<Button>();
            if (currentButton != null)
            {
                currentButton.onClick.RemoveAllListeners();
                currentButton.onClick.AddListener(Main.OpenOverlay);
                currentButton.interactable = true;
            }

            TMP_Text? label = buttonObject.GetComponentInChildren<TMP_Text>(true);
            TMP_Text? sourceLabel = source.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.gameObject.SetActive(true);
                label.text = "协作";
                if (sourceLabel != null)
                {
                    label.font = sourceLabel.font;
                    label.fontSize = sourceLabel.fontSize;
                    label.fontStyle = sourceLabel.fontStyle;
                    label.alignment = sourceLabel.alignment;
                    label.color = sourceLabel.color;
                    label.enableAutoSizing = sourceLabel.enableAutoSizing;
                    label.fontSizeMin = sourceLabel.fontSizeMin;
                    label.fontSizeMax = sourceLabel.fontSizeMax;
                }
            }

            RectTransform sourceRect = source.GetComponent<RectTransform>();
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            if (sourceRect != null && rect != null)
            {
                rect.anchorMin = sourceRect.anchorMin;
                rect.anchorMax = sourceRect.anchorMax;
                rect.pivot = sourceRect.pivot;
                rect.localScale = sourceRect.localScale;

                bool hasLayout = parent.GetComponent<LayoutGroup>() != null;
                if (!hasLayout && source == editor.buttonHelp)
                {
                    SplitHelpRow(sourceRect, rect);
                }
                else
                {
                    rect.anchoredPosition = sourceRect.anchoredPosition -
                                            new Vector2(0f, Mathf.Abs(sourceRect.sizeDelta.y) + 6f);
                    rect.sizeDelta = sourceRect.sizeDelta;
                }
            }

            EventTrigger? trigger = buttonObject.GetComponent<EventTrigger>();
            if (trigger != null)
            {
                trigger.triggers.Clear();
            }

            installedEditor = editor;
            Main.Mod?.Logger.Log("Collab button installed as a clone of the editor Help button.");
        }

        private static void SplitHelpRow(RectTransform helpRect, RectTransform collabRect)
        {
            RectTransform? parentRect = helpRect.parent as RectTransform;
            if (parentRect == null)
            {
                collabRect.sizeDelta = helpRect.sizeDelta;
                collabRect.anchoredPosition = helpRect.anchoredPosition;
                return;
            }

            Vector3[] corners = new Vector3[4];
            helpRect.GetWorldCorners(corners);
            Vector3 leftBottom = parentRect.InverseTransformPoint(corners[0]);
            Vector3 rightTop = parentRect.InverseTransformPoint(corners[2]);
            Vector3 center = (leftBottom + rightTop) * 0.5f;
            float originalWidth = Mathf.Max(160f, rightTop.x - leftBottom.x);
            float originalHeight = Mathf.Max(24f, rightTop.y - leftBottom.y);
            float splitWidth = Mathf.Max(80f, (originalWidth - SplitGap) * 0.5f);
            float offset = (splitWidth + SplitGap) * 0.5f;

            ConfigureSplitButton(helpRect, center - new Vector3(offset, 0f, 0f), splitWidth, originalHeight);
            ConfigureSplitButton(collabRect, center + new Vector3(offset, 0f, 0f), splitWidth, originalHeight);
        }

        private static void ConfigureSplitButton(RectTransform rect, Vector3 localCenter, float width, float height)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            rect.localPosition = new Vector3(localCenter.x, localCenter.y, rect.localPosition.z);
        }

        private static void RemoveStaleButtons(Transform desiredParent)
        {
            foreach (Button button in Resources.FindObjectsOfTypeAll<Button>())
            {
                if (button == null || button.name != ButtonName || button.transform.parent == desiredParent)
                {
                    continue;
                }

                if (button.gameObject.scene.IsValid())
                {
                    Object.Destroy(button.gameObject);
                }
            }
        }

        public static void SetVisible(bool visible)
        {
            if (currentButton != null)
            {
                currentButton.gameObject.SetActive(visible);
            }
        }
    }
}
