using System;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ShrinkEventBus.Tests
{
    [TestFixture]
    public sealed class EventBusRegistrationTests
    {
        private class ProbeEvent : EventBase
        {
        }

        private sealed class PooledEvent : EventBase
        {
            public int Value { get; set; }

            protected override void OnReset()
            {
                Value = 0;
            }
        }

        private sealed class InstanceSubscriber
        {
            public int Hits;

            [EventSubscribe(EventPriority.NORMAL)]
            private void OnProbe(ProbeEvent evt)
            {
                Hits++;
            }
        }

        private static class StaticSubscriber
        {
            public static int Hits;

            [EventSubscribe(EventPriority.NORMAL)]
            private static void OnProbe(ProbeEvent evt)
            {
                Hits++;
            }
        }

        private sealed class InvalidSignatureSubscriber
        {
            [EventSubscribe(EventPriority.NORMAL)]
            private void OnProbe(ProbeEvent evt, int extra)
            {
            }
        }

        [EventBusSubscriber]
        private sealed class EmptySubscriber
        {
        }

        private sealed class UnattributedSubscriber
        {
            [EventSubscribe(EventPriority.NORMAL)]
            private void OnProbe(ProbeEvent evt)
            {
            }
        }

        [Test]
        public void Register_InstanceScan_RegistersAndUnregisters()
        {
            var bus = EventBus.CreateBus();
            var subscriber = new InstanceSubscriber();

            bus.Register(subscriber);
            Assert.IsTrue(bus.IsInstanceRegistered(subscriber));
            bus.TriggerEvent(new ProbeEvent());
            Assert.AreEqual(1, subscriber.Hits);

            bus.Unregister(subscriber);
            Assert.IsFalse(bus.IsInstanceRegistered(subscriber));
            bus.TriggerEvent(new ProbeEvent());
            Assert.AreEqual(1, subscriber.Hits);
        }

        [Test]
        public void Register_StaticTypeScan_RegistersAndUnregisters()
        {
            var bus = EventBus.CreateBus();
            StaticSubscriber.Hits = 0;

            bus.Register(typeof(StaticSubscriber));
            bus.TriggerEvent(new ProbeEvent());
            Assert.AreEqual(1, StaticSubscriber.Hits);

            bus.Unregister(typeof(StaticSubscriber));
            bus.TriggerEvent(new ProbeEvent());
            Assert.AreEqual(1, StaticSubscriber.Hits);
        }

        [Test]
        public void Register_MethodInfo_RegistersSingleHandler()
        {
            var bus = EventBus.CreateBus();
            StaticSubscriber.Hits = 0;
            var method = typeof(StaticSubscriber).GetMethod("OnProbe",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method);

            bus.Register(method);
            bus.TriggerEvent(new ProbeEvent());
            Assert.AreEqual(1, StaticSubscriber.Hits);

            bus.Unregister(method);
            bus.TriggerEvent(new ProbeEvent());
            Assert.AreEqual(1, StaticSubscriber.Hits);
        }

        [Test]
        public void Register_InvalidHandlerSignature_Throws()
        {
            var bus = EventBus.CreateBus();

            Assert.Throws<InvalidOperationException>(() => bus.Register(new InvalidSignatureSubscriber()));
        }

        [Test]
        public void AutoRegister_TypeWithoutSubscriberAttribute_Throws()
        {
            var bus = EventBus.CreateBus();

            Assert.Throws<InvalidOperationException>(() => bus.AutoRegister(new UnattributedSubscriber()));
        }

        [Test]
        public void AutoRegister_SubscriberWithoutHandlers_WarnsInsteadOfThrowing()
        {
            var bus = EventBus.CreateBus();

            LogAssert.Expect(LogType.Warning, new Regex(@"has no \[EventSubscribe\] methods"));
            Assert.DoesNotThrow(() => bus.AutoRegister(new EmptySubscriber()));
        }

        [Test]
        public void GetEventSubscribers_ReturnsDefensiveCopy()
        {
            var bus = EventBus.CreateBus();
            bus.RegisterEvent<ProbeEvent>(_ => { }, EventPriority.NORMAL);

            var first = bus.GetEventSubscribers<ProbeEvent>();
            Assert.AreEqual(1, first.Length);

            first[0] = null;
            var second = bus.GetEventSubscribers<ProbeEvent>();
            Assert.IsNotNull(second[0]);
        }

        [Test]
        public void UnregisterAllEventsForObject_RemovesScannedHandlers()
        {
            var bus = EventBus.CreateBus();
            var subscriber = new InstanceSubscriber();
            bus.Register(subscriber);

            bus.UnregisterAllEventsForObject(subscriber);
            bus.TriggerEvent(new ProbeEvent());

            Assert.AreEqual(0, subscriber.Hits);
        }

        [Test]
        public void EventPool_ReleaseResetsAndReusesInstance()
        {
            var evt = EventPool<PooledEvent>.Get();
            evt.Value = 42;

            EventPool<PooledEvent>.Release(evt);
            var reused = EventPool<PooledEvent>.Get();

            Assert.AreSame(evt, reused);
            Assert.AreEqual(0, reused.Value);

            EventPool<PooledEvent>.Release(reused);
        }

        [Test]
        public void EventPool_DoubleRelease_DoesNotDuplicatePoolEntry()
        {
            var evt = EventPool<PooledEvent>.Get();

            EventPool<PooledEvent>.Release(evt);
            EventPool<PooledEvent>.Release(evt);

            var first = EventPool<PooledEvent>.Get();
            var second = EventPool<PooledEvent>.Get();

            Assert.AreSame(evt, first);
            Assert.AreNotSame(evt, second);

            EventPool<PooledEvent>.Release(first);
            EventPool<PooledEvent>.Release(second);
        }

        [Test]
        public void EventPool_DisposeReturnsToPool()
        {
            PooledEvent captured;
            using (var evt = EventPool<PooledEvent>.Get())
            {
                captured = evt;
                evt.Value = 7;
            }

            var reused = EventPool<PooledEvent>.Get();
            Assert.AreSame(captured, reused);
            Assert.AreEqual(0, reused.Value);

            EventPool<PooledEvent>.Release(reused);
        }
    }
}
