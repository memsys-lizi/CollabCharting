using System;
using Newtonsoft.Json.Linq;

namespace CollabCharting
{
    internal static class CollabWebCommands
    {
        public static void Register(Action<string, Func<JToken?, object?>> register)
        {
            register("collab.getStatus", _ => CollabRuntime.Session.GetStatus());
            register("collab.createLobby", _ => CollabRuntime.Session.CreateLobby());
            register("collab.leaveLobby", _ => CollabRuntime.Session.LeaveLobby());
            register("collab.getFriends", _ => CollabRuntime.Session.GetFriends());
            register("collab.openInviteDialog", _ => CollabRuntime.Session.OpenInviteDialog());
            register("collab.forceSnapshot", _ =>
            {
                string text = EditorStateAdapter.EncodeCurrentLevel();
                CollabRuntime.Session.PublishLocalSnapshot(text, text, "manual-sync");
                return CollabRuntime.Session.GetStatus();
            });
            register("collab.joinLobby", parameters =>
            {
                string lobbyId = parameters?["lobbyId"]?.Value<string>() ?? string.Empty;
                return CollabRuntime.Session.JoinLobby(lobbyId);
            });
            register("collab.inviteFriend", parameters =>
            {
                string steamId = parameters?["steamId"]?.Value<string>() ?? string.Empty;
                return CollabRuntime.Session.InviteFriend(steamId);
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
