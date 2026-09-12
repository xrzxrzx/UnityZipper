using System.Collections.Concurrent;

namespace Zipper.Core.Logging
{
    public sealed class ZMainThreadDispatcher : System.IDisposable
    {
        readonly ConcurrentQueue<Action> _pending;
        int _mainThreadId;
        public bool IsMainThread { get; }

        public void Enqueue(Action action)
        {

        }

        public void Pump(int maxPerFrame = 256)
        {

        }

        public void Dispose()
        {

        }
    }
}