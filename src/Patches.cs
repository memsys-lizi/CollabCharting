namespace CollabCharting
{
    using System.Collections.Generic;
    using ADOFAI;
    using ADOFAI.LevelEditor.Controls;
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

    [HarmonyPatch(typeof(scnEditor), "CreateFloor", new[] { typeof(char), typeof(bool), typeof(bool) })]
    internal static class EditorCreateCharFloorPatch
    {
        private static void Prefix(out EditorCommandCapture.CommandScope __state)
        {
            __state = EditorCommandCapture.Begin("command:path.createFloor.char");
        }

        private static void Postfix(EditorCommandCapture.CommandScope __state)
        {
            EditorCommandCapture.End(__state);
        }
    }

    [HarmonyPatch(typeof(scnEditor), "CreateFloor", new[] { typeof(float), typeof(bool), typeof(bool) })]
    internal static class EditorCreateFloatFloorPatch
    {
        private static void Prefix(out EditorCommandCapture.CommandScope __state)
        {
            __state = EditorCommandCapture.Begin("command:path.createFloor.float");
        }

        private static void Postfix(EditorCommandCapture.CommandScope __state)
        {
            EditorCommandCapture.End(__state);
        }
    }

    [HarmonyPatch(typeof(scnEditor), "CreateFloorWithCharOrAngle", new[] { typeof(float), typeof(char), typeof(bool), typeof(bool) })]
    internal static class EditorCreateFloorWithCharOrAnglePatch
    {
        private static void Prefix(out EditorCommandCapture.CommandScope __state)
        {
            __state = EditorCommandCapture.Begin("command:path.createFloor");
        }

        private static void Postfix(EditorCommandCapture.CommandScope __state)
        {
            EditorCommandCapture.End(__state);
        }
    }

    [HarmonyPatch(typeof(scnEditor), "DeleteSingleSelection", new[] { typeof(bool) })]
    internal static class EditorDeleteSingleSelectionPatch
    {
        private static void Prefix(out EditorCommandCapture.CommandScope __state)
        {
            __state = EditorCommandCapture.Begin("command:path.deleteSingleSelection");
        }

        private static void Postfix(EditorCommandCapture.CommandScope __state)
        {
            EditorCommandCapture.End(__state);
        }
    }

    [HarmonyPatch(typeof(scnEditor), "DeleteMultiSelection", new[] { typeof(bool) })]
    internal static class EditorDeleteMultiSelectionPatch
    {
        private static void Prefix(out EditorCommandCapture.CommandScope __state)
        {
            __state = EditorCommandCapture.Begin("command:path.deleteMultiSelection");
        }

        private static void Postfix(EditorCommandCapture.CommandScope __state)
        {
            EditorCommandCapture.End(__state);
        }
    }

    [HarmonyPatch(typeof(scnEditor), "DeleteSubsequentFloors")]
    internal static class EditorDeleteSubsequentFloorsPatch
    {
        private static void Prefix(out EditorCommandCapture.CommandScope __state)
        {
            __state = EditorCommandCapture.Begin("command:path.deleteSubsequentFloors");
        }

        private static void Postfix(EditorCommandCapture.CommandScope __state)
        {
            EditorCommandCapture.End(__state);
        }
    }

    [HarmonyPatch(typeof(scnEditor), "DeletePrecedingFloors")]
    internal static class EditorDeletePrecedingFloorsPatch
    {
        private static void Prefix(out EditorCommandCapture.CommandScope __state)
        {
            __state = EditorCommandCapture.Begin("command:path.deletePrecedingFloors");
        }

        private static void Postfix(EditorCommandCapture.CommandScope __state)
        {
            EditorCommandCapture.End(__state);
        }
    }

    [HarmonyPatch(typeof(scnEditor), "AddDecoration", new[] { typeof(LevelEvent), typeof(int) })]
    internal static class EditorAddDecorationPatch
    {
        private static void Prefix(out EditorCommandCapture.CommandScope __state)
        {
            __state = EditorCommandCapture.Begin("command:decoration.add");
        }

        private static void Postfix(EditorCommandCapture.CommandScope __state)
        {
            EditorCommandCapture.End(__state);
        }
    }

    [HarmonyPatch(typeof(scnEditor), "AddDecoration", new[] { typeof(LevelEventType), typeof(int) })]
    internal static class EditorAddDecorationTypePatch
    {
        private static void Prefix(out EditorCommandCapture.CommandScope __state)
        {
            __state = EditorCommandCapture.Begin("command:decoration.add");
        }

        private static void Postfix(EditorCommandCapture.CommandScope __state)
        {
            EditorCommandCapture.End(__state);
        }
    }

    [HarmonyPatch(typeof(scnEditor), "RemoveEvent", new[] { typeof(LevelEvent), typeof(bool) })]
    internal static class EditorRemoveEventPatch
    {
        private static void Prefix(out EditorCommandCapture.CommandScope __state)
        {
            __state = EditorCommandCapture.Begin("command:event.remove");
        }

        private static void Postfix(EditorCommandCapture.CommandScope __state)
        {
            EditorCommandCapture.End(__state);
        }
    }

    [HarmonyPatch(typeof(scnEditor), "RemoveEvents", new[] { typeof(List<LevelEvent>) })]
    internal static class EditorRemoveEventsPatch
    {
        private static void Prefix(out EditorCommandCapture.CommandScope __state)
        {
            __state = EditorCommandCapture.Begin("command:event.removeMany");
        }

        private static void Postfix(EditorCommandCapture.CommandScope __state)
        {
            EditorCommandCapture.End(__state);
        }
    }

    [HarmonyPatch(typeof(scnEditor), "DeleteMultiSelectionDecorations")]
    internal static class EditorDeleteMultiSelectionDecorationsPatch
    {
        private static void Prefix(out EditorCommandCapture.CommandScope __state)
        {
            __state = EditorCommandCapture.Begin("command:decoration.deleteMultiSelection");
        }

        private static void Postfix(EditorCommandCapture.CommandScope __state)
        {
            EditorCommandCapture.End(__state);
        }
    }

    [HarmonyPatch(typeof(scnEditor), "DragDecorationsStart")]
    internal static class EditorDragDecorationsStartPatch
    {
        private static void Prefix()
        {
            EditorCommandCapture.BeginDecorationDrag();
        }
    }

    [HarmonyPatch(typeof(PropertyControl_DecorationsList), "OnItemDropMiddle")]
    internal static class DecorationsListDropMiddlePatch
    {
        private static void Prefix(out EditorCommandCapture.CommandScope __state)
        {
            __state = EditorCommandCapture.Begin("command:decoration.reorder");
        }

        private static void Postfix(EditorCommandCapture.CommandScope __state)
        {
            EditorCommandCapture.End(__state);
        }
    }

    [HarmonyPatch(typeof(PropertyControl_DecorationsList), "OnItemDropSides")]
    internal static class DecorationsListDropSidesPatch
    {
        private static void Prefix(out EditorCommandCapture.CommandScope __state)
        {
            __state = EditorCommandCapture.Begin("command:decoration.reorder");
        }

        private static void Postfix(EditorCommandCapture.CommandScope __state)
        {
            EditorCommandCapture.End(__state);
        }
    }
}
