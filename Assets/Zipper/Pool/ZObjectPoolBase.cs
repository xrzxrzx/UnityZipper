using UnityEngine;

namespace Zipper.Pool
{
    public abstract class ZObjectPoolBase : System.IDisposable
    {
        public abstract int CountAll { get; }
        public abstract int CountActive { get; }
        public abstract int CountInactive { get; }
        public abstract void Clear();
        public abstract void Dispose();

        public abstract GameObject Prefab { get; }
    }
}