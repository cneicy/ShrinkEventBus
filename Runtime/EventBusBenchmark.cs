using System;
using System.Diagnostics;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ShrinkEventBus.Runtime
{
    /// <summary>
    /// ShrinkEventBus 性能测试
    /// 挂在任意 GameObject 上，运行后在 Console 查看结果。
    /// 在进行测试时不要打开事件查看器大的追踪实时事件功能
    /// </summary>
    public class EventBusBenchmark : MonoBehaviour
    {
        [Header("同步触发测试")] public int iterations = 1000000;

        [Header("大批量订阅者测试")] public int subscriberCount = 30;
        public int massIterations = 1000;

        [Header("异步触发测试")] public int asyncIterations = 1000;

        [Header("注册 / 注销测试")] public int registerIterations = 5000;

        [Header("取消事件测试")] public int cancelIterations = 100000;


        public class BenchmarkEvent : EventBase
        {
            public int Value { get; set; }
        }

        [Cancelable]
        public class CancelableBenchmarkEvent : EventBase
        {
            public int Value { get; set; }
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
                $"║  iter={iterations,8} mass={massIterations,5} reg={registerIterations,5} cancel={cancelIterations,6}  ║");
            _report.AppendLine("╚══════════════════════════════════════════════════╝");

            Log("开始：无订阅者触发");
            Bench_TriggerEvent_NoSubscriber();
            await UniTask.Yield();

            Log("开始：单订阅者同步触发");
            Bench_TriggerEvent_SingleSubscriber();
            await UniTask.Yield();

            Log($"开始：{subscriberCount} 订阅者同步触发");
            Bench_TriggerEvent_MassSubscribers();
            await UniTask.Yield();

            Log("开始：异步触发");
            await Bench_TriggerEventAsync();
            await UniTask.Yield();

            Log("开始：注册/注销");
            Bench_RegisterUnregister();
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
            EventBus.ClearAllSubscribersForEvent<BenchmarkEvent>();
            var evt = new BenchmarkEvent { Value = 1 };

            for (var i = 0; i < 500; i++) EventBus.TriggerEvent(evt);

            var sw = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++) EventBus.TriggerEvent(evt);
            sw.Stop();

            Record("TriggerEvent × 无订阅者（基线）", sw.Elapsed.TotalMilliseconds, iterations);
            Log("完成：无订阅者触发");
        }


        private void Bench_TriggerEvent_SingleSubscriber()
        {
            EventBus.ClearAllSubscribersForEvent<BenchmarkEvent>();

            var dummy = 0;

            void Handler(BenchmarkEvent e)
            {
                dummy = e.Value;
            }

            EventBus.RegisterEvent<BenchmarkEvent>(Handler, EventPriority.NORMAL);

            var evt = new BenchmarkEvent { Value = 1 };
            for (var i = 0; i < 500; i++) EventBus.TriggerEvent(evt);

            var sw = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++) EventBus.TriggerEvent(evt);
            sw.Stop();

            Record("TriggerEvent × 单订阅者同步", sw.Elapsed.TotalMilliseconds, iterations);
            Log("完成：单订阅者同步触发");

            EventBus.UnregisterEvent<BenchmarkEvent>(Handler);
            GC.KeepAlive(dummy);
        }


        private void Bench_TriggerEvent_MassSubscribers()
        {
            EventBus.ClearAllSubscribersForEvent<BenchmarkEvent>();

            var dummy = 0;
            var handlers = new Action<BenchmarkEvent>[subscriberCount];
            for (var i = 0; i < subscriberCount; i++)
            {
                var captured = i;
                handlers[i] = e => { dummy = captured + e.Value; };
                EventBus.RegisterEvent<BenchmarkEvent>(handlers[i], EventPriority.NORMAL);
            }

            var evt = new BenchmarkEvent { Value = 1 };
            for (var i = 0; i < 100; i++) EventBus.TriggerEvent(evt);

            var sw = Stopwatch.StartNew();
            for (var i = 0; i < massIterations; i++) EventBus.TriggerEvent(evt);
            sw.Stop();

            Record($"TriggerEvent × {subscriberCount} 订阅者同步", sw.Elapsed.TotalMilliseconds, massIterations);
            Log($"完成：{subscriberCount} 订阅者同步触发");

            foreach (var h in handlers) EventBus.UnregisterEvent<BenchmarkEvent>(h);
            GC.KeepAlive(dummy);
        }


        private async UniTask Bench_TriggerEventAsync()
        {
            EventBus.ClearAllSubscribersForEvent<BenchmarkEvent>();

            var dummy = 0;

            async UniTask Handler(BenchmarkEvent e)
            {
                dummy = e.Value;
                await UniTask.CompletedTask;
            }

            EventBus.RegisterEvent<BenchmarkEvent>(Handler, EventPriority.NORMAL);

            var evt = new BenchmarkEvent { Value = 1 };
            for (var i = 0; i < 10; i++) await EventBus.TriggerEventAsync(evt);

            const int perFrame = 10;
            var totalMs = 0.0;
            var ran = 0;

            while (ran < asyncIterations)
            {
                await UniTask.Yield();
                var count = Math.Min(perFrame, asyncIterations - ran);
                var sw = Stopwatch.StartNew();
                for (var i = 0; i < count; i++) await EventBus.TriggerEventAsync(evt);
                sw.Stop();
                totalMs += sw.Elapsed.TotalMilliseconds;
                ran += count;
            }

            Record("TriggerEventAsync × 单订阅者异步", totalMs, asyncIterations);
            Log("完成：异步触发");

            EventBus.UnregisterEvent<BenchmarkEvent>(Handler);
            GC.KeepAlive(dummy);
        }


        private void Bench_RegisterUnregister()
        {
            var dummy = 0;
            var handlers = new Action<BenchmarkEvent>[registerIterations];
            for (var i = 0; i < registerIterations; i++)
            {
                var captured = i;
                handlers[i] = e => { dummy = captured; };
            }

            var swReg = Stopwatch.StartNew();
            for (var i = 0; i < registerIterations; i++)
                EventBus.RegisterEvent<BenchmarkEvent>(handlers[i], EventPriority.NORMAL);
            swReg.Stop();
            Record("RegisterEvent", swReg.Elapsed.TotalMilliseconds, registerIterations);
            Log("完成：RegisterEvent");

            var swUnreg = Stopwatch.StartNew();
            for (var i = 0; i < registerIterations; i++)
                EventBus.UnregisterEvent<BenchmarkEvent>(handlers[i]);
            swUnreg.Stop();
            Record("UnregisterEvent", swUnreg.Elapsed.TotalMilliseconds, registerIterations);
            Log("完成：UnregisterEvent");

            GC.KeepAlive(dummy);
        }


        private void Bench_EventPool_vs_New()
        {
            EventBus.ClearAllSubscribersForEvent<BenchmarkEvent>();

            void Handler(BenchmarkEvent e)
            {
            }

            EventBus.RegisterEvent<BenchmarkEvent>(Handler, EventPriority.NORMAL);

            for (var i = 0; i < 64; i++)
                EventPool<BenchmarkEvent>.Release(EventPool<BenchmarkEvent>.Get());

            GC.Collect();
            var gcBefore = GC.CollectionCount(0);
            var swNew = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                var e = new BenchmarkEvent { Value = i };
                EventBus.TriggerEvent(e);
            }

            swNew.Stop();
            var gcNew = GC.CollectionCount(0) - gcBefore;
            Record($"TriggerEvent × new()      [GC Gen0={gcNew,3}]", swNew.Elapsed.TotalMilliseconds, iterations);
            Log("完成：new() 分配");

            GC.Collect();
            gcBefore = GC.CollectionCount(0);
            var swPool = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                using var e = EventPool<BenchmarkEvent>.Get();
                e.Value = i;
                EventBus.TriggerEvent(e);
            }

            swPool.Stop();
            var gcPool = GC.CollectionCount(0) - gcBefore;
            Record($"TriggerEvent × Pool.Get() [GC Gen0={gcPool,3}]", swPool.Elapsed.TotalMilliseconds, iterations);
            Log("完成：Pool.Get()");

            EventBus.UnregisterEvent<BenchmarkEvent>(Handler);
        }


        private void Bench_CanceledEvent_Skip()
        {
            EventBus.ClearAllSubscribersForEvent<CancelableBenchmarkEvent>();

            var dummy = 0;
            var handlers = new Action<CancelableBenchmarkEvent>[10];
            for (var i = 0; i < 10; i++)
            {
                handlers[i] = e => { dummy = e.Value; };
                EventBus.RegisterEvent<CancelableBenchmarkEvent>(handlers[i], EventPriority.NORMAL);
            }

            var evt = new CancelableBenchmarkEvent { Value = 1 };
            evt.SetCanceled(true);

            for (var i = 0; i < 500; i++) EventBus.TriggerEvent(evt);

            var sw = Stopwatch.StartNew();
            for (var i = 0; i < cancelIterations; i++) EventBus.TriggerEvent(evt);
            sw.Stop();

            Record("TriggerEvent × 已取消事件跳过（10 订阅者）", sw.Elapsed.TotalMilliseconds, cancelIterations);
            Log("完成：已取消事件跳过");

            foreach (var h in handlers)
                EventBus.UnregisterEvent<CancelableBenchmarkEvent>(h);
            GC.KeepAlive(dummy);
        }


        private static void Log(string msg) =>
            Debug.Log($"[Benchmark] {msg}");

        private void Record(string label, double totalMs, int count)
        {
            var perOp = totalMs / count * 1000.0;
            var throughput = count / (totalMs / 1000.0);
            _report.AppendLine($"\n  ▶ {label}");
            _report.AppendLine($"      总耗时 : {totalMs,10:F3} ms");
            _report.AppendLine($"      单次   : {perOp,10:F4} μs/op");
            _report.AppendLine($"      吞吐量 : {throughput,10:F0} ops/s");
        }
    }
}