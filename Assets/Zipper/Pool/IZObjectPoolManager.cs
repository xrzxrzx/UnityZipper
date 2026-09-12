using System.Collections.Generic;
using UnityEngine;

namespace Zipper.Pool
{
    /// <summary>
    /// 目前仅支持一种对象一个池，后续可以拓展为一种对象多个池
    /// </summary>
    public interface IZObjectPoolManager : System.IDisposable
    {
        void CreatePool<T>(ZPoolOptions<T> options) where T : Component, IZObjectPoolItem;
        void DestroyPool<T>();
        void ClearPool<T>();

        T Get<T>() where T : Component, IZObjectPoolItem;
        List<T> GetItemsByCount<T>(int count) where T : Component, IZObjectPoolItem;
        void GetItemsByCount<T>(int count, List<T> result) where T : Component, IZObjectPoolItem;
        void Return<T>(T item) where T : Component, IZObjectPoolItem;

        ZPoolsState GetPoolsState();
    }
}