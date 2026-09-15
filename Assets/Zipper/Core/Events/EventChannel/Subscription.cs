using System;

namespace Zipper.Core.Events.EventChannel
{
    internal class Subscription<T> : IDisposable where T : class
    {
        public Action<T> Action { get; }
        public object Subscriber { get; }
        public bool IsDisposed { get; set; }

        public Subscription(Action<T> action, object subscriber)
        {
            Action = action;
            Subscriber = subscriber;
            IsDisposed = false;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}