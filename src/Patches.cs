namespace CollabCharting
{
    using ADOFAI;
    using HarmonyLib;

    [HarmonyPatch(typeof(scnEditor), "Start")]
    internal static class EditorStartPatch
    {
        private static void Postfix(scnEditor __instance)
        {
            EditorToolbarEntry.Install(__instance);
        }
    }

    [HarmonyPatch(typeof(scnEditor), "SelectFloor", new[] { typeof(scrFloor), typeof(bool) })]
    internal static class EditorSelectFloorPatch
    {
        private static void Postfix(scrFloor floorToSelect)
        {
            if (floorToSelect != null)
            {
                CollabRuntime.AcquireSelectionLock($"floor:{floorToSelect.seqID}");
            }
        }
    }

    [HarmonyPatch(typeof(scnEditor), "SelectDecoration", new[]
    {
        typeof(LevelEvent),
        typeof(bool),
        typeof(bool),
        typeof(bool),
        typeof(bool)
    })]
    internal static class EditorSelectDecorationPatch
    {
        private static void Postfix(LevelEvent levelEvent)
        {
            if (levelEvent == null)
            {
                return;
            }

            int index = scrDecorationManager.GetDecorationIndex(levelEvent);
            string id = index >= 0 ? index.ToString() : $"{levelEvent.eventType}:{levelEvent.floor}";
            CollabRuntime.AcquireSelectionLock($"decoration:{id}");
        }
    }

    [HarmonyPatch(typeof(scnEditor), "Undo")]
    internal static class EditorUndoPatch
    {
        private static bool Prefix()
        {
            return !CollabRuntime.InterceptUndoRedo(redo: false);
        }
    }

    [HarmonyPatch(typeof(scnEditor), "Redo")]
    internal static class EditorRedoPatch
    {
        private static bool Prefix()
        {
            return !CollabRuntime.InterceptUndoRedo(redo: true);
        }
    }

    [HarmonyPatch(typeof(InspectorPanel), "ShowPanel", new[] { typeof(LevelEventType), typeof(int) })]
    internal static class InspectorPanelShowPanelPatch
    {
        private static void Postfix(InspectorPanel __instance, LevelEventType eventType, int eventIndex)
        {
            if (__instance == null ||
                ADOBase.editor == null ||
                __instance != ADOBase.editor.levelEventsPanel ||
                eventType == LevelEventType.None ||
                ADOBase.editor.SelectionIsEmpty())
            {
                return;
            }

            int floor = ADOBase.editor.selectedFloors[0].seqID;
            CollabRuntime.AcquireSelectionLock($"event:{floor}:{eventType}:{eventIndex}");
        }
    }

    [HarmonyPatch(typeof(SaveStateScope), MethodType.Constructor, new[]
    {
        typeof(scnEditor),
        typeof(bool),
        typeof(bool),
        typeof(bool)
    })]
    internal static class SaveStateScopeCtorPatch
    {
        private static void Postfix(SaveStateScope __instance, bool dataHasChanged)
        {
            EditGuard.Begin(__instance, dataHasChanged);
            OperationCapture.Begin(__instance, dataHasChanged);
        }
    }

    [HarmonyPatch(typeof(SaveStateScope), "Dispose")]
    internal static class SaveStateScopeDisposePatch
    {
        private static void Postfix(SaveStateScope __instance)
        {
            EditGuard.End(__instance);
            OperationCapture.End(__instance);
        }
    }
}
