using System;
using System.Collections.Generic;

namespace ShrinkEventBus
{
    public static class EventPool<T> where T : EventBase, new()
    {
        private static readonly Stack<T> Pool = new(32);
        private static readonly object Lock = new();

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
            newEvt.ReleaseAction = e => Release((T)e);
            return newEvt;
        }

        public static void Release(T evt)
        {
            if (evt == null || evt.IsInPool) return;

            evt.ResetInternal();
            evt.IsInPool = true;

            lock (Lock)
            {
                Pool.Push(evt);
            }
        }
    }
}