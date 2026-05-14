using ADOFAI;

namespace CollabCharting
{
    internal static class OperationCapture
    {
        private static SaveStateScope? rootScope;
        private static string rootBefore = string.Empty;
        private static int depth;

        public static void ResetBaseline()
        {
            rootScope = null;
            rootBefore = string.Empty;
            depth = 0;
        }

        public static void Update(float dt)
        {
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

            if (!OperationDiffUtility.TryCreateBatch(before, after, "local-edit", out CollabOperationBatch batch))
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
