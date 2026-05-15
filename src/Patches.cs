namespace CollabCharting
{
    using System.Collections.Generic;
    using ADOFAI;
    using ADOFAI.LevelEditor.Controls;
    using HarmonyLib;

    [HarmonyPatch(typeof(scnEditor), "SelectFloor", new[] { typeof(scrFloor), typeof(bool) })]
    internal static class EditorSelectFloorPatch
    {
        private static bool Prefix(scrFloor floorToSelect, out bool __state)
        {
            __state = false;
            if (EditorInputBlocker.ShouldBlockEditorAction())
            {
                return false;
            }

            __state = EditorSelectionGuard.CanSelectFloor(floorToSelect);
            return __state;
        }

        private static void Postfix(scrFloor floorToSelect, bool __state)
        {
            if (__state && floorToSelect != null)
            {
                CollabRuntime.AcquireSelectionLock(EditorLockTargets.Floor(floorToSelect.seqID));
            }
        }
    }

    [HarmonyPatch(typeof(scnEditor), "MultiSelectFloors", new[] { typeof(scrFloor), typeof(scrFloor), typeof(bool) })]
    internal static class EditorMultiSelectFloorsPatch
    {
        private static bool Prefix(scrFloor startFloor, scrFloor endFloor)
        {
            if (EditorInputBlocker.ShouldBlockEditorAction())
            {
                return false;
            }

            return EditorSelectionGuard.CanMultiSelectFloors(startFloor, endFloor);
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
        private static bool Prefix(LevelEvent levelEvent, out bool __state)
        {
            __state = false;
            if (EditorInputBlocker.ShouldBlockEditorAction())
            {
                return false;
            }

            __state = EditorSelectionGuard.CanSelectDecoration(levelEvent);
            return __state;
        }

        private static void Postfix(LevelEvent levelEvent, bool __state)
        {
            if (!__state || levelEvent == null)
            {
                return;
            }

            CollabRuntime.AcquireSelectionLock(EditorLockTargets.Decoration(levelEvent));
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
        private static bool Prefix(InspectorPanel __instance, LevelEventType eventType, int eventIndex, out bool __state)
        {
            __state = false;
            if (EditorInputBlocker.ShouldBlockEditorAction())
            {
                return false;
            }

            __state = EditorSelectionGuard.CanShowEventPanel(__instance, eventType, eventIndex);
            return __state;
        }

        private static void Postfix(InspectorPanel __instance, LevelEventType eventType, int eventIndex, bool __state)
        {
            if (!__state ||
                __instance == null ||
                ADOBase.editor == null ||
                __instance != ADOBase.editor.levelEventsPanel ||
                eventType == LevelEventType.None ||
                ADOBase.editor.SelectionIsEmpty())
            {
                return;
            }

            int floor = ADOBase.editor.selectedFloors[0].seqID;
            CollabRuntime.AcquireSelectionLock(EditorLockTargets.Event(floor, eventType, eventIndex));
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
        private static bool Prefix(out EditorCommandCapture.CommandScope __state)
        {
            __state = null!;
            if (EditorInputBlocker.ShouldBlockEditorAction())
            {
                return false;
            }

            __state = EditorCommandCapture.Begin("command:path.createFloor.char");
            return true;
        }

        private static void Postfix(EditorCommandCapture.CommandScope __state)
        {
            EditorCommandCapture.End(__state);
        }
    }

    [HarmonyPatch(typeof(scnEditor), "CreateFloor", new[] { typeof(float), typeof(bool), typeof(bool) })]
    internal static class EditorCreateFloatFloorPatch
    {
        private static bool Prefix(out EditorCommandCapture.CommandScope __state)
        {
            __state = null!;
            if (EditorInputBlocker.ShouldBlockEditorAction())
            {
                return false;
            }

            __state = EditorCommandCapture.Begin("command:path.createFloor.float");
            return true;
        }

        private static void Postfix(EditorCommandCapture.CommandScope __state)
        {
            EditorCommandCapture.End(__state);
        }
    }

    [HarmonyPatch(typeof(scnEditor), "CreateFloorWithCharOrAngle", new[] { typeof(float), typeof(char), typeof(bool), typeof(bool) })]
    internal static class EditorCreateFloorWithCharOrAnglePatch
    {
        private static bool Prefix(out EditorCommandCapture.CommandScope __state)
        {
            __state = null!;
            if (EditorInputBlocker.ShouldBlockEditorAction())
            {
                return false;
            }

            __state = EditorCommandCapture.Begin("command:path.createFloor");
            return true;
        }

        private static void Postfix(EditorCommandCapture.CommandScope __state)
        {
            EditorCommandCapture.End(__state);
        }
    }

    [HarmonyPatch(typeof(scnEditor), "DeleteSingleSelection", new[] { typeof(bool) })]
    internal static class EditorDeleteSingleSelectionPatch
    {
        private static bool Prefix(out EditorCommandCapture.CommandScope __state)
        {
            __state = null!;
            if (EditorInputBlocker.ShouldBlockEditorAction())
            {
                return false;
            }

            __state = EditorCommandCapture.Begin("command:path.deleteSingleSelection");
            return true;
        }

        private static void Postfix(EditorCommandCapture.CommandScope __state)
        {
            EditorCommandCapture.End(__state);
        }
    }

    [HarmonyPatch(typeof(scnEditor), "DeleteMultiSelection", new[] { typeof(bool) })]
    internal static class EditorDeleteMultiSelectionPatch
    {
        private static bool Prefix(out EditorCommandCapture.CommandScope __state)
        {
            __state = null!;
            if (EditorInputBlocker.ShouldBlockEditorAction())
            {
                return false;
            }

            __state = EditorCommandCapture.Begin("command:path.deleteMultiSelection");
            return true;
        }

        private static void Postfix(EditorCommandCapture.CommandScope __state)
        {
            EditorCommandCapture.End(__state);
        }
    }

    [HarmonyPatch(typeof(scnEditor), "DeleteSubsequentFloors")]
    internal static class EditorDeleteSubsequentFloorsPatch
    {
        private static bool Prefix(out EditorCommandCapture.CommandScope __state)
        {
            __state = null!;
            if (EditorInputBlocker.ShouldBlockEditorAction())
            {
                return false;
            }

            __state = EditorCommandCapture.Begin("command:path.deleteSubsequentFloors");
            return true;
        }

        private static void Postfix(EditorCommandCapture.CommandScope __state)
        {
            EditorCommandCapture.End(__state);
        }
    }

    [HarmonyPatch(typeof(scnEditor), "DeletePrecedingFloors")]
    internal static class EditorDeletePrecedingFloorsPatch
    {
        private static bool Prefix(out EditorCommandCapture.CommandScope __state)
        {
            __state = null!;
            if (EditorInputBlocker.ShouldBlockEditorAction())
            {
                return false;
            }

            __state = EditorCommandCapture.Begin("command:path.deletePrecedingFloors");
            return true;
        }

        private static void Postfix(EditorCommandCapture.CommandScope __state)
        {
            EditorCommandCapture.End(__state);
        }
    }

    [HarmonyPatch(typeof(scnEditor), "AddEventAtSelected", new[] { typeof(LevelEventType) })]
    internal static class EditorAddEventAtSelectedPatch
    {
        private static bool Prefix(out EditorCommandCapture.CommandScope __state)
        {
            __state = null!;
            if (EditorInputBlocker.ShouldBlockEditorAction())
            {
                return false;
            }

            __state = EditorCommandCapture.Begin("command:event.addAtSelected");
            return true;
        }

        private static void Postfix(EditorCommandCapture.CommandScope __state)
        {
            EditorCommandCapture.End(__state);
        }
    }

    [HarmonyPatch(typeof(scnEditor), "AddDecoration", new[] { typeof(LevelEvent), typeof(int) })]
    internal static class EditorAddDecorationPatch
    {
        private static bool Prefix(out EditorCommandCapture.CommandScope __state)
        {
            __state = null!;
            if (EditorInputBlocker.ShouldBlockEditorAction())
            {
                return false;
            }

            __state = EditorCommandCapture.Begin("command:decoration.add");
            return true;
        }

        private static void Postfix(EditorCommandCapture.CommandScope __state)
        {
            EditorCommandCapture.End(__state);
        }
    }

    [HarmonyPatch(typeof(scnEditor), "AddDecoration", new[] { typeof(LevelEventType), typeof(int) })]
    internal static class EditorAddDecorationTypePatch
    {
        private static bool Prefix(out EditorCommandCapture.CommandScope __state)
        {
            __state = null!;
            if (EditorInputBlocker.ShouldBlockEditorAction())
            {
                return false;
            }

            __state = EditorCommandCapture.Begin("command:decoration.add");
            return true;
        }

        private static void Postfix(EditorCommandCapture.CommandScope __state)
        {
            EditorCommandCapture.End(__state);
        }
    }

    [HarmonyPatch(typeof(scnEditor), "RemoveEvent", new[] { typeof(LevelEvent), typeof(bool) })]
    internal static class EditorRemoveEventPatch
    {
        private static bool Prefix(out EditorCommandCapture.CommandScope __state)
        {
            __state = null!;
            if (EditorInputBlocker.ShouldBlockEditorAction())
            {
                return false;
            }

            __state = EditorCommandCapture.Begin("command:event.remove");
            return true;
        }

        private static void Postfix(EditorCommandCapture.CommandScope __state)
        {
            EditorCommandCapture.End(__state);
        }
    }

    [HarmonyPatch(typeof(scnEditor), "RemoveEvents", new[] { typeof(List<LevelEvent>) })]
    internal static class EditorRemoveEventsPatch
    {
        private static bool Prefix(out EditorCommandCapture.CommandScope __state)
        {
            __state = null!;
            if (EditorInputBlocker.ShouldBlockEditorAction())
            {
                return false;
            }

            __state = EditorCommandCapture.Begin("command:event.removeMany");
            return true;
        }

        private static void Postfix(EditorCommandCapture.CommandScope __state)
        {
            EditorCommandCapture.End(__state);
        }
    }

    [HarmonyPatch(typeof(scnEditor), "DeleteMultiSelectionDecorations")]
    internal static class EditorDeleteMultiSelectionDecorationsPatch
    {
        private static bool Prefix(out EditorCommandCapture.CommandScope __state)
        {
            __state = null!;
            if (EditorInputBlocker.ShouldBlockEditorAction())
            {
                return false;
            }

            __state = EditorCommandCapture.Begin("command:decoration.deleteMultiSelection");
            return true;
        }

        private static void Postfix(EditorCommandCapture.CommandScope __state)
        {
            EditorCommandCapture.End(__state);
        }
    }

    [HarmonyPatch(typeof(scnEditor), "DragDecorationsStart")]
    internal static class EditorDragDecorationsStartPatch
    {
        private static bool Prefix()
        {
            if (EditorInputBlocker.ShouldBlockEditorAction())
            {
                return false;
            }

            EditorCommandCapture.BeginDecorationDrag();
            return true;
        }
    }

    [HarmonyPatch(typeof(PropertyControl_DecorationsList), "OnItemDropMiddle")]
    internal static class DecorationsListDropMiddlePatch
    {
        private static bool Prefix(out EditorCommandCapture.CommandScope __state)
        {
            __state = null!;
            if (EditorInputBlocker.ShouldBlockEditorAction())
            {
                return false;
            }

            __state = EditorCommandCapture.Begin("command:decoration.reorder");
            return true;
        }

        private static void Postfix(EditorCommandCapture.CommandScope __state)
        {
            EditorCommandCapture.End(__state);
        }
    }

    [HarmonyPatch(typeof(PropertyControl_DecorationsList), "OnItemDropSides")]
    internal static class DecorationsListDropSidesPatch
    {
        private static bool Prefix(out EditorCommandCapture.CommandScope __state)
        {
            __state = null!;
            if (EditorInputBlocker.ShouldBlockEditorAction())
            {
                return false;
            }

            __state = EditorCommandCapture.Begin("command:decoration.reorder");
            return true;
        }

        private static void Postfix(EditorCommandCapture.CommandScope __state)
        {
            EditorCommandCapture.End(__state);
        }
    }
}
