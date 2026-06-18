using System;
using System.Collections.Generic;

namespace ShrinkEventBus
{
    public static class EventPool<T> where T : EventBase, new()
    {
        private const int MaxPoolSize = 128;

        private static readonly Stack<T> Pool = new(32);
        private static readonly object Lock = new();
        private static readonly Action<EventBase> CachedReleaseAction = e => Release((T)e);

        public static T Get()
        {
            lock (Lock)
            {
                if (Pool.Count > 0)
                {
                    var evt = Pool.Pop();
                    evt.IsInPool = false;
                    return evt;
                }
            }

            var newEvt = new T();
            newEvt.ReleaseAction = CachedReleaseAction;
            return newEvt;
        }

        public static void Release(T evt)
        {
            if (evt == null)
                return;

            lock (Lock)
            {
                if (evt.IsInPool || Pool.Count >= MaxPoolSize)
                    return;

                evt.ResetInternal();
                evt.IsInPool = true;
                Pool.Push(evt);
            }
        }
    }
}
