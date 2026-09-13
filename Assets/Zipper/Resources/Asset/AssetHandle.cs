using UnityEngine.ResourceManagement.AsyncOperations;

namespace Zipper.Resources.Asset
{
    public sealed class AssetHandle<T> : AssetHandleBase
    {
        private T _asset = default;
        public T Asset { get => IsReleased ? default : _asset; private set => _asset = value; }

        readonly AsyncOperationHandle<T> _inner;
        readonly System.Action<AssetHandle<T>> _unbook;

        internal AssetHandle(string address, string owner, System.Action<AssetHandle<T>> unbook, AsyncOperationHandle<T> inner)
        {
            Address = address;
            Owner = owner;
            _unbook = unbook;
            _inner = inner;
            Asset = inner.Result;
        }

        public override void Release()
        {
            if (IsReleased) return;
            IsReleased = true;
            _unbook?.Invoke(this);
            _inner.Release();
        }
    }
}