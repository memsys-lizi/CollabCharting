using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ADOFAI;
using HarmonyLib;

namespace CollabCharting
{
    internal static class EditorStateAdapter
    {
        public static bool IsEditorReady =>
            ADOBase.editor != null &&
            ADOBase.customLevel != null &&
            ADOBase.customLevel.levelData != null &&
            !string.IsNullOrEmpty(ADOBase.levelPath);

        public static string CurrentLevelPath => IsEditorReady ? ADOBase.levelPath : string.Empty;

        public static string CurrentLevelName =>
            string.IsNullOrEmpty(CurrentLevelPath) ? "未打开关卡" : Path.GetFileNameWithoutExtension(CurrentLevelPath);

        public static string EncodeCurrentLevel()
        {
            if (!IsEditorReady)
            {
                return string.Empty;
            }

            return ADOBase.customLevel.levelData.Encode();
        }

        public static int GetSelectedFloorId()
        {
            if (ADOBase.editor == null ||
                ADOBase.editor.selectedFloors == null ||
                ADOBase.editor.selectedFloors.Count == 0 ||
                ADOBase.editor.selectedFloors[0] == null)
            {
                return -1;
            }

            return ADOBase.editor.selectedFloors[0].seqID;
        }

        public static string HashLevelText(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
        }

        public static void ApplySnapshot(string levelText, string reason)
        {
            ApplyLevelText(levelText, reason, preserveEntityIds: false);
        }

        public static void ApplyLevelText(string levelText, string reason, bool preserveEntityIds = false)
        {
            ApplyLevelText(levelText, reason, preserveEntityIds, -1);
        }

        public static void ApplyLevelText(string levelText, string reason, bool preserveEntityIds, int selectedFloor)
        {
            if (!IsEditorReady || string.IsNullOrWhiteSpace(levelText))
            {
                return;
            }

            try
            {
                if (!(GDMiniJSON.Json.Deserialize(levelText) is Dictionary<string, object> dict))
                {
                    throw new InvalidDataException("Level snapshot is not a valid ADOFAI object.");
                }

                LevelData levelData = new LevelData();
                levelData.Setup();
                levelData.Decode(dict, out LoadResult status);
                if (status != LoadResult.Successful)
                {
                    throw new InvalidDataException($"Level snapshot decode failed: {status}");
                }

                CollabRuntime.IsApplyingRemote = true;
                ADOBase.customLevel.levelData = levelData;
                ADOBase.editor.RemakePath();
                ADOBase.editor.ApplyEventsToFloors();
                RefreshSelectedFloorIndicators();
                ADOBase.customLevel.ReloadAssets(force: true, reloadDecorations: false);
                ADOBase.editor.UpdateDecorationObjects();
                RestoreSelectedFloor(selectedFloor);
                MarkUnsaved();
                ADOBase.editor.ShowNotification($"协作同步：{reason}");
                if (!preserveEntityIds)
                {
                    EntityIdRegistry.InitializeFromLevelText(levelText);
                }
            }
            finally
            {
                CollabRuntime.IsApplyingRemote = false;
                OperationCapture.ResetBaseline();
            }
        }

        private static void RefreshSelectedFloorIndicators()
        {
            try
            {
                if (ADOBase.editor == null ||
                    ADOBase.editor.selectedFloors == null ||
                    ADOBase.editor.selectedFloors.Count == 0 ||
                    ADOBase.editor.selectedFloors[0] == null)
                {
                    return;
                }

                ADOBase.editor.ShowEventIndicators(ADOBase.editor.selectedFloors[0]);
            }
            catch (Exception ex)
            {
                Main.Mod?.Logger.Warning($"Failed to refresh event indicators after collab apply: {ex.Message}");
            }
        }

        private static void RestoreSelectedFloor(int selectedFloor)
        {
            if (selectedFloor < 0 || ADOBase.editor == null || ADOBase.editor.floors == null)
            {
                return;
            }

            int floor = Math.Min(selectedFloor, ADOBase.editor.floors.Count - 1);
            if (floor < 0)
            {
                return;
            }

            try
            {
                ADOBase.editor.SelectFloor(ADOBase.editor.floors[floor]);
            }
            catch (Exception ex)
            {
                Main.Mod?.Logger.Warning($"Failed to restore selected floor after collab apply: {ex.Message}");
            }
        }

        public static void LoadLevelFromCache(string levelPath)
        {
            if (ADOBase.editor == null || string.IsNullOrWhiteSpace(levelPath) || !File.Exists(levelPath))
            {
                return;
            }

            CollabRuntime.IsApplyingRemote = true;
            try
            {
                ADOBase.editor.OpenLevel(levelPath);
                EntityIdRegistry.InitializeFromLevelText(File.ReadAllText(levelPath));
            }
            finally
            {
                CollabRuntime.IsApplyingRemote = false;
                OperationCapture.ResetBaseline();
            }
        }

        private static void MarkUnsaved()
        {
            try
            {
                AccessTools.PropertySetter(typeof(scnEditor), "unsavedChanges")
                    ?.Invoke(ADOBase.editor, new object[] { true });
            }
            catch (Exception ex)
            {
                Main.Mod?.Logger.Warning($"Failed to mark editor as unsaved: {ex.Message}");
            }
        }
    }
}
