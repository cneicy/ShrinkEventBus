namespace ShrinkEventBus
{
    internal static class EventCache<TEvent> where TEvent : EventBase
    {
        public static ListenerList List;
    }
}