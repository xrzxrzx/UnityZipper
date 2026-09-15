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
            string line = LogFormatter.Format(entry);
            switch (entry.Level)
            {
                case ZLogLevel.Warning: UnityEngine.Debug.LogWarning(line, entry.Context); break;
                case ZLogLevel.Error:
                case ZLogLevel.Fatal: UnityEngine.Debug.LogError(line, entry.Context); break;
                default: UnityEngine.Debug.Log(line, entry.Context); break;
            }
        }

        public void Dispose()
        {
            
        }
    }
}