namespace CollabCharting
{
    internal static class EditorInputBlocker
    {
        public static bool ShouldBlockEditorAction()
        {
            if (!CollabRuntime.Session.IsBlockingUserInput)
            {
                return false;
            }

            ADOBase.editor?.ShowNotification("协作同步初始化中，请稍候");
            return true;
        }
    }
}
