using ADOFAI;

namespace CollabCharting
{
    internal static class OperationCapture
    {
        private static SaveStateScope? rootScope;
        private static string rootBefore = string.Empty;
        private static string baseline = string.Empty;
        private static float pollTimer;
        private static int depth;

        public static void ResetBaseline()
        {
            rootScope = null;
            rootBefore = string.Empty;
            baseline = string.Empty;
            pollTimer = 0f;
            depth = 0;
        }

        public static void Update(float dt)
        {
            if (!CollabRuntime.Session.InLobby ||
                CollabRuntime.IsApplyingRemote ||
                !EditorStateAdapter.IsEditorReady)
            {
                baseline = string.Empty;
                pollTimer = 0f;
                return;
            }

            if (EditorPlaybackState.IsPreviewPlaying || depth > 0 || rootScope != null)
            {
                return;
            }

            string current = EditorStateAdapter.EncodeCurrentLevel();
            if (string.IsNullOrWhiteSpace(baseline))
            {
                baseline = current;
                return;
            }

            pollTimer += dt;
            if (pollTimer < 0.35f)
            {
                return;
            }

            pollTimer = 0f;
            if (EditorStateAdapter.HashLevelText(baseline) == EditorStateAdapter.HashLevelText(current))
            {
                return;
            }

            PublishDiff(baseline, current, "local-edit-poll");
        }

        public static void Begin(SaveStateScope scope, bool dataHasChanged)
        {
            if (!dataHasChanged ||
                scope == null ||
                depth < 0 ||
                !CollabRuntime.Session.InLobby ||
                CollabRuntime.IsApplyingRemote ||
                EditorPlaybackState.IsPreviewPlaying ||
                !EditorStateAdapter.IsEditorReady)
            {
                return;
            }

            if (depth == 0)
            {
                rootScope = scope;
                rootBefore = EditorStateAdapter.EncodeCurrentLevel();
            }

            depth++;
        }

        public static void End(SaveStateScope scope)
        {
            if (depth <= 0 || scope == null)
            {
                return;
            }

            depth--;
            if (scope != rootScope || depth != 0)
            {
                return;
            }

            string before = rootBefore;
            rootScope = null;
            rootBefore = string.Empty;

            if (!CollabRuntime.Session.InLobby ||
                CollabRuntime.IsApplyingRemote ||
                EditorPlaybackState.IsPreviewPlaying ||
                !EditorStateAdapter.IsEditorReady)
            {
                return;
            }

            string after = EditorStateAdapter.EncodeCurrentLevel();
            if (EditorStateAdapter.HashLevelText(before) == EditorStateAdapter.HashLevelText(after))
            {
                return;
            }

            PublishDiff(before, after, "local-edit");
        }

        private static void PublishDiff(string before, string after, string reason)
        {
            baseline = after;
            if (!OperationDiffUtility.TryCreateBatch(before, after, reason, out CollabOperationBatch batch))
            {
                Main.Mod?.Logger.Warning("Collab operation capture saw level changes but could not classify them.");
                return;
            }

            batch.SelectedFloor = EditorStateAdapter.GetSelectedFloorId();
            Main.Mod?.Logger.Log($"Captured collab operation batch: {OperationDiffUtility.DescribeBatch(batch)}");
            CollabRuntime.Session.PublishLocalOperationBatch(batch);
        }
    }
}
