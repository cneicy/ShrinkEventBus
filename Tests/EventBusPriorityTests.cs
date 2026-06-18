using System.Collections.Generic;
using NUnit.Framework;

namespace ShrinkEventBus.Tests
{
    [TestFixture]
    public sealed class EventBusPriorityTests
    {
        private class BaseEvent : EventBase
        {
        }

        private sealed class DerivedEvent : BaseEvent
        {
        }

        [Test]
        public void EnumPriority_ExecutesPhasesInOrder()
        {
            var bus = EventBus.CreateBus();
            var order = new List<string>();

            bus.RegisterEvent<BaseEvent>(_ => order.Add("LOW"), EventPriority.LOW);
            bus.RegisterEvent<BaseEvent>(_ => order.Add("MONITOR"), EventPriority.MONITOR);
            bus.RegisterEvent<BaseEvent>(_ => order.Add("HIGHEST"), EventPriority.HIGHEST);
            bus.RegisterEvent<BaseEvent>(_ => order.Add("NORMAL"), EventPriority.NORMAL);

            bus.TriggerEvent(new BaseEvent());

            CollectionAssert.AreEqual(new[] { "HIGHEST", "NORMAL", "LOW", "MONITOR" }, order);
        }

        [Test]
        public void NumericPriority_HigherNumberRunsFirstWithinPhase()
        {
            var bus = EventBus.CreateBus();
            var order = new List<string>();

            // 5 与 10 都映射到 NORMAL 档
            bus.RegisterEvent<BaseEvent>(_ => order.Add("p5"), 5);
            bus.RegisterEvent<BaseEvent>(_ => order.Add("p10"), 10);

            bus.TriggerEvent(new BaseEvent());

            CollectionAssert.AreEqual(new[] { "p10", "p5" }, order);
        }

        [Test]
        public void NumericPriority_TieKeepsRegistrationOrder()
        {
            var bus = EventBus.CreateBus();
            var order = new List<string>();

            bus.RegisterEvent<BaseEvent>(_ => order.Add("first"), 0);
            bus.RegisterEvent<BaseEvent>(_ => order.Add("second"), 0);

            bus.TriggerEvent(new BaseEvent());

            CollectionAssert.AreEqual(new[] { "first", "second" }, order);
        }

        [Test]
        public void NumericPriorityZero_MapsToNormalPhase()
        {
            var bus = EventBus.CreateBus();
            bus.RegisterEvent<BaseEvent>(_ => { }, 0);

            var subscribers = bus.GetEventSubscribers<BaseEvent>();

            Assert.AreEqual(1, subscribers.Length);
            Assert.AreEqual(EventPriority.NORMAL, subscribers[0].Priority);
        }

        [Test]
        public void RegisterEvent_WithoutPriority_ResolvesToNormal()
        {
            var bus = EventBus.CreateBus();

            void Handler(BaseEvent evt)
            {
            }

            // 不带优先级的调用应唯一解析到枚举重载（NORMAL），不再有重载二义性
            bus.RegisterEvent<BaseEvent>(Handler);

            var subscribers = bus.GetEventSubscribers<BaseEvent>();
            Assert.AreEqual(1, subscribers.Length);
            Assert.AreEqual(EventPriority.NORMAL, subscribers[0].Priority);
        }

        [Test]
        public void ParentAndChildHandlers_MergeByNumericPriorityWithinPhase()
        {
            var bus = EventBus.CreateBus();
            var order = new List<string>();

            bus.RegisterEvent<BaseEvent>(_ => order.Add("parent10"), 10);
            bus.RegisterEvent<DerivedEvent>(_ => order.Add("child5"), 5);
            bus.RegisterEvent<DerivedEvent>(_ => order.Add("child1"), 1);

            bus.TriggerEvent(new DerivedEvent());

            CollectionAssert.AreEqual(new[] { "parent10", "child5", "child1" }, order);
        }

        [Test]
        public void ParentAndChildHandlers_TiePrefersChild()
        {
            var bus = EventBus.CreateBus();
            var order = new List<string>();

            bus.RegisterEvent<BaseEvent>(_ => order.Add("parent0"), 0);
            bus.RegisterEvent<DerivedEvent>(_ => order.Add("child0"), 0);

            bus.TriggerEvent(new DerivedEvent());

            CollectionAssert.AreEqual(new[] { "child0", "parent0" }, order);
        }
    }
}
