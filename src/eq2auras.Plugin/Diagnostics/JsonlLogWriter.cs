using System;
using System.IO;
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

        public string FilePath { get; }

        public JsonlLogWriter()
        {
            var dir = Path.Combine(
                ActGlobals.oFormActMain.AppDataFolder.FullName,
                "eq2auras", "logs");
            Directory.CreateDirectory(dir);
            FilePath = Path.Combine(dir, "spike-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".jsonl");
            _writer = new StreamWriter(
                new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.Read),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) // no BOM — keeps JSONL clean
            {
                AutoFlush = true
            };
        }

        public void Write(TimerSnapshotRecord record)
        {
            lock (_gate)
            {
                if (_writer == null) return;
                _writer.WriteLine(record.ToJsonl());
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _writer?.Dispose();
                _writer = null;
            }
        }
    }
}
