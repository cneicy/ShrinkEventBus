using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace ShrinkEventBus.Tests
{
    [TestFixture]
    public sealed class EventBusDispatchTests
    {
        private class PlainEvent : EventBase
        {
        }

        private sealed class DerivedPlainEvent : PlainEvent
        {
        }

        [Cancelable]
        private sealed class CancelableEvent : EventBase
        {
        }

        [HasResult]
        private sealed class ResultEvent : EventBase
        {
        }

        [Test]
        public void TriggerEvent_ReturnsFalseWithoutSubscribers_TrueWhenHandled()
        {
            var bus = EventBus.CreateBus();

            Assert.IsFalse(bus.TriggerEvent(new PlainEvent()));

            bus.RegisterEvent<PlainEvent>(_ => { }, EventPriority.NORMAL);
            Assert.IsTrue(bus.TriggerEvent(new PlainEvent()));
        }

        [Test]
        public void CanceledEvent_SkipsHandlersUnlessReceiveCanceled()
        {
            var bus = EventBus.CreateBus();
            var order = new List<string>();

            bus.RegisterEvent<CancelableEvent>(evt =>
            {
                order.Add("canceler");
                evt.SetCanceled(true);
            }, EventPriority.HIGHEST);
            bus.RegisterEvent<CancelableEvent>(_ => order.Add("skipped"), EventPriority.NORMAL);
            bus.RegisterEvent<CancelableEvent>(evt => order.Add($"monitor:{evt.IsCanceled}"),
                EventPriority.MONITOR, receiveCanceled: true);

            var evt = new CancelableEvent();
            bus.TriggerEvent(evt);

            Assert.IsTrue(evt.IsCanceled);
            CollectionAssert.AreEqual(new[] { "canceler", "monitor:True" }, order);
        }

        [Test]
        public void SetCanceled_OnNonCancelableEvent_ThrowsWithMessage()
        {
            var evt = new PlainEvent();

            var ex = Assert.Throws<UnsupportedOperationException>(() => evt.SetCanceled(true));
            Assert.IsFalse(string.IsNullOrWhiteSpace(ex.Message));
        }

        [Test]
        public void SetResult_OnEventWithoutResult_ThrowsWithMessage()
        {
            var evt = new PlainEvent();

            var ex = Assert.Throws<InvalidOperationException>(() => evt.SetResult(EventResult.ALLOW));
            Assert.IsFalse(string.IsNullOrWhiteSpace(ex.Message));
        }

        [Test]
        public void SetResult_OnResultEvent_PersistsResult()
        {
            var bus = EventBus.CreateBus();
            bus.RegisterEvent<ResultEvent>(evt => evt.SetResult(EventResult.DENY), EventPriority.HIGH);

            var evt = new ResultEvent();
            bus.TriggerEvent(evt);

            Assert.AreEqual(EventResult.DENY, evt.Result);
        }

        [Test]
        public void ParentSubscriber_ReceivesDerivedEvent()
        {
            var bus = EventBus.CreateBus();
            var hits = 0;
            bus.RegisterEvent<PlainEvent>(_ => hits++, EventPriority.NORMAL);

            bus.TriggerEvent(new DerivedPlainEvent());

            Assert.AreEqual(1, hits);
        }

        [Test]
        public void ParentSubscriber_RegisteredAfterChildWasTriggered_StillReceivesChild()
        {
            var bus = EventBus.CreateBus();

            // 先触发子事件，让子类型的监听列表先于父监听器创建（覆盖脏传播路径）
            bus.TriggerEvent(new DerivedPlainEvent());

            var hits = 0;
            bus.RegisterEvent<PlainEvent>(_ => hits++, EventPriority.NORMAL);
            bus.TriggerEvent(new DerivedPlainEvent());

            Assert.AreEqual(1, hits);
        }

        [Test]
        public void PerPhaseDispatch_OnlyRunsRequestedPhase()
        {
            var bus = EventBus.CreateBus(builder => builder.AllowPerPhaseDispatch());
            var order = new List<string>();

            bus.RegisterEvent<PlainEvent>(_ => order.Add("high"), EventPriority.HIGH);
            bus.RegisterEvent<PlainEvent>(_ => order.Add("normal"), EventPriority.NORMAL);

            bus.TriggerEvent(EventPriority.HIGH, new PlainEvent());

            CollectionAssert.AreEqual(new[] { "high" }, order);
        }

        [Test]
        public void PerPhaseDispatch_ThrowsWhenNotEnabled()
        {
            var bus = EventBus.CreateBus();

            Assert.Throws<InvalidOperationException>(() => bus.TriggerEvent(EventPriority.HIGH, new PlainEvent()));
        }

        [Test]
        public void TriggerEventAsync_RunsHandlersSequentiallyByPriority()
        {
            var bus = EventBus.CreateBus();
            var order = new List<string>();

            async UniTask AsyncHandler(PlainEvent evt)
            {
                order.Add("async-high");
                await UniTask.CompletedTask;
            }

            bus.RegisterEvent<PlainEvent>(AsyncHandler, EventPriority.HIGH, receiveCanceled: false);
            bus.RegisterEvent<PlainEvent>(_ => order.Add("sync-normal"), EventPriority.NORMAL);

            var handled = bus.TriggerEventAsync(new PlainEvent()).GetAwaiter().GetResult();

            Assert.IsTrue(handled);
            CollectionAssert.AreEqual(new[] { "async-high", "sync-normal" }, order);
        }

        [Test]
        public void TriggerEventAsync_AsyncHandlerCancellation_SkipsLaterHandlers()
        {
            var bus = EventBus.CreateBus();
            var order = new List<string>();

            async UniTask CancelingHandler(CancelableEvent evt)
            {
                evt.SetCanceled(true);
                order.Add("async-canceler");
                await UniTask.CompletedTask;
            }

            bus.RegisterEvent<CancelableEvent>(CancelingHandler, EventPriority.HIGHEST, receiveCanceled: false);
            bus.RegisterEvent<CancelableEvent>(_ => order.Add("skipped"), EventPriority.NORMAL);

            bus.TriggerEventAsync(new CancelableEvent()).GetAwaiter().GetResult();

            CollectionAssert.AreEqual(new[] { "async-canceler" }, order);
        }

        [Test]
        public void GetSubscribers_ReturnsDefensiveCopyOfDispatchSnapshot()
        {
            var bus = EventBus.CreateBus();
            bus.RegisterEvent<PlainEvent>(_ => { }, EventPriority.NORMAL);

            var evt = new PlainEvent();
            bus.TriggerEvent(evt);

            var first = evt.GetSubscribers();
            Assert.AreEqual(1, first.Length);

            first[0] = null;
            var second = evt.GetSubscribers();
            Assert.IsNotNull(second[0]);
        }
    }
}
