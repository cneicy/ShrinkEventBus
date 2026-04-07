#nullable enable
using System;
using System.Reflection;
using System.Runtime.Serialization;

namespace ShrinkEventBus
{
    public abstract class EventBase : IDisposable
    {
        private readonly ListenerList _listenerList = new();
        private bool _isCanceled;
        private EventResult _result = EventResult.DEFAULT;

        [IgnoreDataMember]
        public EventHandlerInfo? CurrentHandler { get; internal set; }

        [IgnoreDataMember]
        public DateTime EventTime { get; private set; } = DateTime.UtcNow;

        [IgnoreDataMember]
        public Guid EventId { get; private set; } = Guid.NewGuid();

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
            IsCancelable = GetType().GetCustomAttribute<CancelableAttribute>() != null;
            HasResult = GetType().GetCustomAttribute<HasResultAttribute>() != null;
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
            EventId = Guid.NewGuid();
            _listenerList.Clear();
            OnReset();
        }

        internal void PrepareForDispatch()
        {
            CurrentHandler = null;
            Phase = null;
            EventTime = DateTime.UtcNow;
            EventId = Guid.NewGuid();
            _listenerList.Clear();
        }

        [IgnoreDataMember]
        public bool IsCanceled
        {
            get => _isCanceled;
            set
            {
                if (!IsCancelable) throw new UnsupportedOperationException();
                _isCanceled = value;
            }
        }

        [IgnoreDataMember]
        public EventResult Result
        {
            get => _result;
            set
            {
                if (!HasResult) throw new InvalidOperationException();
                _result = value;
            }
        }

        internal void SetPhase(EventPriority value)
        {
            if (Phase == value) return;
            if (Phase != null && Phase.Value.CompareTo(value) > 0)
                throw new ArgumentException();
            Phase = value;
        }

        public void SetCanceled(bool canceled) => IsCanceled = canceled;
        public void SetResult(EventResult result) => Result = result;
        public ListenerList GetListenerList() => _listenerList;
        public EventHandlerInfo[] GetSubscribers() => _listenerList.GetHandlers();

        public void Dispose()
        {
            ReleaseAction?.Invoke(this);
        }
    }
}
