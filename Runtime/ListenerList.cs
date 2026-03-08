#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;

namespace ShrinkEventBus
{
    public class ListenerList
    {
        private readonly object _lock = new();
        private readonly List<EventHandlerInfo> _list = new();
        private EventHandlerInfo[] _snapshot = Array.Empty<EventHandlerInfo>();
        private bool _dirty;

        public void Add(Delegate handler, EventPriority priority, int numericPriority,
            bool receiveCanceled, string debugInfo = "", MethodInfo? originalMethod = null)
        {
            var newInfo = new EventHandlerInfo(handler, priority, numericPriority,
                receiveCanceled, debugInfo, originalMethod);
            AddInfo(newInfo);
        }

        public void Add(EventHandlerInfo info)
        {
            AddInfo(info);
        }

        private void AddInfo(EventHandlerInfo info)
        {
            lock (_lock)
            {
                var index = BinarySearchInsertIndex(info.Priority, info.NumericPriority);
                _list.Insert(index, info);
                _dirty = true;
            }
        }

        public bool Remove(Delegate handler)
        {
            lock (_lock)
            {
                for (var i = 0; i < _list.Count; i++)
                {
                    if (_list[i].Handler.Equals(handler))
                    {
                        _list.RemoveAt(i);
                        _dirty = true;
                        return true;
                    }
                }
                return false;
            }
        }

        public void RemoveTarget(object target)
        {
            lock (_lock)
            {
                var removed = false;
                for (var i = _list.Count - 1; i >= 0; i--)
                {
                    if (_list[i].Target == target)
                    {
                        _list.RemoveAt(i);
                        removed = true;
                    }
                }
                if (removed) _dirty = true;
            }
        }

        public EventHandlerInfo[] GetHandlers()
        {
            lock (_lock)
            {
                if (_dirty)
                {
                    _snapshot = _list.ToArray();
                    _dirty = false;
                }
                return _snapshot;
            }
        }

        public int Count
        {
            get { lock (_lock) return _list.Count; }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _list.Clear();
                _snapshot = Array.Empty<EventHandlerInfo>();
                _dirty = false;
            }
        }

        private int BinarySearchInsertIndex(EventPriority priority, int numericPriority)
        {
            var lo = 0;
            var hi = _list.Count;

            while (lo < hi)
            {
                var mid = (lo + hi) >> 1;
                var existing = _list[mid];
                var cmp = existing.Priority.CompareTo(priority);

                if (cmp == 0)
                    cmp = numericPriority.CompareTo(existing.NumericPriority);

                if (cmp < 0)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            return lo;
        }
    }
}