using NUnit.Framework;
using UnityEngine;

namespace ShrinkEventBus.Tests
{
    [TestFixture]
    public sealed class EventBusSubscriptionTests
    {
        private sealed class SampleEvent : EventBase
        {
            public int Value { get; set; }
        }

        [SetUp]
        public void SetUp()
        {
            EventBus.UnregisterAllEvents();
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.UnregisterAllEvents();
        }

        [Test]
        public void SubscribeEvent_Dispose_RemovesOnlyOwnedSubscription()
        {
            var firstHits = 0;
            var secondHits = 0;

            using var first = EventBus.SubscribeEvent<SampleEvent>(_ => firstHits++);
            using var second = EventBus.SubscribeEvent<SampleEvent>(_ => secondHits++);

            EventBus.TriggerEvent(new SampleEvent());
            Assert.AreEqual(1, firstHits);
            Assert.AreEqual(1, secondHits);

            first.Dispose();

            EventBus.TriggerEvent(new SampleEvent());
            Assert.AreEqual(1, firstHits);
            Assert.AreEqual(2, secondHits);
        }

        [Test]
        public void ActiveSubscriptionSnapshot_ContainsExpectedMetadata()
        {
            using var subscription = EventBus.SubscribeEvent<SampleEvent>(_ => { }, EventPriority.HIGH, true);

            var snapshots = EventBus.GetActiveSubscriptionsSnapshot();

            Assert.AreEqual(1, snapshots.Count);
            Assert.AreEqual(subscription.SubscriptionId, snapshots[0].SubscriptionId);
            Assert.AreEqual(typeof(SampleEvent), snapshots[0].EventType);
            Assert.AreEqual(EventPriority.HIGH, snapshots[0].Priority);
            Assert.IsTrue(snapshots[0].ReceiveCanceled);
            Assert.IsFalse(string.IsNullOrWhiteSpace(snapshots[0].MethodName));
        }

        [Test]
        public void RegisterEvent_StillWorksWithoutDisposableSubscription()
        {
            var hits = 0;

            void Handler(SampleEvent evt)
            {
                hits += evt.Value;
            }

            EventBus.RegisterEvent<SampleEvent>(Handler);
            EventBus.TriggerEvent(new SampleEvent { Value = 3 });

            Assert.AreEqual(3, hits);

            EventBus.UnregisterEvent<SampleEvent>(Handler);
            EventBus.TriggerEvent(new SampleEvent { Value = 5 });

            Assert.AreEqual(3, hits);
        }

        [Test]
        public void ActiveSubscriptionSnapshot_EmptyAfterDispose()
        {
            var subscription = EventBus.SubscribeEvent<SampleEvent>(_ => Debug.Log("noop"));
            subscription.Dispose();

            var snapshots = EventBus.GetActiveSubscriptionsSnapshot();

            Assert.AreEqual(0, snapshots.Count);
            Assert.IsTrue(subscription.IsDisposed);
        }
    }
}
