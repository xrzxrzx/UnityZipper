using System;
using UnityEngine;

namespace Zipper.Pool
{
    public sealed class ZPoolOptions<T> where T : Component, IZObjectPoolItem
    {
        public GameObject Prefab;
        public int InitialSize = 0;
        public int MaxSize = 0;

        //暂不实现
        //public ZPoolOverflowPolicy Overflow = ZPoolOverflowPolicy.Throw;

        public Action<T> OnInitialize;
        public Action<T> OnGet;
        public Action<T> OnReturn;
        public Action<T> OnClear;
    }
}