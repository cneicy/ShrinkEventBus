namespace ShrinkEventBus
{
    public enum EventPriority
    {
        HIGHEST = 0,
        HIGH = 1,
        NORMAL = 2,
        LOW = 3,
        LOWEST = 4,
        MONITOR = 5
    }

    internal static class PriorityHelper
    {
        public static EventPriority ConvertToEventPriority(int numericPriority)
        {
            return numericPriority switch
            {
                >= 100 => EventPriority.HIGHEST,
                >= 50 => EventPriority.HIGH,
                > 0 => EventPriority.NORMAL,
                >= -50 => EventPriority.LOW,
                _ => EventPriority.LOWEST
            };
        }
    }
}