using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Zipper.Core.Logging;
using Zipper.Resources.Asset;

namespace Zipper.Resources
{
    public class ZResourceManager : IZResourceManager
    {
        readonly AsyncLazy<bool> _initLazy;

        readonly HashSet<AssetHandleBase> _book;

        IZLogger _logger;

        public ZResourceManager(IZLogger logger)
        {
            _logger = logger;
            _initLazy = new AsyncLazy<bool>(InitializeCoreAsync);
            _book = new HashSet<AssetHandleBase>();
        }

        public async UniTask InitializeAsync(CancellationToken ct = default)
        {
            await _initLazy.Task.AttachExternalCancellation(ct);
        }

        static async UniTask<bool> InitializeCoreAsync()
        {
            await Addressables.InitializeAsync().ToUniTask();

            return true;
        }

        /// <summary>
        /// 异步加载资源
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="address"></param>
        /// <param name="owner">传nameof()</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        /// <exception cref="System.ArgumentNullException"></exception>
        public async UniTask<AssetHandle<T>> LoadAssetAsync<T>(string address, [CallerMemberName]string owner = "", CancellationToken ct = default)
        {
            if(string.IsNullOrEmpty(address))
                throw new System.ArgumentNullException(nameof(address));

            var inner = Addressables.LoadAssetAsync<T>(address);

            try
            {
                await inner.ToUniTask(cancellationToken: ct);
                var handle = new AssetHandle<T>(address, owner, h => _book.Remove(h), inner);

                _book.Add(handle);
                return handle;
            }
            catch(System.OperationCanceledException ex)
            {
                Addressables.Release(inner);
                _logger.Error($"资源 {address} 加载被取消, 持有者: {owner}", ex);
                throw;
            }
            catch(System.Exception ex)
            {
                Addressables.Release(inner);
                _logger.Error($"资源 {address} 加载失败, 持有者: {owner}", ex);
                throw;
            }
        }

        public void Release<T>(AssetHandle<T> handle) => handle?.Release();

        /// <summary>
        /// 异步加载Prefab资源
        /// </summary>
        /// <param name="address"></param>
        /// <param name="owner">传nameof()</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async UniTask<PrefabAsset> LoadPrefabAsync(string address, [CallerMemberName] string owner = "", CancellationToken ct = default)
        {
            var handle = await LoadAssetAsync<GameObject>(address, owner, ct);
            return new PrefabAsset(handle);
        }

        public void Dispose()
        {
            var bookArray = _book.ToArray();
            _book.Clear();
            foreach (var handle in bookArray)
            {
                _logger.Warning($"资源 {handle.Address} 未被释放, 持有者: {handle.Owner}，已由管理器释放");
                handle.Dispose();
            }
        }
    }
}