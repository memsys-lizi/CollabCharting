using System;
using System.Collections.Generic;

namespace CollabCharting
{
    internal static class MainThreadDispatcher
    {
        private static readonly Queue<Action> Queue = new Queue<Action>();
        private static readonly object Gate = new object();

        public static void Enqueue(Action action)
        {
            if (action == null)
            {
                return;
            }

            lock (Gate)
            {
                Queue.Enqueue(action);
            }
        }

        public static void Pump()
        {
            while (true)
            {
                Action? action;
                lock (Gate)
                {
                    if (Queue.Count == 0)
                    {
                        return;
                    }

                    action = Queue.Dequeue();
                }

                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Main.Mod?.Logger.Error($"Collab main-thread action failed: {ex}");
                }
            }
        }
    }
}
