using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ShrinkEventBus
{
    public static class EventBus
    {
#if UNITY_EDITOR
        public static bool EnableDebugRecord { get; set; }
        public static event Action<EventBase, string, string> OnEventTriggeredForEditor;
#endif

        private static readonly Dictionary<Type, ListenerList> EventHandlers = new();
        private static readonly HashSet<object> RegisteredInstances = new();
        private static readonly object InstanceLock = new();

        static EventBus()
        {
            EventBusRegHelper.RegStaticEventHandler();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EnsureAutoManagerInitialized() => EventAutoRegHelper.EnsureInitialized();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RegisterEvent<TEvent>(Action<TEvent> handler, string methodName, Type declaringType,
            EventPriority priority = EventPriority.NORMAL, bool receiveCanceled = false) where TEvent : EventBase
        {
            var methodInfo = declaringType.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            RegisterEventInternal(typeof(TEvent), handler, priority, 0, receiveCanceled,
                $"Manual Sync Handler (Priority: {priority})", methodInfo);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RegisterEvent<TEvent>(Func<TEvent, UniTask> handler, int priority = 0)
            where TEvent : EventBase
        {
            var eventPriority = PriorityHelper.ConvertToEventPriority(priority);
            RegisterEventInternal(typeof(TEvent), handler, eventPriority, priority, false,
                $"Manual Async Handler (Priority: {priority})");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RegisterEvent<TEvent>(Action<TEvent> handler, EventPriority priority = EventPriority.NORMAL,
            bool receiveCanceled = false) where TEvent : EventBase
        {
            RegisterEventInternal(typeof(TEvent), handler, priority, 0, receiveCanceled,
                $"Manual Sync Handler (Priority: {priority})", handler.Method);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RegisterEvent<TEvent>(Action<TEvent> handler, int priority = 0) where TEvent : EventBase
        {
            var eventPriority = PriorityHelper.ConvertToEventPriority(priority);
            RegisterEventInternal(typeof(TEvent), handler, eventPriority, priority, false,
                $"Manual Sync Handler (Priority: {priority})", handler.Method);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RegisterEvent<TEvent>(Func<TEvent, UniTask> handler,
            EventPriority priority = EventPriority.NORMAL, bool receiveCanceled = false) where TEvent : EventBase
        {
            RegisterEventInternal(typeof(TEvent), handler, priority, 0, receiveCanceled,
                $"Manual Async Handler (Priority: {priority})");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void RegisterEventInternal(Type eventType, Delegate handler, EventPriority priority,
            int numericPriority, bool receiveCanceled, string debugInfo = "", MethodInfo originalMethod = null)
        {
            lock (EventHandlers)
            {
                if (!EventHandlers.TryGetValue(eventType, out var collection))
                {
                    collection = new ListenerList();
                    EventHandlers[eventType] = collection;
                    var cacheType = typeof(EventCache<>).MakeGenericType(eventType);
                    cacheType.GetField("List", BindingFlags.Static | BindingFlags.Public)?.SetValue(null, collection);
                }

                collection.Add(handler, priority, numericPriority, receiveCanceled, debugInfo, originalMethod);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UnregisterEvent<TEvent>(Func<TEvent, UniTask> handler) where TEvent : EventBase
        {
            var eventType = typeof(TEvent);
            lock (EventHandlers)
            {
                if (!EventHandlers.TryGetValue(eventType, out var collection)) return;
                collection.Remove(handler);
                if (collection.Count == 0)
                {
                    EventHandlers.Remove(eventType);
                    EventCache<TEvent>.List = null;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UnregisterEvent<TEvent>(Action<TEvent> handler) where TEvent : EventBase
        {
            var eventType = typeof(TEvent);
            lock (EventHandlers)
            {
                if (!EventHandlers.TryGetValue(eventType, out var collection)) return;
                collection.Remove(handler);
                if (collection.Count == 0)
                {
                    EventHandlers.Remove(eventType);
                    EventCache<TEvent>.List = null;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ClearAllSubscribersForEvent<TEvent>() where TEvent : EventBase
        {
            var eventType = typeof(TEvent);
            lock (EventHandlers)
            {
                if (EventHandlers.Remove(eventType))
                {
                    EventCache<TEvent>.List = null;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UnregisterAllEventsForObject(object targetObject)
        {
            if (targetObject is null) return;
            lock (EventHandlers)
            {
                var eventTypesToRemove = new List<Type>();
                foreach (var kvp in EventHandlers)
                {
                    kvp.Value.RemoveTarget(targetObject);
                    if (kvp.Value.Count == 0) eventTypesToRemove.Add(kvp.Key);
                }

                foreach (var eventType in eventTypesToRemove)
                {
                    EventHandlers.Remove(eventType);
                    var cacheType = typeof(EventCache<>).MakeGenericType(eventType);
                    cacheType.GetField("List", BindingFlags.Static | BindingFlags.Public)?.SetValue(null, null);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UnregisterInstance(object targetObject)
        {
            if (targetObject is null) return;
            lock (InstanceLock)
            {
                RegisteredInstances.Remove(targetObject);
            }

            UnregisterAllEventsForObject(targetObject);
            if (targetObject is not MonoBehaviour mb || !mb) return;
            try
            {
                if (!mb.gameObject ||
                    !mb.gameObject.TryGetComponent<EventBusDestroyListener>(out var listener)) return;
                if (listener)
                {
                    UnityEngine.Object.Destroy(listener);
                }
            }
            catch (MissingReferenceException)
            {
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UnregisterAllEvents()
        {
            lock (EventHandlers)
            {
                foreach (var type in EventHandlers.Keys)
                {
                    var cacheType = typeof(EventCache<>).MakeGenericType(type);
                    cacheType.GetField("List", BindingFlags.Static | BindingFlags.Public).SetValue(null, null);
                }

                EventHandlers.Clear();
            }

            lock (InstanceLock)
            {
                RegisteredInstances.Clear();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static async UniTask<bool> TriggerEventAsync<TEvent>(TEvent eventArgs) where TEvent : EventBase
        {
            EnsureAutoManagerInitialized();

            var collection = EventCache<TEvent>.List;
            if (collection == null) return false;

            var handlers = collection.GetHandlers();
            var wasHandled = false;

#if UNITY_EDITOR
            RecordEditorTrace(handlers, eventArgs, typeof(TEvent));
#endif

            for (var i = 0; i < handlers.Length; i++)
            {
                var handlerInfo = handlers[i];
                eventArgs.CurrentHandler = handlerInfo;
                eventArgs.SetPhase(handlerInfo.Priority);

                if (eventArgs is { IsCancelable: true, IsCanceled: true } && !handlerInfo.ReceiveCanceled) continue;

                try
                {
                    if (handlerInfo.Handler is Action<TEvent> syncHandler)
                    {
                        syncHandler(eventArgs);
                        wasHandled = true;
                    }
                    else if (handlerInfo.Handler is Func<TEvent, UniTask> asyncHandler)
                    {
                        await asyncHandler(eventArgs);
                        wasHandled = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    Debug.LogError(
                        $"[EventBus] {typeof(TEvent).Name} Async Handler {handlerInfo.DisplayDeclaringType.Name}.{handlerInfo.DisplayMethodName} Exception: {ex.Message}");
                }
            }

            eventArgs.CurrentHandler = null;
            return wasHandled;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TriggerEvent<TEvent>(TEvent eventArgs) where TEvent : EventBase
        {
            EnsureAutoManagerInitialized();

            var collection = EventCache<TEvent>.List;
            if (collection == null) return false;

            var handlers = collection.GetHandlers();
            var wasHandled = false;

#if UNITY_EDITOR
            RecordEditorTrace(handlers, eventArgs, typeof(TEvent));
#endif

            for (var i = 0; i < handlers.Length; i++)
            {
                var handlerInfo = handlers[i];
                eventArgs.CurrentHandler = handlerInfo;
                eventArgs.SetPhase(handlerInfo.Priority);

                if (eventArgs is { IsCancelable: true, IsCanceled: true } && !handlerInfo.ReceiveCanceled) continue;

                try
                {
                    if (handlerInfo.Handler is Action<TEvent> syncHandler)
                    {
                        syncHandler(eventArgs);
                        wasHandled = true;
                    }
                    else if (handlerInfo.Handler is Func<TEvent, UniTask> asyncHandler)
                    {
                        FireAndForgetSafe(() => asyncHandler(eventArgs),
                            $"{handlerInfo.DisplayDeclaringType.Name}.{handlerInfo.DisplayMethodName}");
                        wasHandled = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    Debug.LogError(
                        $"[EventBus] {typeof(TEvent).Name} Sync Handler {handlerInfo.DisplayDeclaringType.Name}.{handlerInfo.DisplayMethodName} Exception: {ex.Message}");
                }
            }

            eventArgs.CurrentHandler = null;
            return wasHandled;
        }

        private static void FireAndForgetSafe(Func<UniTask> action, string handlerName)
        {
            action().Forget(e =>
            {
                Debug.LogException(e);
                Debug.LogError($"[EventBus] Async Handler {handlerName} threw exception: {e.Message}");
            });
        }

#if UNITY_EDITOR
        private static void RecordEditorTrace<TEvent>(EventHandlerInfo[] handlers, TEvent eventArgs, Type eventType)
            where TEvent : EventBase
        {
            if (!EnableDebugRecord) return;
            for (var i = 0; i < handlers.Length; i++)
            {
                var handler = handlers[i];
                MethodInfo originalMethod = null;
                var type = handler.GetType();
                var prop = type.GetProperty("OriginalMethod");
                if (prop != null) originalMethod = prop.GetValue(handler) as MethodInfo;
                else
                {
                    var field = type.GetField("OriginalMethod");
                    if (field != null) originalMethod = field.GetValue(handler) as MethodInfo;
                }

                eventArgs.GetListenerList().Add(handler.Handler, handler.Priority, handler.NumericPriority,
                    handler.ReceiveCanceled, handler.DebugInfo, originalMethod);
            }

            OnEventTriggeredForEditor?.Invoke(eventArgs, eventType.Name, GetSenderInfo());
        }

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

                    if (declaringType == null) continue;
                    if (declaringType == typeof(EventBus) ||
                        declaringType.DeclaringType == typeof(EventBus)) continue;
                    var ns = declaringType.Namespace ?? "";
                    if (ns.StartsWith("System") || ns.StartsWith("Cysharp") || ns.StartsWith("UnityEngine"))
                        continue;

                    var className = declaringType.Name;
                    var methodName = method.Name;

                    if (declaringType.DeclaringType != null && className.StartsWith("<") &&
                        className.Contains(">"))
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
                // ignored
            }

            return senderInfo;
        }
#endif

        public static EventHandlerInfo[] GetEventSubscribers<TEvent>() where TEvent : EventBase
        {
            var eventType = typeof(TEvent);
            lock (EventHandlers)
            {
                return EventHandlers.TryGetValue(eventType, out var collection)
                    ? collection.GetHandlers()
                    : Array.Empty<EventHandlerInfo>();
            }
        }

        public static ListenerList GetListenerList<TEvent>() where TEvent : EventBase
        {
            var eventType = typeof(TEvent);
            lock (EventHandlers)
            {
                if (EventHandlers.TryGetValue(eventType, out var collection)) return collection;
                return new ListenerList();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AutoRegister(object target)
        {
            if (target == null) return;

            if (IsInstanceRegistered(target)) return;

            EventBusRegHelper.RegisterEventHandlers(target);

            lock (InstanceLock)
            {
                if (!RegisteredInstances.Add(target)) return;
            }

            if (target is MonoBehaviour mb && mb && mb.gameObject)
            {
                if (!mb.gameObject.TryGetComponent<EventBusDestroyListener>(out _))
                {
                    var listener = mb.gameObject.AddComponent<EventBusDestroyListener>();
                    listener.Target = target;
                    listener.hideFlags = HideFlags.HideAndDontSave;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInstanceRegistered(object target)
        {
            if (target == null) return false;
            lock (InstanceLock)
            {
                return RegisteredInstances.Contains(target);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetRegisteredInstanceCount()
        {
            lock (InstanceLock)
            {
                return RegisteredInstances.Count;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetRegisteredEventTypeCount()
        {
            lock (EventHandlers)
            {
                return EventHandlers.Count;
            }
        }
    }
}