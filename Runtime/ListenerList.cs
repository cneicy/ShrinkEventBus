#nullable enable
using System;
using System.Reflection;

namespace ShrinkEventBus
{
    public class ListenerList
    {
        private readonly object _lock = new();
        private EventHandlerInfo[] _handlers = Array.Empty<EventHandlerInfo>();

        public void Add(Delegate handler, EventPriority priority, int numericPriority,
            bool receiveCanceled, string debugInfo = "", MethodInfo? originalMethod = null)
        {
            lock (_lock)
            {
                var newInfo = new EventHandlerInfo(handler, priority, numericPriority,
                    receiveCanceled, debugInfo, originalMethod);
                var newArray = new EventHandlerInfo[_handlers.Length + 1];
                Array.Copy(_handlers, newArray, _handlers.Length);
                newArray[^1] = newInfo;
                Array.Sort(newArray, CompareHandlers);
                _handlers = newArray;
            }
        }

        public bool Remove(Delegate handler)
        {
            lock (_lock)
            {
                var index = -1;
                for (var i = 0; i < _handlers.Length; i++)
                {
                    if (_handlers[i].Handler.Equals(handler))
                    {
                        index = i;
                        break;
                    }
                }

                if (index == -1) return false;

                var newArray = new EventHandlerInfo[_handlers.Length - 1];
                if (index > 0) Array.Copy(_handlers, 0, newArray, 0, index);
                if (index < _handlers.Length - 1)
                    Array.Copy(_handlers, index + 1, newArray, index, _handlers.Length - index - 1);
                _handlers = newArray;
                return true;
            }
        }

        public void RemoveTarget(object target)
        {
            lock (_lock)
            {
                var removeCount = 0;
                for (var i = 0; i < _handlers.Length; i++)
                {
                    if (_handlers[i].Target == target) removeCount++;
                }

                if (removeCount == 0) return;

                var newArray = new EventHandlerInfo[_handlers.Length - removeCount];
                var dst = 0;
                for (var i = 0; i < _handlers.Length; i++)
                {
                    if (_handlers[i].Target != target)
                    {
                        newArray[dst++] = _handlers[i];
                    }
                }

                _handlers = newArray;
            }
        }

        public EventHandlerInfo[] GetHandlers() => _handlers;

        public int Count => _handlers.Length;

        public void Clear()
        {
            lock (_lock)
            {
                _handlers = Array.Empty<EventHandlerInfo>();
            }
        }

        private static int CompareHandlers(EventHandlerInfo a, EventHandlerInfo b)
        {
            var priorityCompare = a.Priority.CompareTo(b.Priority);
            return priorityCompare != 0 ? priorityCompare : b.NumericPriority.CompareTo(a.NumericPriority);
        }
    }
}