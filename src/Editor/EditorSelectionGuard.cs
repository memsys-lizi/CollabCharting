using System.Collections.Generic;
using ADOFAI;

namespace CollabCharting
{
    internal static class EditorSelectionGuard
    {
        public static bool CanSelectFloor(scrFloor floor)
        {
            if (!ShouldGuard() || floor == null)
            {
                return true;
            }

            return !TryBlock(new[] { EditorLockTargets.Floor(floor.seqID) });
        }

        public static bool CanMultiSelectFloors(scrFloor startFloor, scrFloor endFloor)
        {
            if (!ShouldGuard() || startFloor == null || endFloor == null || ADOBase.editor == null)
            {
                return true;
            }

            int start = ADOBase.editor.floors.IndexOf(startFloor);
            int end = ADOBase.editor.floors.IndexOf(endFloor);
            if (start < 0 || end < 0)
            {
                return true;
            }

            int min = start < end ? start : end;
            int max = start < end ? end : start;
            var targets = new List<string>();
            for (int i = min; i <= max; i++)
            {
                targets.Add(EditorLockTargets.Floor(ADOBase.editor.floors[i].seqID));
            }

            return !TryBlock(targets);
        }

        public static bool CanSelectDecoration(LevelEvent levelEvent)
        {
            if (!ShouldGuard() || levelEvent == null)
            {
                return true;
            }

            return !TryBlock(new[] { EditorLockTargets.Decoration(levelEvent) });
        }

        public static bool CanShowEventPanel(InspectorPanel panel, LevelEventType eventType, int eventIndex)
        {
            if (!ShouldGuard() ||
                panel == null ||
                ADOBase.editor == null ||
                panel != ADOBase.editor.levelEventsPanel ||
                eventType == LevelEventType.None ||
                ADOBase.editor.SelectionIsEmpty())
            {
                return true;
            }

            int floor = ADOBase.editor.selectedFloors[0].seqID;
            return !TryBlock(new[]
            {
                EditorLockTargets.Floor(floor),
                EditorLockTargets.Event(floor, eventType, eventIndex),
                EditorLockTargets.LegacyEvent(floor, eventType, eventIndex)
            });
        }

        private static bool ShouldGuard()
        {
            return CollabRuntime.Session.InLobby &&
                   !CollabRuntime.IsApplyingRemote &&
                   ADOBase.editor != null;
        }

        private static bool TryBlock(IEnumerable<string> targets)
        {
            foreach (string target in targets)
            {
                if (string.IsNullOrWhiteSpace(target))
                {
                    continue;
                }

                if (!CollabRuntime.Session.TryGetRemoteLock(target, out CollabLock? collabLock) || collabLock == null)
                {
                    continue;
                }

                if (ADOBase.editor != null)
                {
                    ADOBase.editor.ShowNotification($"{collabLock.OwnerName} 正在编辑，当前对象只读");
                }

                return true;
            }

            return false;
        }
    }
}
