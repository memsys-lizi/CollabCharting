using System;

namespace CollabCharting
{
    internal static class CollabRuntime
    {
        private static bool initialized;
        private static bool hasSeenEditorDuringLobby;
        private static float editorMissingSeconds;

        public static SteamSessionManager Session { get; } = new SteamSessionManager();

        public static bool IsApplyingRemote { get; set; }

        public static void Initialize()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            Session.Start();
            OperationCapture.ResetBaseline();
        }

        public static void Update(float dt)
        {
            if (!initialized)
            {
                return;
            }

            try
            {
                Session.Update(dt);
                EnforceEditorLifecycle(dt);
                OperationCapture.Update(dt);
                EditorToolbarEntry.Tick();
                EditorLockOverlay.Update();
            }
            catch (Exception ex)
            {
                Main.Mod?.Logger.Error($"Collab runtime update failed: {ex}");
            }
        }

        public static void Shutdown()
        {
            if (!initialized)
            {
                return;
            }

            initialized = false;
            EditorLockOverlay.Clear();
            CollabToastOverlay.Clear();
            Session.Dispose();
        }

        public static void AcquireSelectionLock(string target)
        {
            if (!Session.InLobby || string.IsNullOrWhiteSpace(target) || IsApplyingRemote)
            {
                return;
            }

            try
            {
                Session.AcquireLock(target);
            }
            catch (Exception ex)
            {
                Main.Mod?.Logger.Warning($"Acquire collab lock failed: {ex.Message}");
            }
        }

        public static bool InterceptUndoRedo(bool redo)
        {
            if (!Session.InLobby || IsApplyingRemote)
            {
                return false;
            }

            try
            {
                Session.RequestCollaborativeUndoRedo(redo);
            }
            catch (Exception ex)
            {
                Main.Mod?.Logger.Warning($"Collaborative undo/redo failed: {ex.Message}");
            }

            return true;
        }

        private static void EnforceEditorLifecycle(float dt)
        {
            if (!Session.InLobby)
            {
                hasSeenEditorDuringLobby = false;
                editorMissingSeconds = 0f;
                return;
            }

            if (ADOBase.isLevelEditor)
            {
                hasSeenEditorDuringLobby = true;
                editorMissingSeconds = 0f;
                Session.MarkEditorAvailable();
                return;
            }

            if (!hasSeenEditorDuringLobby || Session.IsWaitingForEditor)
            {
                return;
            }

            editorMissingSeconds += dt;
            if (editorMissingSeconds < 1.0f)
            {
                return;
            }

            Main.Mod?.Logger.Log("Leaving collab lobby because the player left the level editor.");
            Session.LeaveLobby();
            editorMissingSeconds = 0f;
            hasSeenEditorDuringLobby = false;
        }
    }
}
