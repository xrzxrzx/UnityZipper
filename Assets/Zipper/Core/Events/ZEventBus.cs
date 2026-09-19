using System;
using System.Collections.Generic;
using System.Threading;
using Zipper.Core.Events.EventChannel;
using Zipper.Core.Logging;

namespace Zipper.Core.Events
{
    public class ZEventBus : IZEventBus
    {
        IZLogger _logger;
        int _mainThreadId;
        bool _disposed = false;

        static readonly IDisposable EmptyIDisposable = new EmptyDisposable();
        sealed class EmptyDisposable : IDisposable { public void Dispose() { } }

        private Dictionary<Type, EventChannelBase> _eventChannels = new Dictionary<Type, EventChannelBase>();

        public ZEventBus(IZLogger logger)
        {
            _logger = logger;
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public IDisposable Subscribe<T>(Action<T> handler) where T : class
        {
            return Subscribe(null, handler);
        }

        public IDisposable Subscribe<T>(object subscriber, Action<T> handler) where T : class
        {
            if (_disposed)
                return EmptyIDisposable;

            if (_mainThreadId != Thread.CurrentThread.ManagedThreadId)
            {
                _logger.Error($"非主线程调用，线程ID[{Thread.CurrentThread.ManagedThreadId}] 主线程ID[{_mainThreadId}]");
                return EmptyIDisposable;
            }

            if(handler == null)
            {
                ArgumentNullException ex = new ArgumentNullException(nameof(handler));
                _logger.Error("订阅 handler 为空", ex);
                throw ex;
            }

            if (!_eventChannels.TryGetValue(typeof(T), out var channel))
            {
                channel = new EventChannel<T>(_logger);
                _eventChannels[typeof(T)] = channel;
            }

            return (channel as EventChannel<T>).Add(subscriber, handler);
        }

        public bool Unsubscribe<T>(Action<T> handler) where T : class
        {
            if (_disposed)
                return false;

            if (_mainThreadId != Thread.CurrentThread.ManagedThreadId)
            {
                _logger.Error($"非主线程调用，线程ID[{Thread.CurrentThread.ManagedThreadId}] 主线程ID[{_mainThreadId}]");
                return false;
            }

            if (handler == null)
            {
                _logger.Error("退订 handler 为空", new ArgumentNullException(nameof(handler)));
                return false;
            }

            if(_eventChannels.TryGetValue(typeof(T), out var channel))
            {
                int removeCount = (channel as EventChannel<T>).Remove(handler);
                if(removeCount <= 0)
                {
                    _logger.Warning($"退订失败，没有订阅 {typeof(T)} 类型事件");
                    return false;
                }
                return true;
            }
            else
            {
                _logger.Warning($"没有订阅这种事件类型 {typeof(T).Name}");
                return false;
            }
        }

        public void UnsubscribeAll(object subscriber)
        {
            if (_disposed)
                return;

            if (_mainThreadId != Thread.CurrentThread.ManagedThreadId)
            {
                _logger.Error($"非主线程调用，线程ID[{Thread.CurrentThread.ManagedThreadId}] 主线程ID[{_mainThreadId}]");
                return;
            }

            if (subscriber == null)
            {
                _logger.Warning("尝试退订所有匿名订阅");
                return;
            }

            foreach (var channel in _eventChannels.Values)
            {
                channel.Remove(subscriber);
            }
        }

        public void Publish<T>(in T @event) where T : class
        {
            if (_disposed)
                return;

            if (_mainThreadId != Thread.CurrentThread.ManagedThreadId)
            {
                _logger.Error($"非主线程调用，线程ID[{Thread.CurrentThread.ManagedThreadId}] 主线程ID[{_mainThreadId}]");
                return;
            }

            if(_eventChannels.TryGetValue(typeof(T), out var channel))
            {
                (channel as EventChannel<T>).Publish(@event);
            }
        }

        public ZEventsState GetState()
        {
            return new ZEventsState(_eventChannels);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            foreach (var channel in _eventChannels.Values)
            {
                channel.Dispose();
            }
            _eventChannels.Clear();
        }
    }
}