using System;
using System.Collections.Generic;
using UnityEngine;

using Zipper.Core.Logging;

namespace Zipper.Pool
{
    /// <summary>
    /// 对象池
    /// 在使用Get、Return、Clear等方法时，触发对象的初始化、获取、归还和清理操作。为了提供更灵活的自定义行为，可以通过委托来定义这些操作。
    /// 触发逻辑：先调用对象的OnXXX方法，再调用委托方法。
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class ZObjectPool<T> : ZObjectPoolBase where T : Component, IZObjectPoolItem
    {
        IZLogger _logger;

        readonly Action<T> _onInitialize;
        readonly Action<T> _onGet;
        readonly Action<T> _onReturn;
        readonly Action<T> _onClear;

        GameObject prefab;
        public override GameObject Prefab => prefab;

        Queue<T> items;
        HashSet<T> inactiveItems;
        HashSet<T> allItems;
        HashSet<T> clearPendingItems;
        int maxSize;
        int totalCount;
        bool _disposed;

        public override int CountInactive => items.Count;
        public override int CountAll => totalCount;
        public override int CountActive => totalCount - items.Count;

        internal ZObjectPool(in ZPoolOptions<T> options, IZLogger logger)
        {
            _logger = logger;

            this.prefab = options.Prefab;
            this.maxSize = options.MaxSize;
            _onInitialize = options.OnInitialize;
            _onGet = options.OnGet;
            _onReturn = options.OnReturn;
            _onClear = options.OnClear;

            items = new Queue<T>(options.InitialSize);
            inactiveItems = new HashSet<T>(options.InitialSize);
            allItems = new HashSet<T>(options.InitialSize);
            clearPendingItems = new HashSet<T>(options.InitialSize);

            InitializeItems(options.InitialSize);
        }

        private void InitializeItems(int initialSize)
        {
            for (int i = 0; i < initialSize; i++)
            {
                var item = NewItem();
                items.Enqueue(item);
                inactiveItems.Add(item);
            }
        }

        private void ReturnItemToPool(IZObjectPoolItem item)
        {
            if (_disposed)//池已销毁，不再接收归还，否则会误报"不属于当前对象池"
                return;

            var tItem = item as T;
            if (tItem == null)
            {
                _logger.Error($"对象池：归还对象类型不匹配，期望 {typeof(T).Name}", new InvalidCastException($"对象池：归还对象类型不匹配，期望 {typeof(T).Name}"));
                return;
            }

            if (inactiveItems.Contains(tItem))
            {
                return;
            }

            if (!allItems.Contains(tItem))
            {
                _logger.Error($"对象池：对象 {tItem.name} 不属于当前对象池", new InvalidOperationException($"对象池：对象 {tItem.name} 不属于当前对象池"));
                return;
            }

            tItem.OnReturn();
            _onReturn?.Invoke(tItem);

            // 如果对象正在等待清理，则直接清理
            if (clearPendingItems.Remove(tItem))
            {
                ClearItem(tItem);
                return;
            }

            items.Enqueue(tItem);
            inactiveItems.Add(tItem);
        }

        private T NewItem()
        {
            var item = UnityEngine.Object.Instantiate(Prefab).GetComponent<T>();
            item.ReturnToPool = ReturnItemToPool;

            item.OnInitialize();
            _onInitialize?.Invoke(item);
            allItems.Add(item);
            totalCount++;

            return item;
        }

        public T GetItem()
        {
            if (_disposed)//池已销毁，不再产出新对象（静默返回，避免 GetItemsByCount 循环里刷屏）
                return default;

            T item;
            if (items.Count > 0)
            {
                item = items.Dequeue();
                inactiveItems.Remove(item);
            }
            else
            {
                if (maxSize > 0 && totalCount >= maxSize)
                {
                    _logger.Error($"对象池：已达到最大容量 {maxSize}", new InvalidOperationException($"对象池：已达到最大容量 {maxSize}"));
                    return default;
                }

                item = NewItem();
            }

            item.OnGet();
            _onGet?.Invoke(item);

            return item;
        }

        public List<T> GetItemsByCount(int count)
        {
            List<T> result = new List<T>(count);
            GetItemsByCount(count, result);

            return result;
        }

        public void GetItemsByCount(int count, List<T> result)
        {
            #region 健壮性检查
            if (count < 0)
            {
                _logger.Error("对象池：count 不能为负数", new ArgumentOutOfRangeException(nameof(count)));
                return;
            }

            if (result == null)
            {
                _logger.Error("对象池：result 不能为 null", new ArgumentNullException(nameof(result)));
                return;
            }
            #endregion

            result.Clear();
            for (int i = 0; i < count; i++)
            {
                var item = GetItem();
                if(item == null)
                {
                    return;
                }
                result.Add(item);
            }
        }

        public override void Clear()
        {
            clearPendingItems.Clear();
            clearPendingItems.UnionWith(allItems);
            clearPendingItems.ExceptWith(inactiveItems);

            while (items.Count > 0)
            {
                var item = items.Dequeue();
                inactiveItems.Remove(item);

                ClearItem(item);
            }
        }

        private void ClearItem(T item)
        {
            item.OnClear();
            _onClear?.Invoke(item);

            allItems.Remove(item);
            UnityEngine.Object.Destroy(item.gameObject);

            totalCount--;
        }

        public override void Dispose()
        {
            if (_disposed)
                return;

            Clear();//此时 _disposed 仍为 false，Clear 内部不会被短路（顺序不能颠倒）
            items.Clear();
            inactiveItems.Clear();
            allItems.Clear();
            clearPendingItems.Clear();

            _disposed = true;
        }
    }
}
