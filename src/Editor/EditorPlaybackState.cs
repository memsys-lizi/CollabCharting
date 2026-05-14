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
                           ADOBase.isLevelEditor &&
                           ADOBase.editor.playMode;
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}
