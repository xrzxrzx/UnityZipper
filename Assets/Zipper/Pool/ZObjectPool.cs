using System.Collections.Generic;
using UnityEngine;

namespace Zipper.Pool
{
    /// <summary>
    /// 对象池
    /// 在使用Get、Return、Clear等方法时，触发对象的初始化、获取、归还和清理操作。为了提供更灵活的自定义行为，可以通过委托来定义这些操作。
    /// 触发逻辑：先调用对象的OnXXX方法，再调用委托方法。
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class ZObjectPool<T> where T : Component, IZObjectPoolItem
    {
        public delegate void InitializeActionDelegate(T item);
        public delegate void GetActionDelegate(T item);
        public delegate void ReturnActionDelegate(T item);
        public delegate void ClearActionDelegate(T item);

        InitializeActionDelegate InitializeAction;
        GetActionDelegate GetAction;
        ReturnActionDelegate ReturnAction;
        ClearActionDelegate ClearAction;

        GameObject prefab;
        Queue<T> items;
        HashSet<T> inactiveItems;
        HashSet<T> allItems;
        HashSet<T> clearPendingItems;
        int maxSize;
        int totalCount;

        public int CountInactive => items.Count;
        public int CountAll => totalCount;
        public int CountActive => totalCount - items.Count;

        public ZObjectPool(GameObject prefab, int initialSize = 0, int maxSize = 0, InitializeActionDelegate initializeAction = null, GetActionDelegate getAction = null, ReturnActionDelegate returnAction = null, ClearActionDelegate clearAction = null)
        {
            #region 健壮性检查
            if (prefab == null)
            {
                throw new System.ArgumentNullException(nameof(prefab));
            }

            if (prefab.GetComponent<T>() == null)
            {
                throw new System.Exception($"对象池：预制体 {prefab.name} 缺少组件 {typeof(T).Name}");
            }

            if (initialSize < 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(initialSize));
            }

            if (maxSize < 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(maxSize));
            }

            if (maxSize > 0 && initialSize > maxSize)
            {
                throw new System.ArgumentException("initialSize 不能大于 maxSize");
            }
            #endregion

            this.prefab = prefab;
            this.maxSize = maxSize;
            InitializeAction = initializeAction;
            GetAction = getAction;
            ReturnAction = returnAction;
            ClearAction = clearAction;

            items = new Queue<T>(initialSize);
            inactiveItems = new HashSet<T>(initialSize);
            allItems = new HashSet<T>(initialSize);
            clearPendingItems = new HashSet<T>(initialSize);

            InitializeItems(initialSize);
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
            var tItem = item as T;
            if (tItem == null)
            {
                throw new System.InvalidCastException($"对象池：归还对象类型不匹配，期望 {typeof(T).Name}");
            }

            if (inactiveItems.Contains(tItem))
            {
                return;
            }

            if (!allItems.Contains(tItem))
            {
                throw new System.InvalidOperationException($"对象池：对象 {tItem.name} 不属于当前对象池");
            }

            tItem.OnReturn();
            ReturnAction?.Invoke(tItem);

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
            var item = Object.Instantiate(prefab).GetComponent<T>();
            item.ReturnToPool = ReturnItemToPool;

            item.OnInitialize();
            InitializeAction?.Invoke(item);
            allItems.Add(item);
            totalCount++;

            return item;
        }

        public T GetItem()
        {
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
                    throw new System.InvalidOperationException($"对象池：已达到最大容量 {maxSize}");
                }

                item = NewItem();
            }

            item.OnGet();
            GetAction?.Invoke(item);

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
            if (count < 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(count));
            }

            if (result == null)
            {
                throw new System.ArgumentNullException(nameof(result));
            }

            result.Clear();
            for (int i = 0; i < count; i++)
            {
                result.Add(GetItem());
            }
        }

        public void Clear()
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
            ClearAction?.Invoke(item);

            allItems.Remove(item);
            Object.Destroy(item.gameObject);

            totalCount--;
        }
    }
}
