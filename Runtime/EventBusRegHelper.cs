#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ShrinkEventBus
{
    internal static class EventBusRegHelper
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RegStaticEventHandler(ShrinkEventBusInstance bus)
        {
            if (bus == null)
                throw new ArgumentNullException(nameof(bus));

            RegisterStaticSubscriberTypes(bus, EventBusGeneratedRegistry.GetStaticSubscriberTypes());
        }

        public static void RegisterStaticSubscriberTypes(ShrinkEventBusInstance bus, IEnumerable<Type> subscriberTypes)
        {
            if (bus == null)
                throw new ArgumentNullException(nameof(bus));
            if (subscriberTypes == null)
                throw new ArgumentNullException(nameof(subscriberTypes));

            foreach (var type in subscriberTypes)
            {
                try
                {
                    if (type == null || type.GetCustomAttributes(typeof(EventBusSubscriberAttribute), false).Length == 0)
                        continue;

                    RegisterTarget(bus, type, requireSubscriberAttribute: true, lenientWhenNoMethods: true);
                }
                catch (Exception ex)
                {
#if UNITY_EDITOR
                    Debug.LogWarning($"[EventBus] Static registration failed for {type?.FullName}: {ex.Message}");
#endif
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RegisterTarget(ShrinkEventBusInstance bus, object target, bool requireSubscriberAttribute,
            bool lenientWhenNoMethods = false)
        {
            if (bus == null)
                throw new ArgumentNullException(nameof(bus));
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            switch (target)
            {
                case MethodInfo method:
                    RegisterMethod(bus, null, method, requireSubscriberAttribute: false, isStaticRegistration: true);
                    return;
                case Type staticType:
                    RegisterDeclaredMethods(bus, null, staticType, requireSubscriberAttribute,
                        isStaticRegistration: true, lenientWhenNoMethods);
                    return;
                default:
                    RegisterDeclaredMethods(bus, target, target.GetType(), requireSubscriberAttribute,
                        isStaticRegistration: false, lenientWhenNoMethods);
                    return;
            }
        }

        private static void RegisterDeclaredMethods(ShrinkEventBusInstance bus, object? target, Type type,
            bool requireSubscriberAttribute, bool isStaticRegistration, bool lenientWhenNoMethods)
        {
            if (requireSubscriberAttribute &&
                type.GetCustomAttributes(typeof(EventBusSubscriberAttribute), false).Length == 0)
            {
                throw new InvalidOperationException(
                    $"Type {type.FullName} must declare [EventBusSubscriber] before it can be auto-registered.");
            }

            var flags = (isStaticRegistration ? BindingFlags.Static : BindingFlags.Instance) |
                        BindingFlags.Public | BindingFlags.NonPublic;
            var methods = type.GetMethods(flags);
            var foundMethods = 0;
            foreach (var method in methods)
            {
                var hasSubscribeAttribute = method.GetCustomAttributes(typeof(EventSubscribeAttribute), false).Length > 0;
                if (!hasSubscribeAttribute)
                    continue;

                if (method.IsStatic != isStaticRegistration)
                {
                    var expected = isStaticRegistration ? "static" : "instance";
                    throw new InvalidOperationException(
                        $"Method {method} is annotated with [EventSubscribe] but does not match the expected {expected} registration mode.");
                }

                RegisterMethod(bus, target, method, requireSubscriberAttribute: false, isStaticRegistration);
                foundMethods++;
            }

            if (foundMethods == 0)
            {
                var message =
                    $"Type {type.FullName} has no [EventSubscribe] methods for {(isStaticRegistration ? "static" : "instance")} registration.";
                if (lenientWhenNoMethods)
                {
                    Debug.LogWarning($"[EventBus] {message} Auto-registration skipped.");
                    return;
                }

                throw new InvalidOperationException(message);
            }
        }

        private static void RegisterMethod(ShrinkEventBusInstance bus, object? target, MethodInfo method,
            bool requireSubscriberAttribute, bool isStaticRegistration)
        {
            if (method == null)
                throw new ArgumentNullException(nameof(method));

            if (!method.IsDefined(typeof(EventSubscribeAttribute), false))
                throw new InvalidOperationException($"Method {method} is not annotated with [EventSubscribe].");

            if (method.IsStatic != isStaticRegistration)
            {
                var expected = isStaticRegistration ? "static" : "instance";
                throw new InvalidOperationException(
                    $"Method {method} is annotated with [EventSubscribe] but does not match the expected {expected} registration mode.");
            }

            if (requireSubscriberAttribute && method.DeclaringType != null &&
                method.DeclaringType.GetCustomAttributes(typeof(EventBusSubscriberAttribute), false).Length == 0)
            {
                throw new InvalidOperationException(
                    $"Type {method.DeclaringType.FullName} must declare [EventBusSubscriber] before it can be auto-registered.");
            }

            var subscribeAttr = (EventSubscribeAttribute)method.GetCustomAttributes(typeof(EventSubscribeAttribute), false)[0];
            var parameters = method.GetParameters();
            if (parameters.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Method {method} has [EventSubscribe] but declares {parameters.Length} parameters. Event handlers must declare exactly one EventBase parameter.");
            }

            var parameterType = parameters[0].ParameterType;
            if (!typeof(EventBase).IsAssignableFrom(parameterType))
            {
                throw new InvalidOperationException(
                    $"Method {method} has [EventSubscribe] but parameter {parameterType.FullName} does not inherit from EventBase.");
            }

            ProcessMethodRegistration(bus, target, method, subscribeAttr, parameterType);
        }

        private static void ProcessMethodRegistration(ShrinkEventBusInstance bus, object? target, MethodInfo method,
            EventSubscribeAttribute subscribeAttr, Type parameterType)
        {
            string scope = target == null ? "Static" : "Instance";
            string typeName = target == null ? method.DeclaringType?.Name ?? "Unknown" : target.GetType().Name;

            if (method.ReturnType == typeof(UniTask))
            {
                var funcType = typeof(Func<,>).MakeGenericType(parameterType, typeof(UniTask));
                var handlerDelegate = target == null
                    ? Delegate.CreateDelegate(funcType, method)
                    : Delegate.CreateDelegate(funcType, target, method);
                bus.RegisterEventInternal(parameterType, handlerDelegate, subscribeAttr.Priority,
                    subscribeAttr.NumericPriority, subscribeAttr.ReceiveCanceled,
                    $"{scope} {typeName}.{method.Name} (UniTask)", method);
                return;
            }

            if (method.ReturnType == typeof(void))
            {
                var actionType = typeof(Action<>).MakeGenericType(parameterType);
                var actionDelegate = target == null
                    ? Delegate.CreateDelegate(actionType, method)
                    : Delegate.CreateDelegate(actionType, target, method);
                bus.RegisterEventInternal(parameterType, actionDelegate, subscribeAttr.Priority,
                    subscribeAttr.NumericPriority, subscribeAttr.ReceiveCanceled,
                    $"{scope} {typeName}.{method.Name} (Sync)", method);
                return;
            }

            throw new InvalidOperationException(
                $"Method {method} has [EventSubscribe] but return type {method.ReturnType.FullName} is unsupported. Use void or UniTask.");
        }
    }
}
