using System;

namespace ShrinkEventBus
{
    public class UnsupportedOperationException : InvalidOperationException
    {
        public UnsupportedOperationException() : base()
        {
        }

        public UnsupportedOperationException(string message) : base(message)
        {
        }

        public UnsupportedOperationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}