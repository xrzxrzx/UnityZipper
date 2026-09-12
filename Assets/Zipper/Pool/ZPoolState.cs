using System;

namespace Zipper.Pool
{
    public class ZPoolState
    {
        public Type Type {  get; private set; }
        public int TotalItems { get; private set; }
        public int ActiveItems { get; private set; }
        public int InactiveItems { get; private set; }

        internal ZPoolState(Type type, int totalItems, int activeItems, int inactiveItems)
        {
            Type = type;
            TotalItems = totalItems;
            ActiveItems = activeItems;
            InactiveItems = inactiveItems;
        }
    }
}