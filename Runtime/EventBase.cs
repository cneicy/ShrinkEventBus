#nullable enable
using System;
using System.Reflection;

namespace ShrinkEventBus
{
    public abstract class EventBase : IDisposable
    {
        private readonly ListenerList _listenerList = new();
        private bool _isCanceled;
        private EventResult _result = EventResult.DEFAULT;

        public EventHandlerInfo? CurrentHandler { get; internal set; }
        public DateTime EventTime { get; private set; } = DateTime.UtcNow;
        public Guid EventId { get; private set; } = Guid.NewGuid();

        public bool IsCancelable { get; }
        public bool HasResult { get; }
        public EventPriority? Phase { get; private set; }

        internal bool IsInPool { get; set; }
        internal Action<EventBase> ReleaseAction { get; set; }

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

        public bool IsCanceled
        {
            get => _isCanceled;
            set
            {
                if (!IsCancelable) throw new UnsupportedOperationException();
                _isCanceled = value;
            }
        }

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