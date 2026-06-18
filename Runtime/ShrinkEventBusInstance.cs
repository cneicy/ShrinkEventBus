#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ShrinkEventBus
{
    internal sealed class ShrinkEventBusInstance : IShrinkEventBus
    {
        private sealed class EventSubscription : IShrinkEventSubscription
        {
            private readonly ShrinkEventBusInstance _owner;
            private bool _disposed;

            public EventSubscription(ShrinkEventBusInstance owner, long subscriptionId)
            {
                _owner = owner ?? throw new ArgumentNullException(nameof(owner));
                SubscriptionId = subscriptionId;
            }

            public long SubscriptionId { get; }
            public bool IsDisposed => _disposed;

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _owner.UnregisterSubscription(SubscriptionId);
            }
        }

        private readonly Dictionary<Type, ListenerList> _eventHandlers = new();
        private readonly HashSet<object> _registeredTargets = new();
        private readonly object _listenerLock = new();
        private readonly object _registrationLock = new();
        private long _nextSubscriptionId;
        private readonly IShrinkEventExceptionHandler? _exceptionHandler;
        private readonly ShrinkEventExceptionHandlingMode _exceptionHandlingMode;
        private readonly Action<Type>? _eventClassChecker;
        private readonly bool _checkTypesOnDispatch;
        private readonly bool _allowPerPhaseDispatch;

        public event Action<EventBase, Type>? OnEventTriggered;

#if UNITY_EDITOR
        public bool EnableDebugRecord { get; set; }
        public event Action<EventBase, string, string, EventHandlerInfo[]>? OnEventTriggeredForEditor;
#endif

        public bool IsStarted { get; private set; }

        public ShrinkEventBusInstance(ShrinkEventBusBuilder builder)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            _exceptionHandler = builder.ExceptionHandler;
            _exceptionHandlingMode = builder.ExceptionHandlingMode;
            _eventClassChecker = builder.EventClassChecker;
            _checkTypesOnDispatch = builder.CheckTypesOnDispatchEnabled;
            _allowPerPhaseDispatch = builder.AllowPerPhaseDispatchEnabled;
            IsStarted = !builder.StartShutdownEnabled;
        }

        public void Start()
        {
            IsStarted = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AutoRegister(object target)
        {
            RegisterCore(target, requireSubscriberAttribute: true, lenientWhenNoMethods: true);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Register(object target)
        {
            RegisterCore(target, requireSubscriberAttribute: false, lenientWhenNoMethods: false);
        }

        public void Unregister(object target)
        {
            if (target == null)
                return;

            lock (_registrationLock)
            {
                _registeredTargets.Remove(target);
            }

            switch (target)
            {
                case Delegate handler:
                    RemoveHandlers(info => Equals(info.Handler, handler));
                    return;
                case MethodInfo method:
                    RemoveHandlers(info => info.MatchesMethod(method));
                    return;
                case Type declaringType:
                    RemoveHandlers(info => info.Target == null && info.MatchesDeclaringType(declaringType));
                    return;
                default:
                    UnregisterAllEventsForObject(target);
                    return;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RegisterEvent<TEvent>(Func<TEvent, UniTask> handler, int priority)
            where TEvent : EventBase
        {
            var eventPriority = PriorityHelper.ConvertToEventPriority(priority);
            RegisterEventInternal(typeof(TEvent), handler, eventPriority, priority, false,
                $"Manual Async Handler (Priority: {priority})");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RegisterEvent<TEvent>(Action<TEvent> handler, EventPriority priority = EventPriority.NORMAL,
            bool receiveCanceled = false) where TEvent : EventBase
        {
            RegisterEventInternal(typeof(TEvent), handler, priority, 0, receiveCanceled,
                $"Manual Sync Handler (Priority: {priority})", handler.Method);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RegisterEvent<TEvent>(Action<TEvent> handler, int priority) where TEvent : EventBase
        {
            var eventPriority = PriorityHelper.ConvertToEventPriority(priority);
            RegisterEventInternal(typeof(TEvent), handler, eventPriority, priority, false,
                $"Manual Sync Handler (Priority: {priority})", handler.Method);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RegisterEvent<TEvent>(Func<TEvent, UniTask> handler,
            EventPriority priority = EventPriority.NORMAL, bool receiveCanceled = false) where TEvent : EventBase
        {
            RegisterEventInternal(typeof(TEvent), handler, priority, 0, receiveCanceled,
                $"Manual Async Handler (Priority: {priority})");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IShrinkEventSubscription SubscribeEvent<TEvent>(Action<TEvent> handler,
            EventPriority priority = EventPriority.NORMAL, bool receiveCanceled = false) where TEvent : EventBase
        {
            return SubscribeEventInternal(typeof(TEvent), handler, priority, 0, receiveCanceled,
                $"Manual Sync Subscription (Priority: {priority})", handler.Method);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IShrinkEventSubscription SubscribeEvent<TEvent>(Action<TEvent> handler, int priority)
            where TEvent : EventBase
        {
            var eventPriority = PriorityHelper.ConvertToEventPriority(priority);
            return SubscribeEventInternal(typeof(TEvent), handler, eventPriority, priority, false,
                $"Manual Sync Subscription (Priority: {priority})", handler.Method);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IShrinkEventSubscription SubscribeEvent<TEvent>(Func<TEvent, UniTask> handler,
            EventPriority priority = EventPriority.NORMAL, bool receiveCanceled = false) where TEvent : EventBase
        {
            return SubscribeEventInternal(typeof(TEvent), handler, priority, 0, receiveCanceled,
                $"Manual Async Subscription (Priority: {priority})");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IShrinkEventSubscription SubscribeEvent<TEvent>(Func<TEvent, UniTask> handler, int priority)
            where TEvent : EventBase
        {
            var eventPriority = PriorityHelper.ConvertToEventPriority(priority);
            return SubscribeEventInternal(typeof(TEvent), handler, eventPriority, priority, false,
                $"Manual Async Subscription (Priority: {priority})");
        }

        internal void RegisterEventInternal(Type eventType, Delegate handler, EventPriority priority,
            int numericPriority, bool receiveCanceled, string debugInfo = "", MethodInfo? originalMethod = null)
        {
            if (eventType == null)
                throw new ArgumentNullException(nameof(eventType));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            ValidateEventType(eventType, "register");
            var handlerInfo = EventHandlerInfo.Create(GetNextSubscriptionId(), handler, eventType, priority,
                numericPriority, receiveCanceled,
                debugInfo, originalMethod);

            lock (_listenerLock)
            {
                GetOrCreateListenerList(eventType).Add(handlerInfo);
            }
        }

        private IShrinkEventSubscription SubscribeEventInternal(Type eventType, Delegate handler, EventPriority priority,
            int numericPriority, bool receiveCanceled, string debugInfo = "", MethodInfo? originalMethod = null)
        {
            if (eventType == null)
                throw new ArgumentNullException(nameof(eventType));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            ValidateEventType(eventType, "subscribe");
            var subscriptionId = GetNextSubscriptionId();
            var handlerInfo = EventHandlerInfo.Create(subscriptionId, handler, eventType, priority, numericPriority,
                receiveCanceled, debugInfo, originalMethod);

            lock (_listenerLock)
            {
                GetOrCreateListenerList(eventType).Add(handlerInfo);
            }

            return new EventSubscription(this, subscriptionId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UnregisterEvent<TEvent>(Func<TEvent, UniTask> handler) where TEvent : EventBase
        {
            if (handler == null)
                return;

            var collection = TryGetListenerList(typeof(TEvent));
            collection?.Remove(handler);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UnregisterEvent<TEvent>(Action<TEvent> handler) where TEvent : EventBase
        {
            if (handler == null)
                return;

            var collection = TryGetListenerList(typeof(TEvent));
            collection?.Remove(handler);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearAllSubscribersForEvent<TEvent>() where TEvent : EventBase
        {
            var collection = TryGetListenerList(typeof(TEvent));
            collection?.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UnregisterAllEventsForObject(object targetObject)
        {
            if (targetObject is null)
                return;

            lock (_listenerLock)
            {
                foreach (var kvp in _eventHandlers)
                    kvp.Value.RemoveTarget(targetObject);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UnregisterAllEvents()
        {
            lock (_listenerLock)
            {
                foreach (var kvp in _eventHandlers)
                    kvp.Value.Clear();
            }

            lock (_registrationLock)
            {
                _registeredTargets.Clear();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UniTask<bool> TriggerEventAsync<TEvent>(TEvent eventArgs) where TEvent : EventBase
        {
            return TriggerEventAsyncInternal(eventArgs, null);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UniTask<bool> TriggerEventAsync<TEvent>(EventPriority phase, TEvent eventArgs) where TEvent : EventBase
        {
            EnsurePerPhaseDispatchAllowed();
            return TriggerEventAsyncInternal(eventArgs, phase);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TriggerEvent<TEvent>(TEvent eventArgs) where TEvent : EventBase
        {
            return TriggerEventInternal(eventArgs, null);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TriggerEvent<TEvent>(EventPriority phase, TEvent eventArgs) where TEvent : EventBase
        {
            EnsurePerPhaseDispatchAllowed();
            return TriggerEventInternal(eventArgs, phase);
        }

        private async UniTask<bool> TriggerEventAsyncInternal(EventBase eventArgs, EventPriority? phase)
        {
            if (!TryPrepareDispatch(eventArgs, phase, out var eventType, out var handlers))
                return false;

            var wasHandled = false;
            for (var i = 0; i < handlers.Length; i++)
            {
                var handlerInfo = handlers[i];
                if (!TryPrepareHandler(eventArgs, handlerInfo))
                    continue;

                try
                {
                    if (handlerInfo.SyncInvoker != null)
                    {
                        handlerInfo.SyncInvoker(eventArgs);
                        wasHandled = true;
                    }
                    else if (handlerInfo.AsyncInvoker != null)
                    {
                        await handlerInfo.AsyncInvoker(eventArgs);
                        wasHandled = true;
                    }
                }
                catch (Exception ex)
                {
                    if (HandleListenerException(eventArgs, handlers, i, ex))
                        throw;
                }
            }

            eventArgs.CurrentHandler = null;
            CompleteEventDispatch(eventArgs, eventType, handlers);
            return wasHandled;
        }

        private bool TriggerEventInternal(EventBase eventArgs, EventPriority? phase)
        {
            if (!TryPrepareDispatch(eventArgs, phase, out var eventType, out var handlers))
                return false;

            var wasHandled = false;
            for (var i = 0; i < handlers.Length; i++)
            {
                var handlerInfo = handlers[i];
                if (!TryPrepareHandler(eventArgs, handlerInfo))
                    continue;

                try
                {
                    if (handlerInfo.SyncInvoker != null)
                    {
                        handlerInfo.SyncInvoker(eventArgs);
                        wasHandled = true;
                    }
                    else if (handlerInfo.AsyncInvoker != null)
                    {
                        var detachedEvent = EventCloneUtility.CloneForDetachedDispatch(eventArgs);
                        FireAndForgetSafe(() => handlerInfo.AsyncInvoker(detachedEvent),
                            $"{handlerInfo.DisplayDeclaringType.Name}.{handlerInfo.DisplayMethodName}");
                        wasHandled = true;
                    }
                }
                catch (Exception ex)
                {
                    if (HandleListenerException(eventArgs, handlers, i, ex))
                        throw;
                }
            }

            eventArgs.CurrentHandler = null;
            CompleteEventDispatch(eventArgs, eventType, handlers);
            return wasHandled;
        }

        private bool TryPrepareDispatch(EventBase eventArgs, EventPriority? phase, out Type eventType,
            out EventHandlerInfo[] handlers)
        {
            if (eventArgs == null)
                throw new ArgumentNullException(nameof(eventArgs));

            if (!IsStarted)
            {
                eventType = eventArgs.GetType();
                handlers = Array.Empty<EventHandlerInfo>();
                return false;
            }

            eventType = eventArgs.GetType();
            ValidateDispatchEventType(eventType);
            eventArgs.PrepareForDispatch();

            lock (_listenerLock)
            {
                var listenerList = GetOrCreateListenerList(eventType);
                handlers = phase.HasValue ? listenerList.GetHandlers(phase.Value) : listenerList.GetHandlers();
            }

            eventArgs.SetListenerSnapshot(handlers);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryPrepareHandler(EventBase eventArgs, EventHandlerInfo handlerInfo)
        {
            eventArgs.CurrentHandler = handlerInfo;
            eventArgs.SetPhase(handlerInfo.Priority);
            if (eventArgs.IsCancelable && eventArgs.IsCanceled && !handlerInfo.ReceiveCanceled)
                return false;
            return true;
        }

        private bool HandleListenerException(EventBase eventArgs, EventHandlerInfo[] handlers, int index, Exception exception)
        {
            _exceptionHandler?.HandleException(this, eventArgs, handlers, index, exception);

            switch (_exceptionHandlingMode)
            {
                case ShrinkEventExceptionHandlingMode.Throw:
                    return true;
                case ShrinkEventExceptionHandlingMode.LogAndThrow:
                    LogHandlerException(exception);
                    return true;
                default:
                    LogHandlerException(exception);
                    return false;
            }
        }

        private static void LogHandlerException(Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError($"[EventBus] Handler exception: {exception.Message}");
        }

        private static void FireAndForgetSafe(Func<UniTask> action, string handlerName)
        {
            action().Forget(e =>
            {
                Debug.LogException(e);
                Debug.LogError($"[EventBus] Async Handler {handlerName} threw exception: {e.Message}");
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CompleteEventDispatch(EventBase eventArgs, Type eventType, EventHandlerInfo[] handlers)
        {
            OnEventTriggered?.Invoke(eventArgs, eventType);

#if UNITY_EDITOR
            var editorTraceHandler = OnEventTriggeredForEditor;
            if (!EnableDebugRecord || editorTraceHandler == null)
                return;

            editorTraceHandler.Invoke(eventArgs, eventType.Name, GetSenderInfo(), handlers);
#endif
        }

#if UNITY_EDITOR
        private static string GetSenderInfo()
        {
            var senderInfo = "Unknown";
            try
            {
                var trace = new System.Diagnostics.StackTrace(0, false);
                for (var i = 0; i < trace.FrameCount; i++)
                {
                    var method = trace.GetFrame(i)?.GetMethod();
                    var declaringType = method?.DeclaringType;
                    if (declaringType == null)
                        continue;

                    if (declaringType == typeof(EventBus) || declaringType.DeclaringType == typeof(EventBus))
                        continue;

                    if (declaringType == typeof(ShrinkEventBusInstance) ||
                        declaringType.DeclaringType == typeof(ShrinkEventBusInstance))
                        continue;

                    var ns = declaringType.Namespace ?? string.Empty;
                    if (ns.StartsWith("System") || ns.StartsWith("Cysharp") || ns.StartsWith("UnityEngine"))
                        continue;

                    var className = declaringType.Name;
                    var methodName = method!.Name;
                    if (declaringType.DeclaringType != null && className.StartsWith("<") && className.Contains(">"))
                    {
                        className = declaringType.DeclaringType.Name;
                        var startIndex = declaringType.Name.IndexOf('<') + 1;
                        var endIndex = declaringType.Name.IndexOf('>');
                        if (startIndex > 0 && endIndex > startIndex)
                            methodName = declaringType.Name.Substring(startIndex, endIndex - startIndex);
                    }

                    return $"{className}.{methodName}()";
                }
            }
            catch
            {
            }

            return senderInfo;
        }
#endif

        public EventHandlerInfo[] GetEventSubscribers<TEvent>() where TEvent : EventBase
        {
            var listenerList = TryGetListenerList(typeof(TEvent));
            var handlers = listenerList?.GetHandlers();
            if (handlers == null || handlers.Length == 0)
                return Array.Empty<EventHandlerInfo>();

            return (EventHandlerInfo[])handlers.Clone();
        }

        public ListenerList GetListenerList<TEvent>() where TEvent : EventBase
        {
            lock (_listenerLock)
            {
                return GetOrCreateListenerList(typeof(TEvent));
            }
        }

        public IReadOnlyDictionary<Type, EventHandlerInfo[]> GetAllSubscribersSnapshot()
        {
            lock (_listenerLock)
            {
                var snapshot = new Dictionary<Type, EventHandlerInfo[]>(_eventHandlers.Count);
                foreach (var kvp in _eventHandlers)
                {
                    var handlers = kvp.Value.GetHandlers();
                    if (handlers.Length == 0)
                        continue;

                    snapshot[kvp.Key] = (EventHandlerInfo[])handlers.Clone();
                }

                return snapshot;
            }
        }

        public IReadOnlyList<ShrinkEventSubscriptionSnapshot> GetActiveSubscriptionsSnapshot()
        {
            lock (_listenerLock)
            {
                var snapshot = new List<ShrinkEventSubscriptionSnapshot>();
                foreach (var entry in _eventHandlers)
                {
                    var eventType = entry.Key;
                    var handlers = entry.Value.GetHandlers();
                    for (var i = 0; i < handlers.Length; i++)
                    {
                        var handler = handlers[i];
                        snapshot.Add(new ShrinkEventSubscriptionSnapshot(
                            handler.SubscriptionId,
                            eventType,
                            handler.Priority,
                            handler.NumericPriority,
                            handler.ReceiveCanceled,
                            handler.Target,
                            handler.DisplayDeclaringType.FullName ?? handler.DisplayDeclaringType.Name,
                            handler.DisplayMethodName,
                            handler.DebugInfo,
                            handler.RegisteredAtUtc));
                    }
                }

                return snapshot;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsInstanceRegistered(object target)
        {
            if (target == null)
                return false;

            lock (_registrationLock)
            {
                return _registeredTargets.Contains(target);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetRegisteredInstanceCount()
        {
            lock (_registrationLock)
            {
                return _registeredTargets.Count;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetRegisteredEventTypeCount()
        {
            lock (_listenerLock)
            {
                var count = 0;
                foreach (var listenerList in _eventHandlers.Values)
                {
                    if (listenerList.LocalCount > 0)
                        count++;
                }

                return count;
            }
        }

        private void RegisterCore(object target, bool requireSubscriberAttribute, bool lenientWhenNoMethods)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            lock (_registrationLock)
            {
                if (_registeredTargets.Contains(target))
                    return;

                EventBusRegHelper.RegisterTarget(this, target, requireSubscriberAttribute, lenientWhenNoMethods);
                _registeredTargets.Add(target);
            }
        }

        private void RemoveHandlers(Predicate<EventHandlerInfo> predicate)
        {
            lock (_listenerLock)
            {
                foreach (var listenerList in _eventHandlers.Values)
                    listenerList.RemoveWhere(predicate);
            }
        }

        private void UnregisterSubscription(long subscriptionId)
        {
            RemoveHandlers(info => info.SubscriptionId == subscriptionId);
        }

        private long GetNextSubscriptionId() => Interlocked.Increment(ref _nextSubscriptionId);

        private ListenerList GetOrCreateListenerList(Type eventType)
        {
            if (_eventHandlers.TryGetValue(eventType, out var existing))
                return existing;

            ListenerList? parent = null;
            var parentType = GetParentEventType(eventType);
            if (parentType != null)
                parent = GetOrCreateListenerList(parentType);

            var created = new ListenerList(parent, _listenerLock);
            _eventHandlers[eventType] = created;
            return created;
        }

        private ListenerList? TryGetListenerList(Type eventType)
        {
            if (eventType == null)
                return null;

            lock (_listenerLock)
            {
                return _eventHandlers.TryGetValue(eventType, out var listenerList) ? listenerList : null;
            }
        }

        private void ValidateEventType(Type eventType, string action)
        {
            if (!typeof(EventBase).IsAssignableFrom(eventType))
                throw new ArgumentException(
                    $"Cannot {action} listener for {eventType.FullName}: type must inherit from EventBase.");

            _eventClassChecker?.Invoke(eventType);
        }

        private void ValidateDispatchEventType(Type eventType)
        {
            if (!typeof(EventBase).IsAssignableFrom(eventType))
                throw new ArgumentException($"Cannot dispatch event type {eventType.FullName}: not an EventBase.");

            if (_checkTypesOnDispatch)
                _eventClassChecker?.Invoke(eventType);
        }

        private static Type? GetParentEventType(Type eventType)
        {
            var current = eventType.BaseType;
            while (current != null && current != typeof(object))
            {
                if (typeof(EventBase).IsAssignableFrom(current))
                    return current;

                current = current.BaseType;
            }

            return null;
        }

        private void EnsurePerPhaseDispatchAllowed()
        {
            if (!_allowPerPhaseDispatch)
                throw new InvalidOperationException(
                    "Per-phase event dispatch is disabled for this bus. Enable it via ShrinkEventBusBuilder.AllowPerPhaseDispatch().");
        }
    }
}
