using System;
using System.Collections.Generic;
using UnityEngine;

namespace Zipper.Pool
{
    public class ZPoolsState
    {
        public int AllPoolsCount { get; private set; }
        public int AllItems { get; private set; }
        public int AllActiveItems { get; private set; }
        public int AllInactiveItems { get; private set; }
        public List<ZPoolState> PoolStates { get; private set; }

        internal ZPoolsState(Dictionary<Type, ZObjectPoolBase> pools)
        {
            AllPoolsCount = pools.Count;
            PoolStates = new List<ZPoolState>(pools.Count);
            foreach (var pool in pools)
            {
                var poolState = pool.Value;
                AllItems += poolState.CountAll;
                AllActiveItems += poolState.CountActive;
                AllInactiveItems += poolState.CountInactive;
                PoolStates.Add(new ZPoolState(pool.Key, poolState.CountAll, poolState.CountActive, poolState.CountInactive));
            }
        }
    }
}