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
            GUILayout.Label("多人制谱设置");

            GUILayout.Label("工作区用于保存加入协作房间时下载的谱面缓存。");
            GUILayout.BeginHorizontal();
            GUILayout.TextField(GetWorkspacePath(), GUILayout.Width(520));
            if (GUILayout.Button("浏览", GUILayout.Width(80)))
            {
                BrowseWorkspace();
            }

            if (GUILayout.Button("重置默认", GUILayout.Width(100)))
            {
                WorkspacePath = GetDefaultWorkspacePath();
            }

            GUILayout.EndHorizontal();
            GUILayout.Label($"谱面缓存位置：{ResourceSync.CacheRoot}");
            GUILayout.Label("协作入口只会显示在关卡编辑器里；离开编辑器会自动离开协作房间。");
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

        private void BrowseWorkspace()
        {
            string current = GetWorkspacePath();
            try
            {
                string[] selected = SFB.StandaloneFileBrowser.OpenFolderPanel(
                    "选择多人制谱工作区",
                    Directory.Exists(current) ? current : Persistence.GetLastUsedFolder(),
                    multiselect: false);
                if (selected.Length > 0 && !string.IsNullOrWhiteSpace(selected[0]))
                {
                    WorkspacePath = Uri.UnescapeDataString(selected[0].Replace("file:", string.Empty));
                }
            }
            catch (Exception ex)
            {
                Main.Mod?.Logger.Warning($"Open workspace browser failed: {ex.Message}");
            }
        }
    }
}
