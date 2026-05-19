using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace UGTLive
{
    /// <summary>
    /// A started CLI process that has already paid its boot cost and is blocked
    /// waiting for its prompt on stdin. stdout/stderr readers are attached at spawn
    /// time so OS pipe buffers never fill while the process is parked.
    /// </summary>
    public sealed class WarmCliProcess
    {
        public required Process Process { get; init; }
        public required Task<string> StdoutTask { get; init; }
        public required Task<string> StderrTask { get; init; }
        public required string Key { get; init; }
        public DateTime SpawnedUtc { get; init; } = DateTime.UtcNow;
        public bool Cold { get; set; }

        public void Kill()
        {
            try { if (!Process.HasExited) Process.Kill(entireProcessTree: true); }
            catch { }
            try { Process.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// Pre-spawns CLI processes so the multi-second Node/agent boot is paid
    /// off the critical path. A parked process has booted and is waiting on
    /// stdin; handing it the prompt skips cold start entirely.
    ///
    /// Pool is keyed by the exact command line - if the model/thinking flags
    /// change, stale entries are drained and respawned.
    /// </summary>
    public static class CliWarmPool
    {
        private static readonly object _lock = new();
        private static readonly Dictionary<string, List<WarmCliProcess>> _ready = new();
        // Command lines whose CLI refuses to park (proven at runtime) - never pooled again.
        private static readonly HashSet<string> _unpoolable = new();

        public static bool Enabled { get; private set; }
        public static int PoolSize { get; private set; } = 1;
        public static int MaxAgeSeconds { get; private set; } = 600;
        // A freshly spawned process is only treated as "ready" after it has been
        // alive this long, giving the CLI time to finish its boot before we hand
        // it stdin. (Node CLIs print nothing on a clean headless boot, so there
        // is no marker to wait on - a settle delay is the only signal we have.)
        public static int WarmupSettleSeconds { get; private set; } = 8;

        // Env vars that would force per-token API billing instead of subscription login.
        private static readonly string[] _apiKeyEnvVars =
        {
            "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN",
            "OPENAI_API_KEY", "CODEX_API_KEY",
            "GEMINI_API_KEY", "GOOGLE_API_KEY", "GOOGLE_GENAI_API_KEY"
        };

        public static void Configure(bool enabled, int poolSize, int warmupSettleSeconds = 8, int maxAgeSeconds = 600)
        {
            Enabled = enabled;
            PoolSize = Math.Max(1, poolSize);
            WarmupSettleSeconds = Math.Max(0, warmupSettleSeconds);
            MaxAgeSeconds = Math.Max(30, maxAgeSeconds);
        }

        private static string MakeKey(string command, string arguments) => command + "" + arguments;

        /// <summary>
        /// Returns a started process for this command line. If the warm pool has a
        /// parked one it is returned instantly (Cold=false); otherwise a fresh one
        /// is spawned (Cold=true). Either way a background refill is kicked when
        /// pooling is enabled.
        /// </summary>
        public static WarmCliProcess Acquire(string command, string arguments)
        {
            string key = MakeKey(command, arguments);

            bool unpoolable;
            lock (_lock) { unpoolable = _unpoolable.Contains(key); }

            if (Enabled && !unpoolable)
            {
                WarmCliProcess? parked = null;
                lock (_lock)
                {
                    DrainOtherKeysLocked(key);
                    if (_ready.TryGetValue(key, out var list))
                    {
                        while (list.Count > 0)
                        {
                            var candidate = list[0];
                            list.RemoveAt(0);
                            bool tooOld = (DateTime.UtcNow - candidate.SpawnedUtc).TotalSeconds > MaxAgeSeconds;
                            if (tooOld || candidate.Process.HasExited)
                            {
                                candidate.Kill();
                                continue;
                            }
                            parked = candidate;
                            break;
                        }
                    }
                }

                if (parked != null)
                {
                    KickRefill(command, arguments, key);
                    return parked;
                }

                // Pool empty for this key - spawn now (cold) and start filling.
                var cold = Spawn(command, arguments, key);
                cold.Cold = true;
                KickRefill(command, arguments, key);
                return cold;
            }

            var p = Spawn(command, arguments, key);
            p.Cold = true;
            return p;
        }

        private static void KickRefill(string command, string arguments, string key)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        lock (_lock)
                        {
                            if (!Enabled || _unpoolable.Contains(key)) return;
                            if (!_ready.TryGetValue(key, out var list))
                            {
                                list = new List<WarmCliProcess>();
                                _ready[key] = list;
                            }
                            if (list.Count >= PoolSize) return;
                        }
                        var w = Spawn(command, arguments, key);
                        // Let the CLI finish booting before it counts as ready.
                        if (WarmupSettleSeconds > 0)
                            await Task.Delay(WarmupSettleSeconds * 1000);
                        if (w.Process.HasExited)
                        {
                            // The CLI refused to park (e.g. Gemini exits 42 on empty
                            // stdin). Parking is incompatible with this CLI - stop
                            // trying so we don't spam-spawn forever.
                            Console.WriteLine($"[CliWarmPool] '{command}' cannot be pre-warmed " +
                                $"(exited {SafeExit(w)} while parked). Disabling pool for this command.");
                            w.Kill();
                            lock (_lock) { _unpoolable.Add(key); }
                            return;
                        }
                        lock (_lock)
                        {
                            if (!Enabled) { w.Kill(); return; }
                            if (!_ready.TryGetValue(key, out var list))
                            {
                                list = new List<WarmCliProcess>();
                                _ready[key] = list;
                            }
                            if (list.Count >= PoolSize) { w.Kill(); return; }
                            list.Add(w);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CliWarmPool] Refill error: {ex.Message}");
                }
            });
        }

        /// <summary>Pre-spawn the pool for a command line and (optionally) wait until parked.</summary>
        public static void Prewarm(string command, string arguments)
        {
            if (!Enabled) return;
            KickRefill(command, arguments, MakeKey(command, arguments));
        }

        public static int ReadyCount(string command, string arguments)
        {
            lock (_lock)
            {
                return _ready.TryGetValue(MakeKey(command, arguments), out var list) ? list.Count : 0;
            }
        }

        public static bool IsUnpoolable(string command, string arguments)
        {
            lock (_lock) { return _unpoolable.Contains(MakeKey(command, arguments)); }
        }

        private static string SafeExit(WarmCliProcess w)
        {
            try { return w.Process.ExitCode.ToString(); } catch { return "?"; }
        }

        private static void DrainOtherKeysLocked(string keepKey)
        {
            var toRemove = new List<string>();
            foreach (var kv in _ready)
            {
                if (kv.Key == keepKey) continue;
                foreach (var w in kv.Value) w.Kill();
                toRemove.Add(kv.Key);
            }
            foreach (var k in toRemove) _ready.Remove(k);
        }

        private static WarmCliProcess Spawn(string command, string arguments, string key)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command} {arguments}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false)
            };
            foreach (string envVar in _apiKeyEnvVars)
                psi.Environment.Remove(envVar);

            var process = new Process { StartInfo = psi };
            if (!process.Start())
                throw new InvalidOperationException($"Failed to start process: {command}");

            // Drain stdout/stderr immediately so the parked process never blocks
            // on a full OS pipe buffer while it waits for stdin.
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            return new WarmCliProcess
            {
                Process = process,
                StdoutTask = stdoutTask,
                StderrTask = stderrTask,
                Key = key
            };
        }
    }
}
