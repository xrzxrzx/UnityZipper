using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using Zipper.Resources;

namespace Zipper.Resources
{
    public interface IZResourceManager : IDisposable
    {
        UniTask InitializeAsync(CancellationToken ct);
        UniTask<AssetHandle<T>> LoadAssetAsync<T>(string address, string owner, CancellationToken ct);
        void Release<T>(AssetHandle<T> handle);
        UniTask<PrefabAsset> LoadPrefabAsync(string address, string owner, CancellationToken ct);
    }
}