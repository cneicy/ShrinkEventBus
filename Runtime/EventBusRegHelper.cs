using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ShrinkEventBus
{
    public static class EventBusRegHelper
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RegStaticEventHandler() => RegisterEventHandlersInternal(null);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RegisterEventHandlers(object target) => RegisterEventHandlersInternal(target);

        private static void RegisterEventHandlersInternal(object target)
        {
            var isStatic = target == null;

            if (isStatic)
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var assembly in assemblies)
                {
                    try
                    {
                        foreach (var type in assembly.GetTypes())
                        {
                            if (type.GetCustomAttributes(typeof(EventBusSubscriberAttribute), false).Length == 0)
                                continue;
                            ScanMethodsAndRegister(null,
                                type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
                        }
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }
            else
            {
                var type = target.GetType();
                if (type.GetCustomAttributes(typeof(EventBusSubscriberAttribute), false).Length == 0) return;
                ScanMethodsAndRegister(target,
                    type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));
            }
        }

        private static void ScanMethodsAndRegister(object target, MethodInfo[] methods)
        {
            foreach (var method in methods)
            {
                if (target != null && method.IsStatic) continue;

                var attributes = method.GetCustomAttributes(typeof(EventSubscribeAttribute), false);
                if (attributes.Length == 0) continue;

                var subscribeAttr = (EventSubscribeAttribute)attributes[0];
                var parameters = method.GetParameters();

                if (parameters.Length != 1) continue;
                var parameterType = parameters[0].ParameterType;
                if (!typeof(EventBase).IsAssignableFrom(parameterType)) continue;

                ProcessMethodRegistration(target, method, subscribeAttr, parameterType);
            }
        }

        private static void ProcessMethodRegistration(object target, MethodInfo method,
            EventSubscribeAttribute subscribeAttr, Type parameterType)
        {
            try
            {
                string scope = target == null ? "Static" : "Instance";
                string typeName = target == null ? method.DeclaringType?.Name : target.GetType().Name;

                if (method.ReturnType == typeof(UniTask))
                {
                    var funcType = typeof(Func<,>).MakeGenericType(parameterType, typeof(UniTask));
                    var handlerDelegate = target == null
                        ? Delegate.CreateDelegate(funcType, method)
                        : Delegate.CreateDelegate(funcType, target, method);
                    EventBus.RegisterEventInternal(parameterType, handlerDelegate, subscribeAttr.Priority,
                        subscribeAttr.NumericPriority, subscribeAttr.ReceiveCanceled,
                        $"{scope} {typeName}.{method.Name} (UniTask)", method);
                }
                else if (method.ReturnType == typeof(Task))
                {
                    var funcType = typeof(Func<,>).MakeGenericType(parameterType, typeof(Task));
                    var handlerDelegate = target == null
                        ? Delegate.CreateDelegate(funcType, method)
                        : Delegate.CreateDelegate(funcType, target, method);
                    var wrappedHandler = WrapTaskHandlerWithMethodInfo(parameterType, handlerDelegate, method);
                    EventBus.RegisterEventInternal(parameterType, wrappedHandler, subscribeAttr.Priority,
                        subscribeAttr.NumericPriority, subscribeAttr.ReceiveCanceled,
                        $"{scope} {typeName}.{method.Name} (Task->UniTask)", method);
                }
                else if (method.ReturnType == typeof(void))
                {
                    var actionType = typeof(Action<>).MakeGenericType(parameterType);
                    var actionDelegate = target == null
                        ? Delegate.CreateDelegate(actionType, method)
                        : Delegate.CreateDelegate(actionType, target, method);
                    EventBus.RegisterEventInternal(parameterType, actionDelegate, subscribeAttr.Priority,
                        subscribeAttr.NumericPriority, subscribeAttr.ReceiveCanceled,
                        $"{scope} {typeName}.{method.Name} (Sync)", method);
                }
            }
            catch (Exception ex)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[EventBus] Registration failed for {method.Name}: {ex.Message}");
#endif
            }
        }

        private static Delegate WrapTaskHandlerWithMethodInfo(Type eventType, Delegate taskHandler,
            MethodInfo originalMethod)
        {
            var method = typeof(EventBusRegHelper)
                .GetMethod(nameof(WrapTaskActionWithMethodInfo), BindingFlags.NonPublic | BindingFlags.Static)
                ?.MakeGenericMethod(eventType);
            return (Delegate)method.Invoke(null, new object[] { taskHandler, originalMethod })!;
        }

        private static Func<T, UniTask> WrapTaskActionWithMethodInfo<T>(Delegate actionDelegate,
            MethodInfo originalMethod)
        {
            var typedAction = (Func<T, Task>)actionDelegate;
            var wrapper = new MethodInfoPreservingTaskWrapper<T>(typedAction, originalMethod);
            return wrapper.ExecuteAsync;
        }

        private class MethodInfoPreservingTaskWrapper<T>
        {
            private readonly Func<T, Task> _originalAction;
            public readonly MethodInfo OriginalMethod;

            public MethodInfoPreservingTaskWrapper(Func<T, Task> originalAction, MethodInfo originalMethod)
            {
                _originalAction = originalAction;
                OriginalMethod = originalMethod;
            }

            public async UniTask ExecuteAsync(T arg)
            {
                try
                {
                    await _originalAction(arg);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    Debug.LogError($"[EventBus] Task {OriginalMethod.Name}: {ex.Message}");
                }
            }
        }
    }
}