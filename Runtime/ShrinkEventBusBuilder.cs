#nullable enable
using System;

namespace ShrinkEventBus
{
    public sealed class ShrinkEventBusBuilder
    {
        internal IShrinkEventExceptionHandler? ExceptionHandler { get; private set; }
        internal ShrinkEventExceptionHandlingMode ExceptionHandlingMode { get; private set; } =
            ShrinkEventExceptionHandlingMode.LogAndContinue;
        internal Action<Type>? EventClassChecker { get; private set; }
        internal bool StartShutdownEnabled { get; private set; }
        internal bool CheckTypesOnDispatchEnabled { get; private set; }
        internal bool AllowPerPhaseDispatchEnabled { get; private set; }

        public ShrinkEventBusBuilder SetExceptionHandler(IShrinkEventExceptionHandler handler)
        {
            ExceptionHandler = handler ?? throw new ArgumentNullException(nameof(handler));
            return this;
        }

        public ShrinkEventBusBuilder SetExceptionHandlingMode(ShrinkEventExceptionHandlingMode mode)
        {
            ExceptionHandlingMode = mode;
            return this;
        }

        public ShrinkEventBusBuilder StartShutdown()
        {
            StartShutdownEnabled = true;
            return this;
        }

        public ShrinkEventBusBuilder CheckTypesOnDispatch()
        {
            CheckTypesOnDispatchEnabled = true;
            return this;
        }

        public ShrinkEventBusBuilder AllowPerPhaseDispatch()
        {
            AllowPerPhaseDispatchEnabled = true;
            return this;
        }

        public ShrinkEventBusBuilder ClassChecker(Action<Type> checker)
        {
            if (checker == null)
                throw new ArgumentNullException(nameof(checker));

            EventClassChecker = EventClassChecker == null
                ? checker
                : EventClassChecker + checker;
            return this;
        }

        public ShrinkEventBusBuilder MarkerInterface<TMarker>() where TMarker : class
        {
            var markerType = typeof(TMarker);
            if (!markerType.IsInterface)
                throw new InvalidOperationException($"Marker type {markerType.FullName} must be an interface.");

            return ClassChecker(eventType =>
            {
                if (!markerType.IsAssignableFrom(eventType))
                    throw new ArgumentException(
                        $"This bus only accepts events assignable to {markerType.FullName}, but got {eventType.FullName}.");
            });
        }

        public IShrinkEventBus Build()
        {
            return new ShrinkEventBusInstance(this);
        }
    }
}
