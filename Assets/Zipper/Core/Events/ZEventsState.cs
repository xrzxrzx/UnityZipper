namespace Zipper.Core.Events
{
    public class ZEventsState
    {
        public int TotalEventTypes { get; }
        public int TotalSubscribers { get; }
        public ZEventsState(int totalEventTypes, int totalSubscribers)
        {
            TotalEventTypes = totalEventTypes;
            TotalSubscribers = totalSubscribers;
        }
    }
}