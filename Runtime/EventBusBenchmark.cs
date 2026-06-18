using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ShrinkEventBus.Runtime
{
    /// <summary>
    /// ShrinkEventBus 性能测试。
    /// 挂在任意 GameObject 上，运行后在 Console 查看结果。
    /// 在进行测试时不要打开事件查看器的大量实时追踪。
    /// </summary>
    public sealed class EventBusBenchmark : MonoBehaviour
    {
        [Header("同步触发测试")] public int iterations = 1_000_000;

        [Header("大批量订阅者测试")] public int subscriberCount = 30;
        public int massIterations = 10_000;

        [Header("异步触发测试")] public int asyncIterations = 1_000;

        [Header("注册 / 注销测试")] public int registerIterations = 5_000;
        public int scannedRegisterIterations = 1_000;

        [Header("取消事件测试")] public int cancelIterations = 100_000;

        public class BenchmarkEvent : EventBase
        {
            public int Value { get; set; }
        }

        public sealed class DerivedBenchmarkEvent : BenchmarkEvent
        {
            public bool IsDerived { get; set; }
        }

        [Cancelable]
        public class CancelableBenchmarkEvent : EventBase
        {
            public int Value { get; set; }
        }

        private sealed class BenchmarkInstanceSubscriber
        {
            public int Recorded;

            [EventSubscribe(EventPriority.NORMAL)]
            private void OnBenchmark(BenchmarkEvent evt)
            {
                Recorded = evt.Value;
            }
        }

        private static class BenchmarkStaticSubscriber
        {
            public static int Recorded;

            [EventSubscribe(EventPriority.NORMAL)]
            private static void OnBenchmark(BenchmarkEvent evt)
            {
                Recorded = evt.Value;
            }
        }

        private static class BenchmarkMethodSubscriber
        {
            public static int Recorded;

            [EventSubscribe(EventPriority.NORMAL)]
            private static void OnBenchmark(BenchmarkEvent evt)
            {
                Recorded = evt.Value;
            }
        }

        private sealed class PhaseSubscriber
        {
            public int Sum;

            [EventSubscribe(EventPriority.HIGH)]
            private void OnHigh(BenchmarkEvent evt)
            {
                Sum += evt.Value + 1;
            }

            [EventSubscribe(EventPriority.NORMAL)]
            private void OnNormal(BenchmarkEvent evt)
            {
                Sum += evt.Value + 10;
            }

            [EventSubscribe(EventPriority.LOW)]
            private void OnLow(BenchmarkEvent evt)
            {
                Sum += evt.Value + 100;
            }
        }

        private readonly StringBuilder _report = new();

        private void Start()
        {
            RunAllBenchmarks().Forget();
        }

        private async UniTaskVoid RunAllBenchmarks()
        {
            _report.Clear();
            _report.AppendLine("╔══════════════════════════════════════════════════╗");
            _report.AppendLine("║         ShrinkEventBus Benchmark Report          ║");
            _report.AppendLine(
                $"║ iter={iterations,8} mass={massIterations,6} reg={registerIterations,5} scan={scannedRegisterIterations,4} ║");
            _report.AppendLine("╚══════════════════════════════════════════════════╝");
            _report.AppendLine("  基准对象：独立实例 bus（AllowPerPhaseDispatch 已开启）");

            Log("开始：无订阅者触发");
            Bench_TriggerEvent_NoSubscriber();
            await UniTask.Yield();

            Log("开始：单订阅者同步触发");
            Bench_TriggerEvent_SingleSubscriber();
            await UniTask.Yield();

            Log("开始：父事件监听子事件");
            Bench_TriggerEvent_InheritedSubscriber();
            await UniTask.Yield();

            Log($"开始：{subscriberCount} 订阅者同步触发");
            Bench_TriggerEvent_MassSubscribers();
            await UniTask.Yield();

            Log("开始：按 phase 分发");
            Bench_TriggerEvent_PerPhase();
            await UniTask.Yield();

            Log("开始：异步触发");
            await Bench_TriggerEventAsync();
            await UniTask.Yield();

            Log("开始：手工 delegate 注册/注销");
            Bench_RegisterUnregister_ManualDelegate();
            await UniTask.Yield();

            Log("开始：实例扫描注册/注销");
            Bench_RegisterUnregister_InstanceScan();
            await UniTask.Yield();

            Log("开始：静态类型扫描注册/注销");
            Bench_RegisterUnregister_StaticScan();
            await UniTask.Yield();

            Log("开始：MethodInfo 注册/注销");
            Bench_RegisterUnregister_MethodScan();
            await UniTask.Yield();

            Log("开始：EventPool vs new");
            Bench_EventPool_vs_New();
            await UniTask.Yield();

            Log("开始：已取消事件跳过");
            Bench_CanceledEvent_Skip();

            _report.AppendLine("\n══════════════════════════════════════════════════");
            _report.AppendLine("  全部测试完成");
            Debug.Log(_report.ToString());
        }

        private void Bench_TriggerEvent_NoSubscriber()
        {
            var bus = CreateBenchmarkBus();
            var evt = new BenchmarkEvent { Value = 1 };

            Warmup(() => bus.TriggerEvent(evt), 500);

            var sw = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
                bus.TriggerEvent(evt);
            sw.Stop();

            Record("TriggerEvent × 无订阅者（基线）", sw.Elapsed.TotalMilliseconds, iterations);
            Log("完成：无订阅者触发");
        }

        private void Bench_TriggerEvent_SingleSubscriber()
        {
            var bus = CreateBenchmarkBus();
            var dummy = 0;

            void Handler(BenchmarkEvent evt)
            {
                dummy = evt.Value;
            }

            bus.RegisterEvent<BenchmarkEvent>(Handler, EventPriority.NORMAL);

            var evt = new BenchmarkEvent { Value = 1 };
            Warmup(() => bus.TriggerEvent(evt), 500);

            var sw = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
                bus.TriggerEvent(evt);
            sw.Stop();

            Record("TriggerEvent × 单订阅者同步", sw.Elapsed.TotalMilliseconds, iterations);
            Log("完成：单订阅者同步触发");

            bus.UnregisterEvent<BenchmarkEvent>(Handler);
            GC.KeepAlive(dummy);
        }

        private void Bench_TriggerEvent_InheritedSubscriber()
        {
            var bus = CreateBenchmarkBus();
            var dummy = 0;

            void Handler(BenchmarkEvent evt)
            {
                dummy = evt.Value;
            }

            bus.RegisterEvent<BenchmarkEvent>(Handler, EventPriority.NORMAL);

            var evt = new DerivedBenchmarkEvent { Value = 1, IsDerived = true };
            Warmup(() => bus.TriggerEvent(evt), 500);

            var sw = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
                bus.TriggerEvent(evt);
            sw.Stop();

            Record("TriggerEvent × 父事件监听子事件", sw.Elapsed.TotalMilliseconds, iterations);
            Log("完成：父事件监听子事件");

            bus.UnregisterEvent<BenchmarkEvent>(Handler);
            GC.KeepAlive(dummy);
        }

        private void Bench_TriggerEvent_MassSubscribers()
        {
            var bus = CreateBenchmarkBus();
            var dummy = 0;
            var handlers = new Action<BenchmarkEvent>[subscriberCount];
            for (var i = 0; i < subscriberCount; i++)
            {
                var captured = i;
                handlers[i] = evt => { dummy = captured + evt.Value; };
                bus.RegisterEvent<BenchmarkEvent>(handlers[i], EventPriority.NORMAL);
            }

            var evt = new BenchmarkEvent { Value = 1 };
            Warmup(() => bus.TriggerEvent(evt), 100);

            var sw = Stopwatch.StartNew();
            for (var i = 0; i < massIterations; i++)
                bus.TriggerEvent(evt);
            sw.Stop();

            Record($"TriggerEvent × {subscriberCount} 订阅者同步", sw.Elapsed.TotalMilliseconds, massIterations);
            Log($"完成：{subscriberCount} 订阅者同步触发");

            foreach (var handler in handlers)
                bus.UnregisterEvent<BenchmarkEvent>(handler);

            GC.KeepAlive(dummy);
        }

        private void Bench_TriggerEvent_PerPhase()
        {
            var bus = CreateBenchmarkBus();
            var subscriber = new PhaseSubscriber();
            bus.Register(subscriber);

            var evt = new BenchmarkEvent { Value = 1 };
            Warmup(() => bus.TriggerEvent(EventPriority.HIGH, evt), 500);

            var sw = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
                bus.TriggerEvent(EventPriority.HIGH, evt);
            sw.Stop();

            Record("TriggerEvent(Phase=HIGH) × 3 phase 监听", sw.Elapsed.TotalMilliseconds, iterations);
            Log("完成：按 phase 分发");

            bus.Unregister(subscriber);
            GC.KeepAlive(subscriber.Sum);
        }

        private async UniTask Bench_TriggerEventAsync()
        {
            var bus = CreateBenchmarkBus();
            var dummy = 0;

            async UniTask Handler(BenchmarkEvent evt)
            {
                dummy = evt.Value;
                await UniTask.CompletedTask;
            }

            bus.RegisterEvent<BenchmarkEvent>(Handler, EventPriority.NORMAL);

            var evt = new BenchmarkEvent { Value = 1 };
            await WarmupAsync(() => bus.TriggerEventAsync(evt), 10);

            const int perFrame = 10;
            var totalMs = 0.0;
            var ran = 0;

            while (ran < asyncIterations)
            {
                await UniTask.Yield();
                var count = Math.Min(perFrame, asyncIterations - ran);
                var sw = Stopwatch.StartNew();
                for (var i = 0; i < count; i++)
                    await bus.TriggerEventAsync(evt);
                sw.Stop();
                totalMs += sw.Elapsed.TotalMilliseconds;
                ran += count;
            }

            Record("TriggerEventAsync × 单订阅者异步", totalMs, asyncIterations);
            Log("完成：异步触发");

            bus.UnregisterEvent<BenchmarkEvent>(Handler);
            GC.KeepAlive(dummy);
        }

        private void Bench_RegisterUnregister_ManualDelegate()
        {
            var bus = CreateBenchmarkBus();
            var dummy = 0;
            var handlers = new Action<BenchmarkEvent>[registerIterations];
            for (var i = 0; i < registerIterations; i++)
            {
                var captured = i;
                handlers[i] = evt => { dummy = captured; };
            }

            var swReg = Stopwatch.StartNew();
            for (var i = 0; i < registerIterations; i++)
                bus.RegisterEvent<BenchmarkEvent>(handlers[i], EventPriority.NORMAL);
            swReg.Stop();
            Record("RegisterEvent(delegate)", swReg.Elapsed.TotalMilliseconds, registerIterations);

            var swUnreg = Stopwatch.StartNew();
            for (var i = 0; i < registerIterations; i++)
                bus.UnregisterEvent<BenchmarkEvent>(handlers[i]);
            swUnreg.Stop();
            Record("UnregisterEvent(delegate)", swUnreg.Elapsed.TotalMilliseconds, registerIterations);
            Log("完成：手工 delegate 注册/注销");

            GC.KeepAlive(dummy);
        }

        private void Bench_RegisterUnregister_InstanceScan()
        {
            var bus = CreateBenchmarkBus();
            var subscribers = new BenchmarkInstanceSubscriber[scannedRegisterIterations];
            for (var i = 0; i < subscribers.Length; i++)
                subscribers[i] = new BenchmarkInstanceSubscriber();

            var sw = Stopwatch.StartNew();
            for (var i = 0; i < subscribers.Length; i++)
            {
                var subscriber = subscribers[i];
                bus.Register(subscriber);
                bus.Unregister(subscriber);
            }

            sw.Stop();
            Record("Register/Unregister(instance scan)", sw.Elapsed.TotalMilliseconds, subscribers.Length);
            Log("完成：实例扫描注册/注销");
        }

        private void Bench_RegisterUnregister_StaticScan()
        {
            var bus = CreateBenchmarkBus();

            var sw = Stopwatch.StartNew();
            for (var i = 0; i < scannedRegisterIterations; i++)
            {
                bus.Register(typeof(BenchmarkStaticSubscriber));
                bus.Unregister(typeof(BenchmarkStaticSubscriber));
            }

            sw.Stop();
            Record("Register/Unregister(static type scan)", sw.Elapsed.TotalMilliseconds, scannedRegisterIterations);
            Log("完成：静态类型扫描注册/注销");
        }

        private void Bench_RegisterUnregister_MethodScan()
        {
            var bus = CreateBenchmarkBus();
            var method = typeof(BenchmarkMethodSubscriber).GetMethod("OnBenchmark",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null)
                throw new MissingMethodException(typeof(BenchmarkMethodSubscriber).FullName, "OnBenchmark");

            var sw = Stopwatch.StartNew();
            for (var i = 0; i < scannedRegisterIterations; i++)
            {
                bus.Register(method);
                bus.Unregister(method);
            }

            sw.Stop();
            Record("Register/Unregister(MethodInfo)", sw.Elapsed.TotalMilliseconds, scannedRegisterIterations);
            Log("完成：MethodInfo 注册/注销");
        }

        private void Bench_EventPool_vs_New()
        {
            var bus = CreateBenchmarkBus();

            void Handler(BenchmarkEvent evt)
            {
            }

            bus.RegisterEvent<BenchmarkEvent>(Handler, EventPriority.NORMAL);

            for (var i = 0; i < 64; i++)
                EventPool<BenchmarkEvent>.Release(EventPool<BenchmarkEvent>.Get());

            GC.Collect();
            var gcBefore = GC.CollectionCount(0);
            var swNew = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                var evt = new BenchmarkEvent { Value = i };
                bus.TriggerEvent(evt);
            }

            swNew.Stop();
            var gcNew = GC.CollectionCount(0) - gcBefore;
            Record($"TriggerEvent × new()      [GC Gen0={gcNew,3}]", swNew.Elapsed.TotalMilliseconds, iterations);

            GC.Collect();
            gcBefore = GC.CollectionCount(0);
            var swPool = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                using var evt = EventPool<BenchmarkEvent>.Get();
                evt.Value = i;
                bus.TriggerEvent(evt);
            }

            swPool.Stop();
            var gcPool = GC.CollectionCount(0) - gcBefore;
            Record($"TriggerEvent × Pool.Get() [GC Gen0={gcPool,3}]", swPool.Elapsed.TotalMilliseconds, iterations);
            Log("完成：EventPool vs new");

            bus.UnregisterEvent<BenchmarkEvent>(Handler);
        }

        private void Bench_CanceledEvent_Skip()
        {
            var bus = CreateBenchmarkBus();
            var dummy = 0;
            var handlers = new Action<CancelableBenchmarkEvent>[10];
            for (var i = 0; i < handlers.Length; i++)
            {
                handlers[i] = evt => { dummy = evt.Value; };
                bus.RegisterEvent<CancelableBenchmarkEvent>(handlers[i], EventPriority.NORMAL);
            }

            var evt = new CancelableBenchmarkEvent { Value = 1 };
            evt.SetCanceled(true);

            Warmup(() => bus.TriggerEvent(evt), 500);

            var sw = Stopwatch.StartNew();
            for (var i = 0; i < cancelIterations; i++)
                bus.TriggerEvent(evt);
            sw.Stop();

            Record("TriggerEvent × 已取消事件跳过（10 订阅者）", sw.Elapsed.TotalMilliseconds, cancelIterations);
            Log("完成：已取消事件跳过");

            foreach (var handler in handlers)
                bus.UnregisterEvent<CancelableBenchmarkEvent>(handler);

            GC.KeepAlive(dummy);
        }

        private static IShrinkEventBus CreateBenchmarkBus()
        {
            return EventBus.CreateBus(builder => builder.AllowPerPhaseDispatch());
        }

        private static void Warmup(Action action, int count)
        {
            for (var i = 0; i < count; i++)
                action();
        }

        private static async UniTask WarmupAsync(Func<UniTask<bool>> action, int count)
        {
            for (var i = 0; i < count; i++)
                await action();
        }

        private static void Log(string msg)
        {
            Debug.Log($"[Benchmark] {msg}");
        }

        private void Record(string label, double totalMs, int count)
        {
            var perOp = totalMs / count * 1000.0;
            var throughput = totalMs <= 0.0001 ? 0 : count / (totalMs / 1000.0);
            _report.AppendLine($"\n  ▶ {label}");
            _report.AppendLine($"      总耗时 : {totalMs,10:F3} ms");
            _report.AppendLine($"      单次   : {perOp,10:F4} μs/op");
            _report.AppendLine($"      吞吐量 : {throughput,10:F0} ops/s");
        }
    }
}
