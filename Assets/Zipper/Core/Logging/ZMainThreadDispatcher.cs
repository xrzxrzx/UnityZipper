using System;
using System.Collections.Concurrent;

namespace Zipper.Core.Logging
{
    public sealed class ZMainThreadDispatcher : System.IDisposable
    {
        readonly ConcurrentQueue<Action> _pending;
        int _mainThreadId;
        bool _disposed;

        public ZMainThreadDispatcher()
        {
            _pending = new();
            _mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            _disposed = false;
        }

        public bool IsMainThread => System.Threading.Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        public void Enqueue(Action action)
        {
            if (_disposed)
                return;

            _pending.Enqueue(action);
        }

        public void Pump(int maxPerFrame = 256)
        {
            if( _disposed)
                return;

            int i = 0;
            while (i < maxPerFrame && _pending.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch(Exception e)
                {
                    UnityEngine.Debug.LogError($"[Zipper] 日志模块回调调用出错 {e.Message}");
                }
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _pending.Clear();
        }
    }
}