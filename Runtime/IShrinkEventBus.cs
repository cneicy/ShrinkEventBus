using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;

namespace ShrinkEventBus
{
    public interface IShrinkEventSubscription : IDisposable
    {
        long SubscriptionId { get; }
        bool IsDisposed { get; }
    }

    public readonly struct ShrinkEventSubscriptionSnapshot
    {
        public ShrinkEventSubscriptionSnapshot(long subscriptionId, Type eventType, EventPriority priority,
            int numericPriority, bool receiveCanceled, object target, string targetTypeName, string methodName,
            string debugInfo, DateTime registeredAtUtc)
        {
            SubscriptionId = subscriptionId;
            EventType = eventType ?? throw new ArgumentNullException(nameof(eventType));
            Priority = priority;
            NumericPriority = numericPriority;
            ReceiveCanceled = receiveCanceled;
            Target = target;
            TargetTypeName = targetTypeName ?? string.Empty;
            MethodName = methodName ?? string.Empty;
            DebugInfo = debugInfo ?? string.Empty;
            RegisteredAtUtc = registeredAtUtc;
        }

        public long SubscriptionId { get; }
        public Type EventType { get; }
        public EventPriority Priority { get; }
        public int NumericPriority { get; }
        public bool ReceiveCanceled { get; }
        public object Target { get; }
        public string TargetTypeName { get; }
        public string MethodName { get; }
        public string DebugInfo { get; }
        public DateTime RegisteredAtUtc { get; }
        public bool IsStaticHandler => Target == null;
    }

    public interface IShrinkEventBus
    {
        event Action<EventBase, Type> OnEventTriggered;

#if UNITY_EDITOR
        event Action<EventBase, string, string, EventHandlerInfo[]> OnEventTriggeredForEditor;
        bool EnableDebugRecord { get; set; }
#endif

        bool IsStarted { get; }

        void Start();
        void AutoRegister(object target);
        void Register(object target);
        void Unregister(object target);

        void RegisterEvent<TEvent>(Action<TEvent> handler, EventPriority priority = EventPriority.NORMAL,
            bool receiveCanceled = false) where TEvent : EventBase;
        void RegisterEvent<TEvent>(Action<TEvent> handler, int priority) where TEvent : EventBase;
        void RegisterEvent<TEvent>(Func<TEvent, UniTask> handler, EventPriority priority = EventPriority.NORMAL,
            bool receiveCanceled = false) where TEvent : EventBase;
        void RegisterEvent<TEvent>(Func<TEvent, UniTask> handler, int priority) where TEvent : EventBase;
        IShrinkEventSubscription SubscribeEvent<TEvent>(Action<TEvent> handler,
            EventPriority priority = EventPriority.NORMAL, bool receiveCanceled = false) where TEvent : EventBase;
        IShrinkEventSubscription SubscribeEvent<TEvent>(Action<TEvent> handler, int priority)
            where TEvent : EventBase;
        IShrinkEventSubscription SubscribeEvent<TEvent>(Func<TEvent, UniTask> handler,
            EventPriority priority = EventPriority.NORMAL, bool receiveCanceled = false) where TEvent : EventBase;
        IShrinkEventSubscription SubscribeEvent<TEvent>(Func<TEvent, UniTask> handler, int priority)
            where TEvent : EventBase;

        void UnregisterEvent<TEvent>(Action<TEvent> handler) where TEvent : EventBase;
        void UnregisterEvent<TEvent>(Func<TEvent, UniTask> handler) where TEvent : EventBase;
        void ClearAllSubscribersForEvent<TEvent>() where TEvent : EventBase;
        void UnregisterAllEventsForObject(object targetObject);
        void UnregisterAllEvents();

        bool TriggerEvent<TEvent>(TEvent eventArgs) where TEvent : EventBase;
        bool TriggerEvent<TEvent>(EventPriority phase, TEvent eventArgs) where TEvent : EventBase;
        UniTask<bool> TriggerEventAsync<TEvent>(TEvent eventArgs) where TEvent : EventBase;
        UniTask<bool> TriggerEventAsync<TEvent>(EventPriority phase, TEvent eventArgs) where TEvent : EventBase;

        EventHandlerInfo[] GetEventSubscribers<TEvent>() where TEvent : EventBase;
        ListenerList GetListenerList<TEvent>() where TEvent : EventBase;
        IReadOnlyDictionary<Type, EventHandlerInfo[]> GetAllSubscribersSnapshot();
        IReadOnlyList<ShrinkEventSubscriptionSnapshot> GetActiveSubscriptionsSnapshot();

        bool IsInstanceRegistered(object target);
        int GetRegisteredInstanceCount();
        int GetRegisteredEventTypeCount();
    }

    public interface IShrinkEventExceptionHandler
    {
        void HandleException(IShrinkEventBus bus, EventBase eventArgs, EventHandlerInfo[] listeners, int index,
            Exception exception);
    }

    public enum ShrinkEventExceptionHandlingMode
    {
        LogAndContinue,
        Throw,
        LogAndThrow
    }
}
