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

            if (!TryFindLockedSelection(out CollabLock? collabLock) || collabLock == null)
            {
                return;
            }

            contexts[scope] = new GuardContext
            {
                Snapshot = EditorStateAdapter.EncodeCurrentLevel(),
                Lock = collabLock
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
                ADOBase.editor.ShowNotification($"{context.Lock.OwnerName} 正在编辑，已撤回本地修改");
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
                if (floor != null && CollabRuntime.Session.TryGetRemoteLock($"floor:{floor.seqID}", out collabLock))
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

                int index = scrDecorationManager.GetDecorationIndex(decoration);
                string id = index >= 0 ? index.ToString() : $"{decoration.eventType}:{decoration.floor}";
                if (CollabRuntime.Session.TryGetRemoteLock($"decoration:{id}", out collabLock))
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
                if (CollabRuntime.Session.TryGetRemoteLock($"event:{floor}:{eventType}:{index}", out collabLock))
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class GuardContext
        {
            public string Snapshot { get; set; } = string.Empty;

            public CollabLock Lock { get; set; } = new CollabLock();
        }
    }
}
