using System;
using System.Reflection;
using Cysharp.Threading.Tasks;
#pragma warning disable CS8632 // 只能在 "#nullable" 注释上下文内的代码中使用可为 null 的引用类型的注释。


namespace ShrinkEventBus
{
    public interface IMethodWrapper
    {
        MethodInfo OriginalMethod { get; }
    }

    public class EventHandlerInfo
    {
        public long SubscriptionId { get; }
        public Delegate Handler { get; }
        internal Action<EventBase>? SyncInvoker { get; }
        internal Func<EventBase, UniTask>? AsyncInvoker { get; }
        public EventPriority Priority { get; }
        public int NumericPriority { get; }
        public bool ReceiveCanceled { get; }
        public object? Target { get; }
        public MethodInfo Method { get; }
        public string DebugInfo { get; }
        public Type DeclaringType { get; }
        public string MethodName { get; }
        public MethodInfo? OriginalMethod { get; }
        public string OriginalMethodName { get; }
        public Type? OriginalDeclaringType { get; }
        public DateTime RegisteredAtUtc { get; }

        private EventHandlerInfo(long subscriptionId, Delegate handler, Action<EventBase>? syncInvoker,
            Func<EventBase, UniTask>? asyncInvoker,
            EventPriority priority, int numericPriority, bool receiveCanceled, string debugInfo = "",
            MethodInfo? originalMethod = null)
        {
            SubscriptionId = subscriptionId;
            Handler = handler;
            SyncInvoker = syncInvoker;
            AsyncInvoker = asyncInvoker;
            Priority = priority;
            NumericPriority = numericPriority;
            ReceiveCanceled = receiveCanceled;
            Target = handler.Target;
            Method = handler.Method;
            DebugInfo = debugInfo;
            DeclaringType = Method.DeclaringType ?? typeof(object);
            MethodName = Method.Name;

            OriginalMethod = ExtractOriginalMethodFromWrapper(handler) ?? originalMethod;

            if (OriginalMethod != null)
            {
                OriginalMethodName = OriginalMethod.Name;
                OriginalDeclaringType = OriginalMethod.DeclaringType;
            }
            else
            {
                OriginalMethodName = MethodName;
                OriginalDeclaringType = DeclaringType;
            }

            RegisteredAtUtc = DateTime.UtcNow;
        }

        private static MethodInfo? ExtractOriginalMethodFromWrapper(Delegate handler)
        {
            if (handler.Target is IMethodWrapper wrapper)
            {
                return wrapper.OriginalMethod;
            }

            return null;
        }

        public string DisplayMethodName => OriginalMethodName;
        public Type DisplayDeclaringType => OriginalDeclaringType ?? DeclaringType;

        public bool MatchesMethod(MethodInfo method)
        {
            return method != null && (ReferenceEquals(Method, method) || ReferenceEquals(OriginalMethod, method));
        }

        public bool MatchesDeclaringType(Type declaringType)
        {
            return declaringType != null &&
                   (DeclaringType == declaringType || OriginalDeclaringType == declaringType);
        }

        public static EventHandlerInfo Create(long subscriptionId, Delegate handler, Type eventType,
            EventPriority priority, int numericPriority, bool receiveCanceled, string debugInfo = "",
            MethodInfo? originalMethod = null)
        {
            if (handler == null)
                throw new System.ArgumentNullException(nameof(handler));
            if (eventType == null)
                throw new System.ArgumentNullException(nameof(eventType));

            Action<EventBase>? syncInvoker = null;
            Func<EventBase, UniTask>? asyncInvoker = null;
            if (handler.Method.ReturnType == typeof(void))
                syncInvoker = CreateSyncInvoker(eventType, handler);
            else if (handler.Method.ReturnType == typeof(UniTask))
                asyncInvoker = CreateAsyncInvoker(eventType, handler);
            else
                throw new System.ArgumentException(
                    $"Unsupported event handler return type {handler.Method.ReturnType.FullName} for {handler.Method}.");

            return new EventHandlerInfo(subscriptionId, handler, syncInvoker, asyncInvoker, priority, numericPriority,
                receiveCanceled, debugInfo, originalMethod);
        }

        private static Action<EventBase> CreateSyncInvoker(Type eventType, Delegate handler)
        {
            var factory = typeof(EventHandlerInfo).GetMethod(nameof(CreateSyncInvokerGeneric),
                BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(eventType);
            return (Action<EventBase>)factory.Invoke(null, new object[] { handler })!;
        }

        private static Func<EventBase, UniTask> CreateAsyncInvoker(Type eventType, Delegate handler)
        {
            var factory = typeof(EventHandlerInfo).GetMethod(nameof(CreateAsyncInvokerGeneric),
                BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(eventType);
            return (Func<EventBase, UniTask>)factory.Invoke(null, new object[] { handler })!;
        }

        private static Action<EventBase> CreateSyncInvokerGeneric<TEvent>(Delegate handler) where TEvent : EventBase
        {
            var typedHandler = (Action<TEvent>)handler;
            return eventArgs => typedHandler((TEvent)eventArgs);
        }

        private static Func<EventBase, UniTask> CreateAsyncInvokerGeneric<TEvent>(Delegate handler)
            where TEvent : EventBase
        {
            var typedHandler = (Func<TEvent, UniTask>)handler;
            return eventArgs => typedHandler((TEvent)eventArgs);
        }
    }
}
