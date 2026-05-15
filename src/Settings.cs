using System;
using System.IO;
using UnityEngine;
using UnityModManagerNet;

namespace CollabCharting
{
    public class Settings : UnityModManager.ModSettings
    {
        public string WorkspacePath = string.Empty;

        public float SnapshotDebounceSeconds = 0.75f;

        public float ResourceResendMinSeconds = 2.0f;

        private string joinRoomId = string.Empty;
        private string loginId = string.Empty;
        private string panelMessage = string.Empty;
        private float authPollTimer;
        private bool authPolling;

        public void OnGUI(UnityModManager.ModEntry modEntry)
        {
            CollabStatus status = CollabRuntime.Session.GetStatus();
            DrawHeader(status);
            DrawFeedback(status);
            DrawAccountSection(status);
            DrawRoomSection(status);
            DrawCacheSection();
        }

        public void TickAuth(float dt)
        {
            if (!authPolling || string.IsNullOrWhiteSpace(loginId))
            {
                return;
            }

            authPollTimer += dt;
            if (authPollTimer < 1.5f)
            {
                return;
            }

            authPollTimer = 0f;
            PollAuthOnce();
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

        private static void DrawHeader(CollabStatus status)
        {
            GUILayout.Label("Collab Charting");
            GUILayout.Label(GetPlayerStatus(status));
            GUILayout.Space(6f);
        }

        private void DrawFeedback(CollabStatus status)
        {
            string message = FriendlyMessage(panelMessage);
            string error = FriendlyMessage(status.LastError);
            if (string.IsNullOrWhiteSpace(message) && string.IsNullOrWhiteSpace(error))
            {
                return;
            }

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("提示");
            if (!string.IsNullOrWhiteSpace(message))
            {
                GUILayout.Label(message);
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                GUILayout.Label(error);
            }

            GUILayout.EndVertical();
            GUILayout.Space(4f);
        }

        private void DrawAccountSection(CollabStatus status)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("账号");
            GUILayout.Label(FormatAccount(status));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(status.AccountAvailable ? "切换账号" : "登录 ADOFAITools", GUILayout.Width(160)))
            {
                StartAuth();
            }

            if (authPolling && GUILayout.Button("刷新状态", GUILayout.Width(100)))
            {
                PollAuthOnce();
            }

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.Space(4f);
        }

        private void DrawRoomSection(CollabStatus status)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("房间");
            DrawRoomStatus(status);

            GUILayout.BeginHorizontal();
            GUI.enabled = status.AccountAvailable && !status.InLobby;
            if (GUILayout.Button("创建房间", GUILayout.Width(120)))
            {
                RunPanelAction(() => CollabRuntime.Session.CreateLobby());
            }

            GUI.enabled = status.InLobby;
            if (GUILayout.Button("离开房间", GUILayout.Width(120)))
            {
                RunPanelAction(() => CollabRuntime.Session.LeaveLobby());
            }

