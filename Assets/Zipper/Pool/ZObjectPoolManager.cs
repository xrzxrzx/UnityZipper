using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Zipper.Core.Logging;

namespace Zipper.Pool
{
    public class ZObjectPoolManager : IZObjectPoolManager
    {
        private readonly Dictionary<Type, ZObjectPoolBase> _pools;
        private readonly IZLogger _logger; 

        public ZObjectPoolManager(IZLogger logger)
        {
            _pools = new Dictionary<Type, ZObjectPoolBase>();
            _logger = logger;
        }

        public void ClearPool<T>()
        {
            var pool = GetPool<T>();
            if (pool == null)
                return;

            pool.Clear();
        }

        public void CreatePool<T>(ZPoolOptions<T> options) where T : Component, IZObjectPoolItem
        {
            if (_pools.ContainsKey(typeof(T)))//如果当前类型已有池
            {
                _logger.Warning($"对象池：类型 {typeof(T).Name} 的对象池已存在");
                return;
            }

            var pool = new ZObjectPool<T>(options, _logger);
            if (pool == null)
            {
                _logger.Error($"对象池：类型 {typeof(T).Name} 的对象池创建失败");
                return;
            }

            _pools.Add(typeof(T), pool);
        }

        public void DestroyPool<T>()
        {
            var pool = GetPool<T>();
            if (pool == null)
                return;

            _pools.Remove(typeof(T));
            pool.Dispose();
        }

        public void Dispose()
        {
            foreach (var pool in _pools.Values.ToArray())
            {
                pool.Dispose();
            }
            _pools.Clear();
        }

        public T Get<T>() where T : Component, IZObjectPoolItem
        {
            var pool = GetPool<T>();
            if (pool == null)
                return default;

            return (pool as ZObjectPool<T>).GetItem();
        }

        public List<T> GetItemsByCount<T>(int count) where T : Component, IZObjectPoolItem
        {
            var pool = GetPool<T>();
            if (pool == null)
                return new List<T>(0);

            return (pool as ZObjectPool<T>).GetItemsByCount(count);
        }

        public void GetItemsByCount<T>(int count, List<T> result) where T : Component, IZObjectPoolItem
        {
            result.Clear();

            var pool = GetPool<T>();
            if (pool == null)
                return;

            (pool as ZObjectPool<T>).GetItemsByCount(count, result);
        }

        public ZPoolsState GetPoolsState()
        {
            return new ZPoolsState(_pools);
        }

        public void Return<T>(T item) where T : Component, IZObjectPoolItem
        {
            item?.ReturnToPool?.Invoke(item);
        }

        private ZObjectPoolBase GetPool<T>()
        {
            ZObjectPoolBase pool;
            if (!_pools.TryGetValue(typeof(T), out pool))//如果当前类型池不存在
            {
                _logger.Error($"对象池：类型 {typeof(T).Name} 的对象池不存在");
                return null;
            }
            return pool;
        }
    }
}