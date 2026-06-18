#nullable enable
using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Serialization;

namespace ShrinkEventBus
{
    public abstract class EventBase : IDisposable
    {
        private sealed class EventMetadata
        {
            public bool IsCancelable;
            public bool HasResult;
        }

        private static readonly ConcurrentDictionary<Type, EventMetadata> MetadataCache = new();

        private EventHandlerInfo[]? _dispatchSnapshot;
        private Guid? _eventId;
        private bool _isCanceled;
        private EventResult _result = EventResult.DEFAULT;

        [IgnoreDataMember]
        public EventHandlerInfo? CurrentHandler { get; internal set; }

        [IgnoreDataMember]
        public DateTime EventTime { get; private set; } = DateTime.UtcNow;

        [IgnoreDataMember]
        public Guid EventId => _eventId ??= Guid.NewGuid();

        [IgnoreDataMember]
        public bool IsCancelable { get; }

        [IgnoreDataMember]
        public bool HasResult { get; }

        [IgnoreDataMember]
        public EventPriority? Phase { get; private set; }

        internal bool IsInPool { get; set; }
        internal Action<EventBase>? ReleaseAction { get; set; }

        protected EventBase()
        {
            var metadata = GetOrCreateMetadata(GetType());
            IsCancelable = metadata.IsCancelable;
            HasResult = metadata.HasResult;
            Setup();
        }

        protected virtual void Setup() { }

        protected virtual void OnReset() { }

        internal void ResetInternal()
        {
            _isCanceled = false;
            _result = EventResult.DEFAULT;
            CurrentHandler = null;
            Phase = null;
            EventTime = DateTime.UtcNow;
            _eventId = null;
            _dispatchSnapshot = null;
            OnReset();
        }

        internal void PrepareForDispatch()
        {
            CurrentHandler = null;
            Phase = null;
            EventTime = DateTime.UtcNow;
            _eventId = null;
            _dispatchSnapshot = null;
        }

        internal void SetListenerSnapshot(EventHandlerInfo[]? handlers)
        {
            _dispatchSnapshot = handlers is { Length: > 0 } ? handlers : null;
        }

        [IgnoreDataMember]
        public bool IsCanceled
        {
            get => _isCanceled;
            set
            {
                if (!IsCancelable)
                    throw new UnsupportedOperationException(
                        $"Event {GetType().Name} is not cancelable. Mark it with [Cancelable] to allow cancellation.");
                _isCanceled = value;
            }
        }

        [IgnoreDataMember]
        public EventResult Result
        {
            get => _result;
            set
            {
                if (!HasResult)
                    throw new InvalidOperationException(
                        $"Event {GetType().Name} does not support results. Mark it with [HasResult] to allow setting a result.");
                _result = value;
            }
        }

        internal void SetPhase(EventPriority value)
        {
            if (Phase == value) return;
            if (Phase != null && Phase.Value.CompareTo(value) > 0)
                throw new ArgumentException(
                    $"Event phase cannot move backwards from {Phase.Value} to {value}.", nameof(value));
            Phase = value;
        }

        public void SetCanceled(bool canceled) => IsCanceled = canceled;
        public void SetResult(EventResult result) => Result = result;

        public EventHandlerInfo[] GetSubscribers()
        {
            var snapshot = _dispatchSnapshot;
            if (snapshot == null || snapshot.Length == 0)
                return Array.Empty<EventHandlerInfo>();

            var copy = new EventHandlerInfo[snapshot.Length];
            Array.Copy(snapshot, copy, snapshot.Length);
            return copy;
        }

        public void Dispose()
        {
            ReleaseAction?.Invoke(this);
        }

        private static EventMetadata GetOrCreateMetadata(Type eventType)
        {
            return MetadataCache.GetOrAdd(eventType, static type => new EventMetadata
            {
                IsCancelable = type.GetCustomAttribute<CancelableAttribute>() != null,
                HasResult = type.GetCustomAttribute<HasResultAttribute>() != null
            });
        }
    }
}
