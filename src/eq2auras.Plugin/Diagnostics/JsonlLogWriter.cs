using System;
using System.IO;
using System.Linq;
using System.Text;
using Advanced_Combat_Tracker;
using Eq2Auras.Core.Diagnostics;

namespace Eq2Auras.Plugin.Diagnostics
{
    /// Append-only JSONL writer under %APPDATA%\Advanced Combat Tracker\eq2auras\logs.
    /// One file per plugin session. Thread-safe via a lock; flushes each line.
    public sealed class JsonlLogWriter : IDisposable
    {
        private readonly object _gate = new object();
        private StreamWriter _writer;
        private bool _wroteAnything;

        public string FilePath { get; }

        public JsonlLogWriter()
        {
            var dir = LogsDirectory();
            Directory.CreateDirectory(dir);
            FilePath = Path.Combine(dir, "spike-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".jsonl");
            _writer = new StreamWriter(
                new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.Read),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) // no BOM — keeps JSONL clean
            {
                AutoFlush = true
            };
        }

        private static string LogsDirectory()
            => Path.Combine(ActGlobals.oFormActMain.AppDataFolder.FullName, "eq2auras", "logs");

        /// Rolling-window retention (SPEC §Diagnostic logging): decision is Core's
        /// LogRetentionPolicy; this is only the IO. Called BEFORE the session writer
        /// opens, and never when debug mode is on. Best-effort throughout — a locked
        /// or vanished file must not break plugin init.
        public static void SweepOldLogs()
        {
            try
            {
                var dir = LogsDirectory();
                if (!Directory.Exists(dir)) return;

                var files = new DirectoryInfo(dir)
                    .GetFiles("spike-*.jsonl")
                    .Select(f => new LogFileInfo
                    {
                        Path = f.FullName,
                        LastWriteUtc = f.LastWriteTimeUtc,
                        SizeBytes = f.Length
                    })
                    .ToList();

                foreach (var path in LogRetentionPolicy.FilesToDelete(files, DateTime.UtcNow))
                {
                    try { File.Delete(path); }
                    catch { }
                }
            }
            catch { }
        }

        public void Write(TimerSnapshotRecord record)
        {
            lock (_gate)
            {
                if (_writer == null) return;
                _writer.WriteLine(record.ToJsonl());
                _wroteAnything = true;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_writer == null) return;
                _writer.Dispose();
                _writer = null;

                if (_wroteAnything) return;
                try { File.Delete(FilePath); }      // empty session artifact — best-effort cleanup
                catch { }                            // teardown must never throw over a leftover file
            }
        }
    }
}
