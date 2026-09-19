using System;
using System.Collections.Generic;
using Zipper.Core.Events.EventChannel;

namespace Zipper.Core.Events
{
    public class ZEventsState
    {
        public int TotalEventTypes { get; }
        public int TotalSubscribers { get; }
        public List<ZEventState> EventStates { get; }

        internal ZEventsState(Dictionary<Type, EventChannelBase> eventChannels)
        {
            EventStates = new List<ZEventState>();

            TotalEventTypes = eventChannels.Count;
            int totalSubscribers = 0;

            foreach (var channelKeyValue in eventChannels)
            {
                EventStates.Add(new ZEventState(channelKeyValue.Key, channelKeyValue.Value.TotalSubscribers));
                totalSubscribers += channelKeyValue.Value.TotalSubscribers;
            }

            TotalSubscribers = totalSubscribers;
        }
    }
}