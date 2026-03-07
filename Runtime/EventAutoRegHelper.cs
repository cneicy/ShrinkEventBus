using System;
using System.Collections.Concurrent;
using System.Reflection;
using UnityEngine;

namespace ShrinkEventBus
{
    public static class EventAutoRegHelper
    {
        private static readonly ConcurrentDictionary<Type, bool> SubscriberTypes = new();
        private static readonly object InitLock = new();
        public static bool IsInitialized { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void PreInitializeReflection()
        {
            if (SubscriberTypes.Count == 0) ScanEventBusSubscribers();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InitializeAfterSceneLoad()
        {
            EnsureInitialized();
        }

        public static void EnsureInitialized()
        {
            if (IsInitialized) return;
            lock (InitLock)
            {
                if (IsInitialized) return;
                ScanEventBusSubscribers();
                ScanExistingObjects();
                IsInitialized = true;
            }
        }

        private static void ScanExistingObjects()
        {
            if (SubscriberTypes.Count == 0)
            {
#if UNITY_2023_1_OR_NEWER
                var allMonoBehaviours =
                    UnityEngine.Object.FindObjectsByType<MonoBehaviour>(UnityEngine.FindObjectsInactive.Include,
                        UnityEngine.FindObjectsSortMode.None);
#else
                var allMonoBehaviours = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true);
#endif
                foreach (var mb in allMonoBehaviours) ProcessNodeRegistration(mb);
                return;
            }

            foreach (var targetType in SubscriberTypes.Keys)
            {
#if UNITY_2023_1_OR_NEWER
                var objects = UnityEngine.Object.FindObjectsByType(targetType, UnityEngine.FindObjectsInactive.Include,
                    UnityEngine.FindObjectsSortMode.None);
#else
                var objects = UnityEngine.Object.FindObjectsOfType(targetType, true);
#endif
                foreach (var obj in objects) ProcessNodeRegistration(obj);
            }
        }

        private static void ScanEventBusSubscribers()
        {
            if (SubscriberTypes.Count > 0) return;
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                if (IsSystemAssembly(assembly)) continue;
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.GetCustomAttribute<EventBusSubscriberAttribute>() != null &&
                            typeof(MonoBehaviour).IsAssignableFrom(type))
                            SubscriberTypes.TryAdd(type, true);
                    }
                }
                catch (ReflectionTypeLoadException ex)
                {
                    foreach (var type in ex.Types)
                    {
                        if (type?.GetCustomAttribute<EventBusSubscriberAttribute>() != null &&
                            typeof(MonoBehaviour).IsAssignableFrom(type))
                            SubscriberTypes.TryAdd(type, true);
                    }
                }
                catch
                {
                    // ignored
                }
            }
        }

        private static bool IsSystemAssembly(Assembly assembly)
        {
            var name = assembly.FullName ?? "";
            return name.StartsWith("System.") || name.StartsWith("Microsoft.") || name.StartsWith("mscorlib") ||
                   name.StartsWith("UnityEngine.") || name.StartsWith("UnityEditor.");
        }

        private static bool ProcessNodeRegistration(object node)
        {
            if (node == null) return false;
            var nodeType = node.GetType();

            if (SubscriberTypes.Count == 0 && nodeType.GetCustomAttribute<EventBusSubscriberAttribute>() != null)
                SubscriberTypes.TryAdd(nodeType, true);

            if (!SubscriberTypes.ContainsKey(nodeType)) return false;
            if (EventBus.IsInstanceRegistered(node)) return false;

            EventBus.AutoRegister(node);
            return true;
        }

        public static void UnregisterNode(object node)
        {
            if (node == null) return;
            EventBus.UnregisterInstance(node);
        }

        public static void ForceScanCurrentScene() => ScanExistingObjects();

        public static void Cleanup()
        {
            lock (InitLock)
            {
                SubscriberTypes.Clear();
                IsInitialized = false;
            }
        }
    }
}