            if (status.InLobby && GUILayout.Button("复制房间码", GUILayout.Width(120)))
            {
                GUIUtility.systemCopyBuffer = status.LobbyId;
                panelMessage = $"已复制房间码：{status.LobbyId}";
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("房间码", GUILayout.Width(60));
            joinRoomId = GUILayout.TextField(joinRoomId, GUILayout.Width(220));
            GUI.enabled = status.AccountAvailable && !status.InLobby;
            if (GUILayout.Button("加入房间", GUILayout.Width(110)))
            {
                RunPanelAction(() => CollabRuntime.Session.JoinLobby(joinRoomId));
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (status.InLobby)
            {
                DrawMembers(status);
                GUILayout.Label("离开编辑器时会自动退出协作房间。");
            }

            GUILayout.EndVertical();
            GUILayout.Space(4f);
        }

        private static void DrawMembers(CollabStatus status)
        {
            GUILayout.Space(6f);
            GUILayout.Label($"房间成员（{status.Members.Count}）");
            if (status.Members.Count == 0)
            {
                GUILayout.Label("正在等待成员信息。");
                return;
            }

            foreach (CollabMember member in status.Members)
            {
                string role = member.IsHost ? "房主" : "成员";
                string local = member.IsLocal ? "，我" : string.Empty;
                GUILayout.Label($"{member.Name}  ·  {role}{local}");
            }
        }

        private void DrawCacheSection()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("缓存位置");
            GUILayout.Label("加入别人房间时，下载的谱面和资源会临时保存到这里。平时不用修改。");
            GUILayout.BeginHorizontal();
            GUILayout.TextField(GetWorkspacePath(), GUILayout.Width(520));
            if (GUILayout.Button("浏览", GUILayout.Width(80)))
            {
                BrowseWorkspace();
            }

            if (GUILayout.Button("恢复默认", GUILayout.Width(100)))
            {
                WorkspacePath = GetDefaultWorkspacePath();
                panelMessage = "已恢复默认缓存位置。";
            }

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void StartAuth()
        {
            RunPanelAction(() =>
            {
                CollabAuthStart auth = CollabRuntime.Session.StartAuth();
                loginId = auth.LoginId;
                authPolling = true;
                authPollTimer = 0f;
                panelMessage = "已打开 ADOFAITools 登录页面，登录完成后会自动刷新。";
                Application.OpenURL(auth.AuthorizationUrl);
                return auth;
            });
        }

        private void PollAuthOnce()
        {
            RunPanelAction(() =>
            {
                object raw = CollabRuntime.Session.PollAuth(loginId);
                var poll = raw as CollabAuthPoll;
                if (poll == null)
                {
                    return raw;
                }

                if (poll.Status == "pending")
                {
                    panelMessage = "等待浏览器登录完成。";
                }
                else if (poll.Status == "ok")
                {
                    authPolling = false;
                    loginId = string.Empty;
                    panelMessage = "ADOFAITools 登录完成。";
                }
                else
                {
                    authPolling = false;
                    loginId = string.Empty;
                    panelMessage = string.IsNullOrWhiteSpace(poll.Message)
                        ? "登录失败或已过期，请重新登录。"
                        : poll.Message;
                }

                return raw;
            });
        }

        private void RunPanelAction(Func<object> action)
        {
            try
            {
                panelMessage = string.Empty;
                action();
                if (string.IsNullOrWhiteSpace(panelMessage))
                {
                    panelMessage = "操作已提交。";
                }
            }
            catch (Exception ex)
            {
                panelMessage = FriendlyMessage(ex.InnerException?.Message ?? ex.Message);
                Main.Mod?.Logger.Warning($"Collab panel action failed: {ex}");
            }
        }

        private static string FormatAccount(CollabStatus status)
        {
            if (!status.AccountAvailable)
            {
                return "未登录";
            }

            return $"已登录：{status.LocalName}";
        }

        private static void DrawRoomStatus(CollabStatus status)
        {
            if (!status.InLobby)
            {
                GUILayout.Label("还没有加入房间。可以创建房间，或输入朋友发来的房间码加入。");
                return;
            }

            string role = status.IsHost ? "你是房主" : "你是成员";
            GUILayout.Label($"房间码：{status.LobbyId}");
            GUILayout.Label($"{role}  ·  {FormatSyncState(status.SyncState)}");
        }

        private static string GetPlayerStatus(CollabStatus status)
        {
            if (!status.AccountAvailable)
            {
                return "登录后即可创建或加入协作房间。";
            }

            if (!status.InLobby)
            {
                return "已登录，可以开始协作。";
            }

            return status.IsHost ? "正在作为房主协作。" : "正在加入房主的协作房间。";
        }

        private static string FormatSyncState(string state)
        {
            switch (state)
            {
                case "creating":
                    return "正在创建";
                case "joining":
                    return "正在加入";
                case "syncing":
                case "sending":
                    return "正在同步谱面";
                case "queued":
                    return "等待编辑器就绪";
                case "error":
                    return "需要处理错误";
                case "hosting":
                case "synced":
                case "idle":
                default:
                    return "已连接";
            }
        }

        private static string FriendlyMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return string.Empty;
            }

            if (message.IndexOf("500", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("Internal Server Error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("服务器未配置", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "协作服务暂时还没准备好，请稍后再试。";
            }

            if (message.IndexOf("Unable to connect", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("No such host", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "暂时无法连接协作服务，请检查网络后重试。";
            }

            return message;
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
