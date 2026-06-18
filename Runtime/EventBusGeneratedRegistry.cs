#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace ShrinkEventBus
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class EventBusStaticRegistryAttribute : Attribute
    {
        public EventBusStaticRegistryAttribute(params Type[] subscriberTypes)
        {
            SubscriberTypes = subscriberTypes ?? Array.Empty<Type>();
        }

        public Type[] SubscriberTypes { get; }
    }

    internal static class EventBusGeneratedRegistry
    {
        public static IReadOnlyList<Type> GetStaticSubscriberTypes()
        {
            var types = new List<Type>();
            var seen = new HashSet<Type>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                object[] attributes;
                try
                {
                    attributes = assembly.GetCustomAttributes(typeof(EventBusStaticRegistryAttribute), false);
                }
                catch
                {
                    continue;
                }

                foreach (var attribute in attributes.OfType<EventBusStaticRegistryAttribute>())
                {
                    foreach (var subscriberType in attribute.SubscriberTypes ?? Array.Empty<Type>())
                    {
                        if (subscriberType == null || !seen.Add(subscriberType))
                            continue;

                        types.Add(subscriberType);
                    }
                }
            }

            return types;
        }
    }
}
