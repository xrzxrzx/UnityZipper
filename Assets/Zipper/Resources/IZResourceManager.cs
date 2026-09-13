using Cysharp.Threading.Tasks;
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Zipper.Resources.Asset;

namespace Zipper.Resources
{
    public interface IZResourceManager : IDisposable
    {
        UniTask InitializeAsync(CancellationToken ct);
        UniTask<AssetHandle<T>> LoadAssetAsync<T>(string address, [CallerMemberName] string owner = "", CancellationToken ct = default);
        void Release<T>(AssetHandle<T> handle);
        UniTask<PrefabAsset> LoadPrefabAsync(string address, [CallerMemberName] string owner = "", CancellationToken ct = default);
    }
}