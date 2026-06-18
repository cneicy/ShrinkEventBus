#nullable enable
using System;
using System.Collections.Generic;

namespace ShrinkEventBus
{
    public class ListenerList
    {
        private static readonly EventPriority[] Priorities = (EventPriority[])Enum.GetValues(typeof(EventPriority));

        private readonly object _lock;
        private readonly List<EventHandlerInfo>[] _priorityBuckets;
        private readonly ListenerList? _parent;
        private List<ListenerList>? _children;
        private EventHandlerInfo[] _snapshot = Array.Empty<EventHandlerInfo>();
        private readonly EventHandlerInfo[]?[] _phaseSnapshots = new EventHandlerInfo[Priorities.Length][];
        private bool _dirty;

        public ListenerList(ListenerList? parent = null) : this(parent, null)
        {
        }

        // 同一条父子链必须共用一把锁，否则脏标记传播与快照重建会产生竞态
        internal ListenerList(ListenerList? parent, object? sharedLock)
        {
            _lock = sharedLock ?? parent?._lock ?? new object();
            _priorityBuckets = new List<EventHandlerInfo>[Priorities.Length];
            for (var i = 0; i < _priorityBuckets.Length; i++)
                _priorityBuckets[i] = new List<EventHandlerInfo>();

            _parent = parent;
            _parent?.AddChild(this);
            _dirty = true;
        }

        public int LocalCount
        {
            get
            {
                lock (_lock)
                {
                    var count = 0;
                    foreach (var list in _priorityBuckets)
                        count += list.Count;
                    return count;
                }
            }
        }

        public int Count => GetHandlers().Length;

        public void Add(EventHandlerInfo info)
        {
            if (info == null)
                throw new ArgumentNullException(nameof(info));

            lock (_lock)
            {
                var list = _priorityBuckets[(int)info.Priority];
                var index = BinarySearchInsertIndex(list, info.NumericPriority);
                list.Insert(index, info);
                MarkDirty();
            }
        }

        public bool Remove(Delegate handler)
        {
            if (handler == null)
                return false;

            lock (_lock)
            {
                foreach (var list in _priorityBuckets)
                {
                    for (var i = 0; i < list.Count; i++)
                    {
                        if (!list[i].Handler.Equals(handler))
                            continue;

                        list.RemoveAt(i);
                        MarkDirty();
                        return true;
                    }
                }

                return false;
            }
        }

        public int RemoveWhere(Predicate<EventHandlerInfo> predicate)
        {
            if (predicate == null)
                return 0;

            lock (_lock)
            {
                var removedCount = 0;
                foreach (var list in _priorityBuckets)
                {
                    for (var i = list.Count - 1; i >= 0; i--)
                    {
                        if (!predicate(list[i]))
                            continue;

                        list.RemoveAt(i);
                        removedCount++;
                    }
                }

                if (removedCount > 0)
                    MarkDirty();

                return removedCount;
            }
        }

        public void RemoveTarget(object target)
        {
            if (target == null)
                return;

            lock (_lock)
            {
                var removed = false;
                foreach (var list in _priorityBuckets)
                {
                    for (var i = list.Count - 1; i >= 0; i--)
                    {
                        if (!ReferenceEquals(list[i].Target, target))
                            continue;

                        list.RemoveAt(i);
                        removed = true;
                    }
                }

                if (removed)
                    MarkDirty();
            }
        }

        public EventHandlerInfo[] GetHandlers()
        {
            lock (_lock)
            {
                if (_dirty)
                    RebuildSnapshot();

                return _snapshot;
            }
        }

        public EventHandlerInfo[] GetHandlers(EventPriority priority)
        {
            lock (_lock)
            {
                if (_dirty)
                    RebuildSnapshot();

                return _phaseSnapshots[(int)priority] ?? Array.Empty<EventHandlerInfo>();
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                foreach (var list in _priorityBuckets)
                    list.Clear();
                MarkDirty();
            }
        }

        private void AddChild(ListenerList child)
        {
            if (child == null)
                return;

            lock (_lock)
            {
                _children ??= new List<ListenerList>(2);
                _children.Add(child);
            }
        }

        private void MarkDirty()
        {
            _dirty = true;
            _snapshot = Array.Empty<EventHandlerInfo>();
            Array.Clear(_phaseSnapshots, 0, _phaseSnapshots.Length);

            if (_children == null)
                return;

            for (var i = 0; i < _children.Count; i++)
                _children[i].MarkDirty();
        }

        private void RebuildSnapshot()
        {
            var merged = new List<EventHandlerInfo>();

            for (var i = 0; i < Priorities.Length; i++)
            {
                var parentHandlers = _parent != null
                    ? _parent.GetHandlers(Priorities[i])
                    : Array.Empty<EventHandlerInfo>();

                var phaseSnapshot = MergeByNumericPriority(_priorityBuckets[i], parentHandlers);
                _phaseSnapshots[i] = phaseSnapshot;
                merged.AddRange(phaseSnapshot);
            }

            _snapshot = merged.ToArray();
            _dirty = false;
        }

        // 两侧均已按 NumericPriority 降序排列；平局时本类型 handler 在前
        private static EventHandlerInfo[] MergeByNumericPriority(List<EventHandlerInfo> own,
            EventHandlerInfo[] parentHandlers)
        {
            if (parentHandlers.Length == 0)
                return own.ToArray();
            if (own.Count == 0)
                return parentHandlers;

            var result = new EventHandlerInfo[own.Count + parentHandlers.Length];
            int i = 0, j = 0, k = 0;
            while (i < own.Count && j < parentHandlers.Length)
            {
                result[k++] = parentHandlers[j].NumericPriority > own[i].NumericPriority
                    ? parentHandlers[j++]
                    : own[i++];
            }

            while (i < own.Count)
                result[k++] = own[i++];
            while (j < parentHandlers.Length)
                result[k++] = parentHandlers[j++];

            return result;
        }

        private static int BinarySearchInsertIndex(List<EventHandlerInfo> list, int numericPriority)
        {
            var lo = 0;
            var hi = list.Count;

            while (lo < hi)
            {
                var mid = (lo + hi) >> 1;
                var cmp = numericPriority.CompareTo(list[mid].NumericPriority);
                if (cmp > 0)
                    hi = mid;
                else
                    lo = mid + 1;
            }

            return lo;
        }
    }
}
