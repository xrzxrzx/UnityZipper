using System;

namespace Zipper.Core.Logging.Sink
{
    public interface IZLogSink : IDisposable
    {
        bool RequiresMainThread { get; }//该Sink是否要求在主线程中写入
        ZLogLevel MinimumLevel { get; }
        void Write(in ZLogEntry entry);
        void Flush();
    }
}