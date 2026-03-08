using UnityEngine;

namespace ShrinkEventBus
{
    public static class EventAutoRegHelper
    {
        private static readonly object InitLock = new();
        public static bool IsInitialized { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InitializeAfterSceneLoad()
        {
            EnsureInitialized();
        }

        public static void EnsureInitialized()
        {
            if (IsInitialized) return;
            lock (InitLock)
            {
                if (IsInitialized) return;
                IsInitialized = true;
            }
        }

        public static void Cleanup()
        {
            lock (InitLock)
            {
                IsInitialized = false;
            }

            EventBus.UnregisterAllEvents();
        }
    }
}