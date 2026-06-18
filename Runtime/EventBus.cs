#nullable enable
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace ShrinkEventBus
{
    public static class EventBus
    {
        private static readonly IShrinkEventBus DefaultBus = CreateDefaultBus();

        public static IShrinkEventBus Default => DefaultBus;

        public static event Action<EventBase, Type> OnEventTriggered
        {
            add => DefaultBus.OnEventTriggered += value;
            remove => DefaultBus.OnEventTriggered -= value;
        }

#if UNITY_EDITOR
        public static bool EnableDebugRecord
        {
            get => DefaultBus.EnableDebugRecord;
            set => DefaultBus.EnableDebugRecord = value;
        }

        public static event Action<EventBase, string, string, EventHandlerInfo[]> OnEventTriggeredForEditor
        {
            add => DefaultBus.OnEventTriggeredForEditor += value;
            remove => DefaultBus.OnEventTriggeredForEditor -= value;
        }
#endif

        public static ShrinkEventBusBuilder Builder() => new();

        public static IShrinkEventBus CreateBus(Action<ShrinkEventBusBuilder>? configure = null)
        {
            var builder = new ShrinkEventBusBuilder();
            configure?.Invoke(builder);
            return builder.Build();
        }

        public static void Start() => DefaultBus.Start();
        public static void AutoRegister(object target) => DefaultBus.AutoRegister(target);
        public static void Register(object target) => DefaultBus.Register(target);
        public static void Unregister(object target) => DefaultBus.Unregister(target);

        public static void RegisterEvent<TEvent>(Func<TEvent, UniTask> handler, int priority)
            where TEvent : EventBase => DefaultBus.RegisterEvent(handler, priority);

        public static void RegisterEvent<TEvent>(Action<TEvent> handler,
            EventPriority priority = EventPriority.NORMAL, bool receiveCanceled = false)
            where TEvent : EventBase => DefaultBus.RegisterEvent(handler, priority, receiveCanceled);

        public static void RegisterEvent<TEvent>(Action<TEvent> handler, int priority)
            where TEvent : EventBase => DefaultBus.RegisterEvent(handler, priority);

        public static void RegisterEvent<TEvent>(Func<TEvent, UniTask> handler,
            EventPriority priority = EventPriority.NORMAL, bool receiveCanceled = false)
            where TEvent : EventBase => DefaultBus.RegisterEvent(handler, priority, receiveCanceled);

        public static IShrinkEventSubscription SubscribeEvent<TEvent>(Action<TEvent> handler,
            EventPriority priority = EventPriority.NORMAL, bool receiveCanceled = false)
            where TEvent : EventBase => DefaultBus.SubscribeEvent(handler, priority, receiveCanceled);

        public static IShrinkEventSubscription SubscribeEvent<TEvent>(Action<TEvent> handler, int priority)
            where TEvent : EventBase => DefaultBus.SubscribeEvent(handler, priority);

        public static IShrinkEventSubscription SubscribeEvent<TEvent>(Func<TEvent, UniTask> handler,
            EventPriority priority = EventPriority.NORMAL, bool receiveCanceled = false)
            where TEvent : EventBase => DefaultBus.SubscribeEvent(handler, priority, receiveCanceled);

        public static IShrinkEventSubscription SubscribeEvent<TEvent>(Func<TEvent, UniTask> handler, int priority)
            where TEvent : EventBase => DefaultBus.SubscribeEvent(handler, priority);

        public static void UnregisterEvent<TEvent>(Func<TEvent, UniTask> handler) where TEvent : EventBase =>
            DefaultBus.UnregisterEvent(handler);

        public static void UnregisterEvent<TEvent>(Action<TEvent> handler) where TEvent : EventBase =>
            DefaultBus.UnregisterEvent(handler);

        public static void ClearAllSubscribersForEvent<TEvent>() where TEvent : EventBase =>
            DefaultBus.ClearAllSubscribersForEvent<TEvent>();

        public static void UnregisterAllEventsForObject(object targetObject) =>
            DefaultBus.UnregisterAllEventsForObject(targetObject);

        public static void UnregisterInstance(object targetObject) => DefaultBus.Unregister(targetObject);
        public static void UnregisterAllEvents() => DefaultBus.UnregisterAllEvents();

        public static UniTask<bool> TriggerEventAsync<TEvent>(TEvent eventArgs) where TEvent : EventBase =>
            DefaultBus.TriggerEventAsync(eventArgs);

        public static UniTask<bool> TriggerEventAsync<TEvent>(EventPriority phase, TEvent eventArgs)
            where TEvent : EventBase => DefaultBus.TriggerEventAsync(phase, eventArgs);

        public static bool TriggerEvent<TEvent>(TEvent eventArgs) where TEvent : EventBase =>
            DefaultBus.TriggerEvent(eventArgs);

        public static bool TriggerEvent<TEvent>(EventPriority phase, TEvent eventArgs) where TEvent : EventBase =>
            DefaultBus.TriggerEvent(phase, eventArgs);

        public static EventHandlerInfo[] GetEventSubscribers<TEvent>() where TEvent : EventBase =>
            DefaultBus.GetEventSubscribers<TEvent>();

        public static ListenerList GetListenerList<TEvent>() where TEvent : EventBase =>
            DefaultBus.GetListenerList<TEvent>();

        public static IReadOnlyDictionary<Type, EventHandlerInfo[]> GetAllSubscribersSnapshot() =>
            DefaultBus.GetAllSubscribersSnapshot();

        public static IReadOnlyList<ShrinkEventSubscriptionSnapshot> GetActiveSubscriptionsSnapshot() =>
            DefaultBus.GetActiveSubscriptionsSnapshot();

        public static bool IsInstanceRegistered(object target) => DefaultBus.IsInstanceRegistered(target);
        public static int GetRegisteredInstanceCount() => DefaultBus.GetRegisteredInstanceCount();
        public static int GetRegisteredEventTypeCount() => DefaultBus.GetRegisteredEventTypeCount();

        private static IShrinkEventBus CreateDefaultBus()
        {
            var bus = (ShrinkEventBusInstance)new ShrinkEventBusBuilder()
                .SetExceptionHandlingMode(ShrinkEventExceptionHandlingMode.LogAndContinue)
                .AllowPerPhaseDispatch()
                .Build();
            EventBusRegHelper.RegStaticEventHandler(bus);
            return bus;
        }
    }
}
