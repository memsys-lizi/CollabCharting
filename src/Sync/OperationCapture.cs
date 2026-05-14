using System;

namespace CollabCharting
{
    internal static class OperationCapture
    {
        private static string baselineHash = string.Empty;
        private static string baselineText = string.Empty;
        private static string observedHash = string.Empty;
        private static string observedText = string.Empty;
        private static float dirtySeconds;

        public static void ResetBaseline()
        {
            if (!EditorStateAdapter.IsEditorReady)
            {
                baselineHash = string.Empty;
                baselineText = string.Empty;
                observedHash = string.Empty;
                observedText = string.Empty;
                dirtySeconds = 0f;
                return;
            }

            observedText = EditorStateAdapter.EncodeCurrentLevel();
            baselineText = observedText;
            baselineHash = EditorStateAdapter.HashLevelText(observedText);
            observedHash = baselineHash;
            dirtySeconds = 0f;
        }

        public static void Update(float dt)
        {
            if (!CollabRuntime.Session.InLobby ||
                CollabRuntime.IsApplyingRemote ||
                EditorPlaybackState.IsPreviewPlaying ||
                !EditorStateAdapter.IsEditorReady)
            {
                return;
            }

            string levelText = EditorStateAdapter.EncodeCurrentLevel();
            string hash = EditorStateAdapter.HashLevelText(levelText);
            if (string.IsNullOrEmpty(baselineHash))
            {
                baselineHash = hash;
                baselineText = levelText;
                observedHash = hash;
                observedText = levelText;
                return;
            }

            if (hash == baselineHash)
            {
                dirtySeconds = 0f;
                observedHash = hash;
                observedText = levelText;
                return;
            }

            if (hash != observedHash)
            {
                observedHash = hash;
                observedText = levelText;
                dirtySeconds = 0f;
                return;
            }

            dirtySeconds += dt;
            if (dirtySeconds < Math.Max(0.1f, Main.Settings.SnapshotDebounceSeconds))
            {
                return;
            }

            CollabRuntime.Session.PublishLocalSnapshot(observedText, baselineText, "local-edit");
            baselineText = observedText;
            baselineHash = observedHash;
            dirtySeconds = 0f;
        }
    }
}
