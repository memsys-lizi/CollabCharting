using System;
using ADOFAI;
using Newtonsoft.Json.Linq;

namespace CollabCharting
{
    internal static class CollabWebCommands
    {
        public static void Register(Action<string, Func<JToken?, object?>> register)
        {
            register("collab.getStatus", _ => CollabRuntime.Session.GetStatus());
            register("collab.startAuth", _ => CollabRuntime.Session.StartAuth());
            register("collab.pollAuth", parameters =>
            {
                string loginId = parameters?["loginId"]?.Value<string>() ?? string.Empty;
                return CollabRuntime.Session.PollAuth(loginId);
            });
            register("collab.loginWithToken", parameters =>
            {
                string token = parameters?["relayToken"]?.Value<string>() ?? string.Empty;
                CollabAuthUser user = parameters?["user"]?.ToObject<CollabAuthUser>() ?? new CollabAuthUser();
                return CollabRuntime.Session.LoginWithToken(token, user);
            });
            register("collab.createLobby", _ => CollabRuntime.Session.CreateLobby());
            register("collab.leaveLobby", _ => CollabRuntime.Session.LeaveLobby());
            register("collab.forceSync", _ =>
            {
                if (ADOBase.editor != null)
                {
                    ADOBase.editor.ShowNotification("精确操作同步已启用，不再手动推送完整快照。");
                }

                return CollabRuntime.Session.GetStatus();
            });
            register("collab.joinLobby", parameters =>
            {
                string lobbyId = parameters?["lobbyId"]?.Value<string>() ?? string.Empty;
                return CollabRuntime.Session.JoinLobby(lobbyId);
            });
            register("collab.acquireLock", parameters =>
            {
                string target = parameters?["target"]?.Value<string>() ?? string.Empty;
                return CollabRuntime.Session.AcquireLock(target);
            });
            register("collab.releaseLock", parameters =>
            {
                string target = parameters?["target"]?.Value<string>() ?? string.Empty;
                return CollabRuntime.Session.ReleaseLock(target);
            });
            register("collab.openOverlay", _ =>
            {
                Main.OpenOverlay();
                return new { ok = true };
            });
        }
    }
}
