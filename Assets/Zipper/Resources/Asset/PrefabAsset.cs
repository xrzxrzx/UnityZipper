using UnityEngine;

namespace Zipper.Resources
{
    public sealed class PrefabAsset : System.IDisposable
    {
        public string Address { get; } = string.Empty;
        public GameObject Prefab { get; } = null;
        readonly AssetHandle<GameObject> _handle;
        
        internal PrefabAsset(AssetHandle<GameObject> handle)
        {
            Address = handle.Address;
            _handle = handle;
            Prefab = handle.Asset;
        }

        public GameObject Instantiate(Transform parent = null, bool worldPositionStays = false)
            => Object.Instantiate(Prefab, parent, worldPositionStays);

        public void Dispose() => _handle.Release();
    }
}