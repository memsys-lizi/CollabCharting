using System;
using UnityEngine;

namespace CollabCharting
{
    internal static class EditorSceneNavigator
    {
        public static void EnsureEditorScene()
        {
            if (ADOBase.isLevelEditor)
            {
                return;
            }

            try
            {
                Time.timeScale = 1f;
                AudioListener.pause = false;
                ADOBase.LoadScene("scnEditor");
            }
            catch (Exception ex)
            {
                Main.Mod?.Logger.Error($"Failed to enter editor scene for collab: {ex}");
            }
        }
    }
}
