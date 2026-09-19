using System;

namespace Zipper.Core.Events
{
    public class ZEventState
    {
        public Type EventType { get; }
        public int SubscriberCount { get; }

        internal ZEventState(Type eventType, int subscriberCount)
        {
            EventType = eventType;
            SubscriberCount = subscriberCount;
        }
    }
}