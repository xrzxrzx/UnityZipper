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
            if (!TryGetPool<T>(out var pool))//幂等语义：池不存在时静默跳过
                return;

            pool.Clear();
        }

        public void CreatePool<T>(ZPoolOptions<T> options) where T : Component, IZObjectPoolItem
        {
            #region 健壮性检查
            if (options.Prefab == null)
            {
                _logger.Error("对象池：预制体不能为空", new ArgumentNullException(nameof(options.Prefab)));
                return;
            }

            if (options.Prefab.GetComponent<T>() == null)
            {
                _logger.Error($"对象池：预制体 {options.Prefab.name} 缺少组件 {typeof(T).Name}");
                return;
            }

            if (options.InitialSize < 0)
            {
                _logger.Error("对象池：InitialSize 不能为负数", new ArgumentOutOfRangeException(nameof(options.InitialSize)));
                return;
            }

            if (options.MaxSize < 0)
            {
                _logger.Error("对象池：MaxSize 不能为负数", new ArgumentOutOfRangeException(nameof(options.MaxSize)));
                return;
            }

            if (options.MaxSize > 0 && options.InitialSize > options.MaxSize)
            {
                _logger.Error("对象池：InitialSize 不能大于 MaxSize", new ArgumentException("InitialSize 不能大于 MaxSize"));
                return;
            }

            if (_pools.ContainsKey(typeof(T)))//如果当前类型已有池
            {
                _logger.Warning($"对象池：类型 {typeof(T).Name} 的对象池已存在");
                return;
            }

            #endregion

            var pool = new ZObjectPool<T>(options, _logger);

            _pools.Add(typeof(T), pool);
        }

        public void DestroyPool<T>()
        {
            if (!TryGetPool<T>(out var pool))//幂等语义：池不存在时静默跳过
                return;

            _pools.Remove(typeof(T));//先摘牌再 Dispose：OnClear 回调里若再访问该池会得到"不存在"
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
            var pool = GetTypedPool<T>();
            if (pool == null)
                return default;

            return pool.GetItem();
        }

        public List<T> GetItemsByCount<T>(int count) where T : Component, IZObjectPoolItem
        {
            var pool = GetTypedPool<T>();
            if (pool == null)
                return new List<T>(0);

            return pool.GetItemsByCount(count);
        }

        public void GetItemsByCount<T>(int count, List<T> result) where T : Component, IZObjectPoolItem
        {
            result.Clear();

            var pool = GetTypedPool<T>();
            if (pool == null)
                return;

            pool.GetItemsByCount(count, result);
        }

        public ZPoolsState GetPoolsState()
        {
            return new ZPoolsState(_pools);
        }

        public void Return<T>(T item) where T : Component, IZObjectPoolItem
        {
            if (item == null)
            {
                _logger.Error("对象池：返回的对象不能为空", new ArgumentNullException(nameof(item)));
                return;
            }

            if(item.ReturnToPool == null)
            {
                _logger.Error($"对象池：对象 {item.name} 的 ReturnToPool 委托未设置");
                return;
            }

            item.ReturnToPool.Invoke(item);
        }

        //静默查询：给"池可以不存在"的幂等 API 使用（ClearPool、DestroyPool）
        private bool TryGetPool<T>(out ZObjectPoolBase pool)
            => _pools.TryGetValue(typeof(T), out pool);

        private ZObjectPool<T> GetTypedPool<T>() where T : Component, IZObjectPoolItem
        {
            if (!_pools.TryGetValue(typeof(T), out var pool))
            {
                _logger.Error($"对象池：类型 {typeof(T).Name} 的对象池不存在");
                return null;
            }

            var typed = pool as ZObjectPool<T>;
            if (typed == null)//兜底：key 与实例不匹配（当前不可能，将来"一类型多池"才会出现）
                _logger.Error($"对象池：类型 {typeof(T).Name} 的池实例与 key 不匹配");

            return typed;
        }
    }
}