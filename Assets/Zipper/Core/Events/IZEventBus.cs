using System;

namespace Zipper.Core.Events
{
    public interface IZEventBus : IDisposable
    {
        IDisposable Subscribe<T>(Action<T> action) where T : class;
        IDisposable Subscribe<T>(object subscriber, Action<T> action) where T : class;

        bool Unsubscribe<T>(Action<T> action) where T : class;
        void UnsubscribeAll(object subscriber);

        void Publish<T>(in T @event) where T : class;

        ZEventsState GetState();
    }
}