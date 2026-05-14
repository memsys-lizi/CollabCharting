using System.Collections.Generic;
using ADOFAI;

namespace CollabCharting
{
    internal static class EditGuard
    {
        private static readonly Dictionary<SaveStateScope, GuardContext> contexts = new Dictionary<SaveStateScope, GuardContext>();

        public static void Begin(SaveStateScope scope, bool dataHasChanged)
        {
            if (!dataHasChanged ||
                scope == null ||
                !CollabRuntime.Session.InLobby ||
                CollabRuntime.IsApplyingRemote ||
                !EditorStateAdapter.IsEditorReady)
            {
                return;
            }

            if (CollabRuntime.Session.IsBlockingUserInput)
            {
                contexts[scope] = new GuardContext
                {
                    Snapshot = EditorStateAdapter.EncodeCurrentLevel(),
                    Message = "协作同步初始化中，已撤回本地修改"
                };
                return;
            }

            if (!TryFindLockedSelection(out CollabLock? collabLock) || collabLock == null)
            {
                return;
            }

            contexts[scope] = new GuardContext
            {
                Snapshot = EditorStateAdapter.EncodeCurrentLevel(),
                Message = $"{collabLock.OwnerName} 正在编辑，已撤回本地修改"
            };
        }

        public static void End(SaveStateScope scope)
        {
            if (scope == null || !contexts.TryGetValue(scope, out GuardContext context))
            {
                return;
            }

            contexts.Remove(scope);
            if (!EditorStateAdapter.IsEditorReady)
            {
                return;
            }

            string current = EditorStateAdapter.EncodeCurrentLevel();
            if (EditorStateAdapter.HashLevelText(current) == EditorStateAdapter.HashLevelText(context.Snapshot))
            {
                return;
            }

            EditorStateAdapter.ApplySnapshot(context.Snapshot, "软锁保护");
            if (ADOBase.editor != null)
            {
                ADOBase.editor.ShowNotification(context.Message);
            }
        }

        private static bool TryFindLockedSelection(out CollabLock? collabLock)
        {
            collabLock = null;
            if (ADOBase.editor == null)
            {
                return false;
            }

            foreach (scrFloor floor in ADOBase.editor.selectedFloors)
            {
                if (floor != null && CollabRuntime.Session.TryGetRemoteLock(EditorLockTargets.Floor(floor.seqID), out collabLock))
                {
                    return true;
                }
            }

            foreach (LevelEvent decoration in ADOBase.editor.selectedDecorations)
            {
                if (decoration == null)
                {
                    continue;
                }

                if (CollabRuntime.Session.TryGetRemoteLock(EditorLockTargets.Decoration(decoration), out collabLock))
                {
                    return true;
                }
            }

            if (!ADOBase.editor.SelectionIsEmpty() &&
                ADOBase.editor.levelEventsPanel != null &&
                ADOBase.editor.levelEventsPanel.selectedEventType != LevelEventType.None)
            {
                int floor = ADOBase.editor.selectedFloors[0].seqID;
                LevelEventType eventType = ADOBase.editor.levelEventsPanel.selectedEventType;
                int index = ADOBase.editor.levelEventsPanel.EventNumOfTab(eventType);
                if (CollabRuntime.Session.TryGetRemoteLock(EditorLockTargets.Event(floor, eventType, index), out collabLock) ||
                    CollabRuntime.Session.TryGetRemoteLock(EditorLockTargets.Floor(floor), out collabLock))
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class GuardContext
        {
            public string Snapshot { get; set; } = string.Empty;

            public string Message { get; set; } = string.Empty;
        }
    }
}
