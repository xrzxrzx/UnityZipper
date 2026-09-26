using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Zipper.Resources;
using Zipper.Resources.Asset;

namespace Zipper.Tests.Fakes
{
    /// <summary>
    /// 假资源管理器：可控地模拟"加载失败（抛异常）"与"加载成功但无资源（null handle）"。
    /// 用来验证音频侧"失败不抛"的契约。
    ///
    /// 限制：<see cref="AssetHandle{T}"/> 的构造函数是 internal（只能由 ZResourceManager 签发），
    /// 所以本假实现无法造出"真实可用的句柄" → 成功路径（真的拿到 clip）属手工试听范畴。
    /// </summary>
    internal sealed class FakeResourceManager : IZResourceManager
    {
        /// <summary>按地址决定抛什么异常；返回 null 表示"不抛，但返回 null handle"。</summary>
        public Func<string, Exception> ThrowOn;

        public int LoadCallCount { get; private set; }
        public int ReleaseCallCount { get; private set; }

        public UniTask InitializeAsync(CancellationToken ct) => UniTask.CompletedTask;

        public async UniTask<AssetHandle<T>> LoadAssetAsync<T>(string address, string owner = "", CancellationToken ct = default)
        {
            LoadCallCount++;

            await UniTask.CompletedTask;      // 使其成为真正的 async 方法（throw 语义更直观）

            var ex = ThrowOn?.Invoke(address);
            if (ex != null)
                throw ex;

            return null;                      // 假实现拿不到真句柄（构造 internal）
        }

        public void Release<T>(AssetHandle<T> handle) => ReleaseCallCount++;

        public async UniTask<PrefabAsset> LoadPrefabAsync(string address, string owner = "", CancellationToken ct = default)
        {
            await UniTask.CompletedTask;
            return null;
        }

        public void Dispose() { }
    }
}
