namespace Zipper.Core.Events
{
    public class ZEventState
    {
        public string EventType { get; }
        public int SubscriberCount { get; }

        internal ZEventState(string eventType, int subscriberCount)
        {
            EventType = eventType;
            SubscriberCount = subscriberCount;
        }
    }
}