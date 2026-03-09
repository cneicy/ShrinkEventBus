using System;
using System.Reflection;
using System.Runtime.CompilerServices;
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
                else
                {
                    Debug.LogWarning(
                        $"[EventBus] Registration failed: {typeName}.{method.Name} return type must be void or UniTask.");
                }
            }
            catch (Exception ex)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[EventBus] Registration failed for {method.Name}: {ex.Message}");
#endif
            }
        }
    }
}