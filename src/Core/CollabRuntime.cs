using System;

namespace CollabCharting
{
    internal static class CollabRuntime
    {
        private static bool initialized;
        private static bool hasSeenEditorDuringLobby;
        private static float editorMissingSeconds;
        private static float selectionLockRefreshSeconds;
        private static string lastSelectionLockTarget = string.Empty;
        private static string activeSelectionLockTarget = string.Empty;

        public static CollabSessionManager Session { get; } = new CollabSessionManager();

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
                RefreshSelectionLock(dt);
                EditorCommandCapture.Tick();
                OperationCapture.Update(dt);
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
            EditorCommandCapture.Reset();
            Session.Dispose();
        }

        public static bool AcquireSelectionLock(string target)
        {
            if (!Session.InLobby || string.IsNullOrWhiteSpace(target) || IsApplyingRemote)
            {
                return false;
            }

            try
            {
                object result = Session.AcquireLock(target);
                return result is CollabLock collabLock && Session.IsLocalLock(collabLock);
            }
            catch (Exception ex)
            {
                Main.Mod?.Logger.Warning($"Acquire collab lock failed: {ex.Message}");
                return false;
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

        private static void RefreshSelectionLock(float dt)
        {
            if (!Session.InLobby || IsApplyingRemote || ADOBase.editor == null)
            {
                selectionLockRefreshSeconds = 0f;
                lastSelectionLockTarget = string.Empty;
                activeSelectionLockTarget = string.Empty;
                return;
            }

            string target = GetCurrentSelectionLockTarget();
            if (string.IsNullOrWhiteSpace(target))
            {
                selectionLockRefreshSeconds = 0f;
                lastSelectionLockTarget = string.Empty;
                ReleaseActiveSelectionLock();
                return;
            }

            selectionLockRefreshSeconds += dt;
            if (string.Equals(target, lastSelectionLockTarget, StringComparison.Ordinal) &&
                selectionLockRefreshSeconds < 2f)
            {
                return;
            }

            lastSelectionLockTarget = target;
            selectionLockRefreshSeconds = 0f;
            if (AcquireSelectionLock(target))
            {
                activeSelectionLockTarget = target;
            }
        }

        private static string GetCurrentSelectionLockTarget()
        {
            scnEditor editor = ADOBase.editor;
            if (editor.selectedDecorations != null && editor.selectedDecorations.Count > 0)
            {
                int index = scrDecorationManager.GetDecorationIndex(editor.selectedDecorations[0]);
                if (index >= 0)
                {
                    return EditorLockTargets.Decoration(editor.selectedDecorations[0]);
                }
            }

            if (!editor.SelectionIsEmpty() &&
                editor.levelEventsPanel != null &&
                editor.levelEventsPanel.selectedEventType != ADOFAI.LevelEventType.None)
            {
                int floor = editor.selectedFloors[0].seqID;
                ADOFAI.LevelEventType eventType = editor.levelEventsPanel.selectedEventType;
                int index = editor.levelEventsPanel.EventNumOfTab(eventType);
                return EditorLockTargets.Event(floor, eventType, index);
            }

            if (!editor.SelectionIsEmpty())
            {
                return EditorLockTargets.Floor(editor.selectedFloors[0].seqID);
            }

            return string.Empty;
        }

        private static void ReleaseActiveSelectionLock()
        {
            if (string.IsNullOrWhiteSpace(activeSelectionLockTarget) || !Session.InLobby || IsApplyingRemote)
            {
                activeSelectionLockTarget = string.Empty;
                return;
            }

            try
            {
                Session.ReleaseLock(activeSelectionLockTarget);
            }
            catch (Exception ex)
            {
                Main.Mod?.Logger.Warning($"Release collab selection lock failed: {ex.Message}");
            }
            finally
            {
                activeSelectionLockTarget = string.Empty;
            }
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
