using System;
using System.IO;
using UnityEngine;
using UnityModManagerNet;

namespace CollabCharting
{
    public class Settings : UnityModManager.ModSettings
    {
        public bool DevelopmentMode = false;

        public string DevServerUrl = "http://127.0.0.1:5173/";

        public int Port = 39800;

        public bool EnableEditorToolbarButton = true;

        public string WorkspacePath = string.Empty;

        public float SnapshotDebounceSeconds = 0.75f;

        public float ResourceResendMinSeconds = 2.0f;

        public void OnGUI(UnityModManager.ModEntry modEntry)
        {
            GUILayout.Label("Collab Charting");
            GUILayout.Label("多人协作制谱设置。协作入口只在关卡编辑器内显示。");

            EnableEditorToolbarButton = GUILayout.Toggle(EnableEditorToolbarButton, "在编辑器顶部显示“协作”按钮");
            DevelopmentMode = GUILayout.Toggle(DevelopmentMode, "开发模式：打开 Vite dev server");
            GUILayout.Space(6f);

            GUILayout.Label("协作工作区");
            GUILayout.BeginHorizontal();
            WorkspacePath = GUILayout.TextField(GetWorkspacePath(), GUILayout.Width(500));
            if (GUILayout.Button("使用当前关卡目录", GUILayout.Width(140)))
            {
                if (!string.IsNullOrWhiteSpace(EditorStateAdapter.CurrentLevelPath))
                {
                    WorkspacePath = Path.GetDirectoryName(EditorStateAdapter.CurrentLevelPath) ?? GetDefaultWorkspacePath();
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("重置默认", GUILayout.Width(100)))
            {
                WorkspacePath = GetDefaultWorkspacePath();
            }

            if (GUILayout.Button("创建目录", GUILayout.Width(100)))
            {
                Directory.CreateDirectory(GetWorkspacePath());
            }
            GUILayout.EndHorizontal();
            GUILayout.Label($"缓存目录：{ResourceSync.CacheRoot}");
            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Dev Server", GUILayout.Width(100));
            DevServerUrl = GUILayout.TextField(DevServerUrl, GUILayout.Width(360));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("端口", GUILayout.Width(100));
            if (int.TryParse(GUILayout.TextField(Port.ToString(), GUILayout.Width(100)), out int port))
            {
                Port = Mathf.Clamp(port, 1024, 65535);
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("WebUI 只能通过编辑器内“协作”按钮打开。Steam 邀请会自动进入编辑器并加载房主关卡。");
        }

        public void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            Save(modEntry);
        }

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }

        public static Settings Load(UnityModManager.ModEntry modEntry)
        {
            Settings settings = Load<Settings>(modEntry);
            if (string.IsNullOrWhiteSpace(settings.WorkspacePath))
            {
                settings.WorkspacePath = GetDefaultWorkspacePath();
            }

            return settings;
        }

        public string GetWorkspacePath()
        {
            return string.IsNullOrWhiteSpace(WorkspacePath) ? GetDefaultWorkspacePath() : WorkspacePath;
        }

        private static string GetDefaultWorkspacePath()
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(documents, "ADOFAI Collab Workspace");
        }
    }
}
