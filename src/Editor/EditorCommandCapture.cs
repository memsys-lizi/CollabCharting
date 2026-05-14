using UnityEngine;

namespace CollabCharting
{
    internal static class EditorCommandCapture
    {
        private static int commandDepth;
        private static CommandScope? pendingDecorationDrag;

        public static bool IsCapturingCommand => commandDepth > 0 || pendingDecorationDrag != null;

        public static CommandScope Begin(string reason)
        {
            var scope = new CommandScope
            {
                Reason = reason
            };

            if (!CanCapture())
            {
                return scope;
            }

            commandDepth++;
            scope.Participates = true;
            if (commandDepth == 1)
            {
                scope.Active = true;
                scope.BeforeLevelText = EditorStateAdapter.EncodeCurrentLevel();
                OperationCapture.CancelActiveScope();
            }

            return scope;
        }

        public static void End(CommandScope scope)
        {
            if (scope == null)
            {
                return;
            }

            try
            {
                if (scope.Active)
                {
                    Publish(scope.BeforeLevelText, EditorStateAdapter.EncodeCurrentLevel(), scope.Reason);
                }
            }
            finally
            {
                if (scope.Participates && commandDepth > 0)
                {
                    commandDepth--;
                }
            }
        }

        public static void BeginDecorationDrag()
        {
            if (!CanCapture() || pendingDecorationDrag != null)
            {
                return;
            }

            pendingDecorationDrag = new CommandScope
            {
                Active = true,
                Participates = false,
                Reason = "command:decoration.drag",
                BeforeLevelText = EditorStateAdapter.EncodeCurrentLevel()
            };
            OperationCapture.CancelActiveScope();
        }

        public static void Tick()
        {
            if (pendingDecorationDrag == null)
            {
                return;
            }

            if (Input.GetMouseButton(0) || CollabRuntime.IsApplyingRemote || !EditorStateAdapter.IsEditorReady)
            {
                return;
            }

            CommandScope scope = pendingDecorationDrag;
            pendingDecorationDrag = null;
            Publish(scope.BeforeLevelText, EditorStateAdapter.EncodeCurrentLevel(), scope.Reason);
        }

        public static void Reset()
        {
            commandDepth = 0;
            pendingDecorationDrag = null;
        }

        private static bool CanCapture()
        {
            return CollabRuntime.Session.InLobby &&
                   !CollabRuntime.IsApplyingRemote &&
                   !EditorPlaybackState.IsPreviewPlaying &&
                   EditorStateAdapter.IsEditorReady;
        }

        private static void Publish(string before, string after, string reason)
        {
            if (string.IsNullOrWhiteSpace(before) ||
                string.IsNullOrWhiteSpace(after) ||
                EditorStateAdapter.HashLevelText(before) == EditorStateAdapter.HashLevelText(after))
            {
                return;
            }

            if (!OperationDiffUtility.TryCreateBatch(before, after, reason, out CollabOperationBatch batch))
            {
                Main.Mod?.Logger.Warning($"Editor command changed level but produced no collab operation: {reason}");
                OperationCapture.ResetBaseline();
                return;
            }

            batch.SelectedFloor = EditorStateAdapter.GetSelectedFloorId();
            OperationCapture.ResetBaseline();
            Main.Mod?.Logger.Log($"Captured editor command operation: {reason} -> {OperationDiffUtility.DescribeBatch(batch)}");
            CollabRuntime.Session.PublishLocalOperationBatch(batch);
        }

        internal sealed class CommandScope
        {
            public bool Active { get; set; }

            public bool Participates { get; set; }

            public string Reason { get; set; } = string.Empty;

            public string BeforeLevelText { get; set; } = string.Empty;
        }
    }
}
