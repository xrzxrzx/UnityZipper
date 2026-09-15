using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace Zipper.Core.Logging.Sink
{
    internal class FileSink : IZLogSink
    {
        public bool RequiresMainThread => false;//文件支持多线程写入

        public ZLogLevel MinimumLevel { get; }

        readonly ConcurrentQueue<string> _queue;
        readonly AutoResetEvent _signal;
        readonly Thread _worker;
        readonly string _path;
        readonly StringBuilder _buffer;

        ZMainThreadDispatcher _dispatcher;
        int _dropped;
        bool _disposed;
        CancellationTokenSource _cts;
        CancellationToken _ct;

        const int MaxQueuedLines = 8192;
        const int FlushBytes = 16 * 1024;
        const int FlushIntervalMs = 500;

        public FileSink(string path, ZLogLevel level, ZMainThreadDispatcher dispatcher)
        {
            MinimumLevel = level;
            _queue = new ConcurrentQueue<string>();
            _signal = new AutoResetEvent(false);
            _worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "ZipperLogWriter"
            };
            _path = path;
            _dispatcher = dispatcher;
            _buffer = new StringBuilder(16 * 1024);

            _dropped = 0;
            _disposed = false;

            _cts = new CancellationTokenSource();
            _ct = _cts.Token;

            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            _worker.Start();
        }

        private void WorkerLoop()
        {
            try
            {
                using var writer = new StreamWriter(_path, append: true) { AutoFlush = true };

                while (!_ct.IsCancellationRequested)
                {
                    _signal.WaitOne(FlushIntervalMs);
                    DrainInto(writer);
                }

                DrainInto(writer);
                writer.Flush();
            }
            catch (Exception ex)
            {
                _dispatcher.Enqueue(() => UnityEngine.Debug.LogException(ex));
            }
        }

        private void DrainInto(StreamWriter writer)
        {
            while (_queue.TryDequeue(out var line))
            {
                _buffer.Append(line);
                if (_buffer.Length >= FlushBytes)
                {
                    Commit(writer);
                }
            }
            Commit(writer);
        }

        private void Commit(StreamWriter writer)
        {
            if (_buffer.Length == 0)
                return;

            if (_dropped > 0)//如果有日志丢失
            {
                writer.WriteLine($"[{System.DateTime.Now:HH:mm:ss.fff}] [Warn] [Zipper.Core] 日志过载，已丢弃 {_dropped} 条");
                Interlocked.Exchange(ref _dropped, 0);
            }

            writer.Write(_buffer);
            _buffer.Clear();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _signal.Set();
            _cts.Cancel();

            _worker.Join(1000);//设置上限，防止卡死
            _signal.Dispose();
            _cts.Dispose();
        }

        public void Flush()
        {
            if (_disposed)
                return;

            _signal.Set();
        }

        public void Write(in ZLogEntry entry)
        {
            if (_disposed)
                return;

            if (_queue.Count >= MaxQueuedLines)//如果超过待写队列上线
            {
                if (_queue.TryDequeue(out _))//抛弃最老的待写日志
                {
                    Interlocked.Increment(ref _dropped);
                }
            }

            bool wasEmpty = _queue.IsEmpty;
            _queue.Enqueue(LogFormatter.Format(entry));
            if (wasEmpty)
                _signal.Set();

            if (entry.Level >= ZLogLevel.Error)
            {
                Flush();
            }
        }
    }
}