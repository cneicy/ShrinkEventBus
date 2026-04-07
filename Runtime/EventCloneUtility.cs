using System;
using System.Collections.Generic;
using System.Reflection;

namespace ShrinkEventBus
{
    internal static class EventCloneUtility
    {
        private static readonly object CacheLock = new();
        private static readonly Dictionary<Type, FieldInfo[]> FieldCache = new();

        public static TEvent CloneForDetachedDispatch<TEvent>(TEvent source) where TEvent : EventBase
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (Activator.CreateInstance(source.GetType()) is not TEvent clone)
                throw new InvalidOperationException(
                    $"Cannot clone event type {source.GetType().FullName}. A public parameterless constructor is required.");

            CopyFields(source, clone);
            CopyListenerSnapshot(source, clone);
            clone.ReleaseAction = null;
            clone.IsInPool = false;
            return clone;
        }

        private static void CopyFields(EventBase source, EventBase target)
        {
            var fields = GetCopyableFields(source.GetType());
            for (var i = 0; i < fields.Length; i++)
                fields[i].SetValue(target, fields[i].GetValue(source));
        }

        private static void CopyListenerSnapshot(EventBase source, EventBase target)
        {
            var targetListeners = target.GetListenerList();
            targetListeners.Clear();

            var sourceHandlers = source.GetListenerList().GetHandlers();
            for (var i = 0; i < sourceHandlers.Length; i++)
                targetListeners.Add(sourceHandlers[i]);
        }

        private static FieldInfo[] GetCopyableFields(Type type)
        {
            lock (CacheLock)
            {
                if (FieldCache.TryGetValue(type, out var cached))
                    return cached;

                var fields = new List<FieldInfo>();
                var currentType = type;
                while (currentType != null && currentType != typeof(object))
                {
                    var declaredFields = currentType.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                               BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    for (var i = 0; i < declaredFields.Length; i++)
                    {
                        var field = declaredFields[i];
                        if (field.IsStatic)
                            continue;
                        if (ShouldSkipField(field))
                            continue;

                        fields.Add(field);
                    }

                    currentType = currentType.BaseType;
                }

                cached = fields.ToArray();
                FieldCache[type] = cached;
                return cached;
            }
        }

        private static bool ShouldSkipField(FieldInfo field)
        {
            return field.Name is "_listenerList"
                or "<ReleaseAction>k__BackingField"
                or "<IsInPool>k__BackingField";
        }
    }
}
