using System;

namespace Zipper.Core.Logging.Sink
{
    internal class ConsoleSink : IZLogSink
    {
        public bool RequiresMainThread => true; //只能主线程

        public ZLogLevel MinimumLevel { get; }

        public ConsoleSink(ZLogLevel level)
        {
            MinimumLevel = level;
        }

        public void Flush()
        {
            
        }

        public void Write(in ZLogEntry entry)
        {
            string line = Format(entry);                     // "[12:00:01.123] [Zipper.Resources] 加载完成"
            switch (entry.Level)
            {
                case ZLogLevel.Warning: UnityEngine.Debug.LogWarning(line, entry.Context); break;
                case ZLogLevel.Error:
                case ZLogLevel.Fatal: UnityEngine.Debug.LogError(line, entry.Context); break;
                default: UnityEngine.Debug.Log(line, entry.Context); break;
            }
        }

        private string Format(ZLogEntry entry)
        {
            return $"[{entry.Time:HH:mm:ss.fff}] [{entry.ClassName ?? "?"}|{entry.Member ?? "?"}:{entry.Line}] {(entry.Exception != null ? entry.Exception.Message : "")} {entry.Message}";
        }

        public void Dispose()
        {
            
        }
    }
}