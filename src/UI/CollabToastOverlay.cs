using System;
using System.Collections.Generic;
using UnityEngine;

namespace CollabCharting
{
    internal sealed class CollabToastOverlay : MonoBehaviour
    {
        private const float ToastLifetime = 5.5f;
        private const float FadeSeconds = 0.45f;
        private const int MaxToasts = 3;
        private static readonly List<Toast> Toasts = new List<Toast>();
        private static GUIStyle? containerStyle;
        private static GUIStyle? titleStyle;
        private static GUIStyle? bodyStyle;
        private static GUIStyle? avatarTextStyle;
        private static Texture2D? panelTexture;
        private static Texture2D? avatarFallbackTexture;
        private static CollabToastOverlay? instance;

        public static void Ensure()
        {
            if (instance != null)
            {
                return;
            }

            var host = new GameObject("CollabChartingToastOverlay");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<CollabToastOverlay>();
        }

        public static void Push(string message, string userId = "", string name = "")
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            Ensure();
            Toasts.Insert(0, new Toast
            {
                Message = StripTimestamp(message),
                UserId = userId,
                Name = name,
                CreatedAt = Time.unscaledTime
            });

            while (Toasts.Count > MaxToasts)
            {
                Toasts.RemoveAt(Toasts.Count - 1);
            }
        }

        public static void Clear()
        {
            Toasts.Clear();
        }

        private void OnGUI()
        {
            if (!CollabRuntime.Session.InLobby || ADOBase.editor == null || Toasts.Count == 0)
            {
                return;
            }

            EnsureStyles();

            float width = Mathf.Min(340f, Screen.width * 0.32f);
            float x = Screen.width - width - 18f;
            float y = 18f;

            for (int i = Toasts.Count - 1; i >= 0; i--)
            {
                Toast toast = Toasts[i];
                float age = Time.unscaledTime - toast.CreatedAt;
                if (age >= ToastLifetime)
                {
                    Toasts.RemoveAt(i);
                    continue;
                }

                float alpha = age > ToastLifetime - FadeSeconds
                    ? Mathf.InverseLerp(ToastLifetime, ToastLifetime - FadeSeconds, age)
                    : 1f;

                DrawToast(new Rect(x, y, width, 54f), toast, alpha);
                y += 62f;
            }
        }

        private static void DrawToast(Rect rect, Toast toast, float alpha)
        {
            Color oldColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Box(rect, GUIContent.none, containerStyle);

            Rect avatarRect = new Rect(rect.x + 10f, rect.y + 9f, 36f, 36f);
            GUI.DrawTexture(avatarRect, avatarFallbackTexture, ScaleMode.ScaleToFit, true);
            GUI.Label(avatarRect, GetInitial(toast), avatarTextStyle);

            string actor = string.IsNullOrWhiteSpace(toast.Name) ? "协作动态" : toast.Name;
            GUI.Label(new Rect(rect.x + 56f, rect.y + 8f, rect.width - 68f, 18f), actor, titleStyle);
            GUI.Label(new Rect(rect.x + 56f, rect.y + 27f, rect.width - 68f, 20f), toast.Message, bodyStyle);
            GUI.color = oldColor;
        }

        private static void EnsureStyles()
        {
            if (containerStyle != null)
            {
                return;
            }

            panelTexture = MakeTexture(new Color(0.055f, 0.08f, 0.11f, 0.88f));
            avatarFallbackTexture = MakeTexture(new Color(0.12f, 0.31f, 0.45f, 0.95f));
            containerStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = panelTexture },
                border = new RectOffset(8, 8, 8, 8),
                padding = new RectOffset(0, 0, 0, 0)
            };
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.78f, 0.93f, 1f, 1f) },
                clipping = TextClipping.Clip
            };
            bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.92f, 0.96f, 1f, 1f) },
                clipping = TextClipping.Clip
            };
            avatarTextStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
        }

        private static Texture2D MakeTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private static string StripTimestamp(string message)
        {
            if (message.Length > 11 && message[0] == '[' && message[9] == ']')
            {
                return message.Substring(11);
            }

            return message;
        }

        private static string GetInitial(Toast toast)
        {
            string text = string.IsNullOrWhiteSpace(toast.Name) ? toast.Message : toast.Name;
            return string.IsNullOrWhiteSpace(text) ? "协" : text.Substring(0, 1);
        }

        private sealed class Toast
        {
            public string Message { get; set; } = string.Empty;

            public string UserId { get; set; } = string.Empty;

            public string Name { get; set; } = string.Empty;

            public float CreatedAt { get; set; }
        }
    }
}
