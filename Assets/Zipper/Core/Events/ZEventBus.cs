using System;

namespace Zipper.Core.Events
{
    public class ZEventBus : IZEventBus
    {


        public IDisposable Subscribe<T>(Action<T> action) where T : class
        {
            throw new NotImplementedException();
        }

        public IDisposable Subscribe<T>(object subscriber, Action<T> action) where T : class
        {
            throw new NotImplementedException();
        }

        public void Unsubscribe<T>(Action<T> action) where T : class
        {
            throw new NotImplementedException();
        }

        public void Unsubscribe<T>(object subscriber) where T : class
        {
            throw new NotImplementedException();
        }

        public void UnsubscribeAll(object subscriber)
        {
            throw new NotImplementedException();
        }

        public void Publish<T>(in T @event) where T : class
        {
            throw new NotImplementedException();
        }

        public ZEventsState GetState()
        {
            throw new NotImplementedException();
        }
    }
}