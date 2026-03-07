using System;
using System.Reflection;

#pragma warning disable CS8632 // 只能在 "#nullable" 注释上下文内的代码中使用可为 null 的引用类型的注释。

namespace ShrinkEventBus
{
    public class EventHandlerInfo
    {
        public Delegate Handler { get; }
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

        public EventHandlerInfo(Delegate handler, EventPriority priority, int numericPriority,
            bool receiveCanceled, string debugInfo = "", MethodInfo? originalMethod = null)
        {
            Handler = handler;
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
        }

        private static MethodInfo? ExtractOriginalMethodFromWrapper(Delegate handler)
        {
            if (handler.Target == null) return null;

            var targetType = handler.Target.GetType();
            if (!targetType.Name.Contains("MethodInfoPreserving")) return null;

            try
            {
                var field = targetType.GetField("OriginalMethod",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null) return field.GetValue(handler.Target) as MethodInfo;

                var prop = targetType.GetProperty("OriginalMethod",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null) return prop.GetValue(handler.Target) as MethodInfo;
            }
            catch
            {
                // ignored
            }

            return null;
        }

        public string DisplayMethodName => OriginalMethodName;
        public Type DisplayDeclaringType => OriginalDeclaringType ?? DeclaringType;
    }
}