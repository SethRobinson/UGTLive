using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace UGTLive
{
    public static class OverlayProfiler
    {
        private sealed class ProfilingStat
        {
            public long Count;
            public long TotalMilliseconds;
            public long MaxMilliseconds;
        }

        private const long SlowOperationThresholdMs = 50;
        private static readonly object _lock = new object();
        private static readonly Dictionary<string, ProfilingStat> _stats = new Dictionary<string, ProfilingStat>();

        public static IDisposable Measure(string operationName)
        {
            return new ProfilingScope(operationName);
        }

        public static void Record(string operationName, long elapsedMilliseconds)
        {
            lock (_lock)
            {
                if (!_stats.TryGetValue(operationName, out ProfilingStat? stat))
                {
                    stat = new ProfilingStat();
                    _stats[operationName] = stat;
                }

                stat.Count++;
                stat.TotalMilliseconds += elapsedMilliseconds;
                if (elapsedMilliseconds > stat.MaxMilliseconds)
                {
                    stat.MaxMilliseconds = elapsedMilliseconds;
                }

                if (elapsedMilliseconds >= SlowOperationThresholdMs || stat.Count % 20 == 0)
                {
                    double average = stat.Count > 0 ? (double)stat.TotalMilliseconds / stat.Count : 0;
                    Console.WriteLine($"[OverlayProfiler] {operationName}: last={elapsedMilliseconds}ms avg={average:F1}ms max={stat.MaxMilliseconds}ms count={stat.Count}");
                }
            }
        }

        public static void DumpSummary()
        {
            lock (_lock)
            {
                foreach (KeyValuePair<string, ProfilingStat> kvp in _stats)
                {
                    ProfilingStat stat = kvp.Value;
                    double average = stat.Count > 0 ? (double)stat.TotalMilliseconds / stat.Count : 0;
                    Console.WriteLine($"[OverlayProfiler] SUMMARY {kvp.Key}: avg={average:F1}ms max={stat.MaxMilliseconds}ms count={stat.Count}");
                }
            }
        }

        private sealed class ProfilingScope : IDisposable
        {
            private readonly string _operationName;
            private readonly Stopwatch _stopwatch;
            private bool _disposed;

            public ProfilingScope(string operationName)
            {
                _operationName = operationName;
                _stopwatch = Stopwatch.StartNew();
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _stopwatch.Stop();
                Record(_operationName, _stopwatch.ElapsedMilliseconds);
                _disposed = true;
            }
        }
    }
}

