using System;
using Zipper.Core.Logging;

namespace Zipper.Core.Events.EventChannel
{
    internal class EventChannel<T> : EventChannelBase where T : class
    {
        IZLogger _logger;
        Subscription<T>[] _subscriptions;

        public override int TotalSubscribers => _subscriptions.Length;

        public EventChannel(IZLogger logger)
        {
            _logger = logger;
            _subscriptions = new Subscription<T>[0];
        }

        public IDisposable Add(object subscriber, Action<T> handler)
        {
            var subscription = new Subscription<T>(handler, subscriber, this);
            Array.Resize(ref _subscriptions, _subscriptions.Length + 1);
            _subscriptions[_subscriptions.Length - 1] = subscription;
            return subscription;
        }

        public int Remove(Action<T> handler)
        {
            int length = _subscriptions.Length;
            _subscriptions = Array.FindAll(_subscriptions, s => s.Handler != handler);
            return length - _subscriptions.Length;
        }

        public int Remove(Subscription<T> subscription)
        {
            int length = _subscriptions.Length;
            _subscriptions = Array.FindAll(_subscriptions, s => s != subscription);
            return length - _subscriptions.Length;
        }

        public override int Remove(object subscriber)
        {
            int length = _subscriptions.Length;
            _subscriptions = Array.FindAll(_subscriptions, s => s.Subscriber != subscriber);
            return length - _subscriptions.Length;
        }

        public void Publish(in T @event)
        {
            foreach (var subscription in _subscriptions)
            {
                if (subscription.IsDisposed)
                {
                    continue;
                }

                try
                {
                    subscription.Handler(@event);
                }
                catch (Exception ex)
                {
                    _logger.Error($"执行事件处理程序时发生异常: {ex}", ex);
                }
            }
        }

        public override void Dispose()
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.MarkAsDisposed();
            }
            _subscriptions = Array.Empty<Subscription<T>>();
        }
    }
}