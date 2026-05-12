using UnityEngine;
using UnityModManagerNet;

namespace CollabCharting
{
    public class Settings : UnityModManager.ModSettings
    {
        public bool EnableHotkey = true;

        public KeyCode OpenKey = KeyCode.F8;

        public bool DevelopmentMode = false;

        public string DevServerUrl = "http://127.0.0.1:5173/";

        public int Port = 39800;

        public void OnGUI(UnityModManager.ModEntry modEntry)
        {
            GUILayout.Label("Collab Charting");
            GUILayout.Label("多人协作制谱 WebUI 原型。");

            EnableHotkey = GUILayout.Toggle(EnableHotkey, $"启用快捷键打开 WebUI（{OpenKey}）");
            DevelopmentMode = GUILayout.Toggle(DevelopmentMode, "开发模式：打开 Vite dev server");

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

            if (GUILayout.Button("打开 WebUI", GUILayout.Width(160)))
            {
                Main.OpenOverlay();
            }
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
            return Load<Settings>(modEntry);
        }
    }
}
