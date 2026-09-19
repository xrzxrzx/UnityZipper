using System;

namespace Zipper.Core.Events.EventChannel
{
    internal class Subscription<T> : IDisposable where T : class
    {
        public Action<T> Handler { get; }
        public object Subscriber { get; }//匿名订阅时为空（unity使用的C#版本问题，所以不使用可空类型）
        public bool IsDisposed { get; private set; }

        private EventChannel<T> _eventChannel;

        public Subscription(Action<T> handler, object subscriber, EventChannel<T> eventChannel)
        {
            Handler = handler;
            Subscriber = subscriber;
            IsDisposed = false;
            _eventChannel = eventChannel;
        }

        public void MarkAsDisposed()
        {
            IsDisposed = true;
        }

        public void Dispose()
        {
            if (IsDisposed)
                return;

            MarkAsDisposed();
            _eventChannel.Remove(this);
        }
    }
}