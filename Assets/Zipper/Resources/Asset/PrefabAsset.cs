using UnityEngine;

namespace Zipper.Resources.Asset
{
    public sealed class PrefabAsset : System.IDisposable
    {
        public bool IsReleased => _handle.IsReleased;

        public string Address { get; } = string.Empty;
        
        private GameObject _prefab;
        public GameObject Prefab { get => IsReleased ? null : _prefab; private set => _prefab = value; }
        
        private AssetHandle<GameObject> _handle;
        public AssetHandle<GameObject> Handle { get => _handle; private set => _handle = value; }

        internal PrefabAsset(AssetHandle<GameObject> handle)
        {
            Address = handle.Address;
            Handle = handle;
            Prefab = handle.Asset;
        }

        public GameObject Instantiate(Transform parent = null, bool worldPositionStays = false)
            => Object.Instantiate(Prefab, parent, worldPositionStays);

        public void Dispose() => _handle.Release();
    }
}