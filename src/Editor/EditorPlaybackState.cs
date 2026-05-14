namespace CollabCharting
{
    internal static class EditorPlaybackState
    {
        public static bool IsPreviewPlaying
        {
            get
            {
                try
                {
                    return ADOBase.editor != null &&
                           ADOBase.controller != null &&
                           ADOBase.isLevelEditor &&
                           !ADOBase.controller.paused;
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}
