using UnityEngine;

namespace CollabCharting
{
    internal sealed class CollabSyncBlockingOverlay : MonoBehaviour
    {
        private static CollabSyncBlockingOverlay? instance;
        private static GUIStyle? titleStyle;
        private static GUIStyle? bodyStyle;
        private static GUIStyle? progressTextStyle;
        private static Texture2D? overlayTexture;
        private static Texture2D? progressTexture;
        private static Texture2D? progressBackTexture;

        public static void Ensure()
        {
            if (instance != null)
            {
                return;
            }

            var host = new GameObject("CollabChartingSyncBlockingOverlay");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<CollabSyncBlockingOverlay>();
        }

        private void OnGUI()
        {
            if (!CollabRuntime.Session.IsBlockingUserInput || ADOBase.editor == null)
            {
                return;
            }

            EnsureStyles();
            GUI.depth = -10000;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), overlayTexture);

            float panelWidth = Mathf.Min(460f, Screen.width - 64f);
            float panelHeight = 126f;
            var panel = new Rect(
                (Screen.width - panelWidth) * 0.5f,
                (Screen.height - panelHeight) * 0.5f,
                panelWidth,
                panelHeight);

            float progress = Mathf.Clamp01(CollabRuntime.Session.SyncProgress);
            GUI.Label(new Rect(panel.x, panel.y, panel.width, 36f), "正在初始化协作谱面", titleStyle);
            GUI.Label(new Rect(panel.x, panel.y + 42f, panel.width, 24f), BuildSubtitle(), bodyStyle);

            var bar = new Rect(panel.x, panel.y + 78f, panel.width, 10f);
            GUI.DrawTexture(bar, progressBackTexture);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * progress, bar.height), progressTexture);
            GUI.Label(new Rect(panel.x, panel.y + 96f, panel.width, 24f), $"{Mathf.RoundToInt(progress * 100f)}%", progressTextStyle);
        }

        private static string BuildSubtitle()
        {
            string state = CollabRuntime.Session.SyncState;
            if (state == "joining")
            {
                return "正在加入协作房间";
            }

            if (state == "syncing")
            {
                return "正在下载并加载房主谱面资源";
            }

            if (state == "queued")
            {
                return "正在等待编辑器准备完成";
            }

            return "请稍候，正在准备协作环境";
        }

        private static void EnsureStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            overlayTexture = Solid(new Color(0.01f, 0.015f, 0.02f, 0.88f));
            progressTexture = Solid(new Color(0.38f, 0.78f, 1f, 1f));
            progressBackTexture = Solid(new Color(0.11f, 0.18f, 0.24f, 1f));
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 28,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            bodyStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 15,
                normal = { textColor = new Color(0.72f, 0.86f, 0.94f, 1f) }
            };
            progressTextStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                normal = { textColor = new Color(0.58f, 0.82f, 0.96f, 1f) }
            };
        }

        private static Texture2D Solid(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }
    }
}
