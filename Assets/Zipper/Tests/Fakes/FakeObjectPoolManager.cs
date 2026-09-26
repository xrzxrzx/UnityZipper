using System.Collections.Generic;
using UnityEngine;
using Zipper.Pool;

namespace Zipper.Tests.Fakes
{
    /// <summary>
    /// 假对象池管理器：只为了让 ZAudioManager 能被构造起来（音量/选项等纯逻辑测试用），不做真实池化。
    /// <see cref="Get{T}"/> 恒返回 null —— 正好模拟"池满/无池"的失败分支。
    /// </summary>
    internal sealed class FakeObjectPoolManager : IZObjectPoolManager
    {
        public readonly List<IZObjectPoolItem> Returned = new List<IZObjectPoolItem>();

        public void CreatePool<T>(ZPoolOptions<T> options) where T : Component, IZObjectPoolItem { }

        // 注意：接口 IZObjectPoolManager 里 DestroyPool/ClearPool 的泛型参数【没有约束】
        // （与 CreatePool/Get/Return 不同）→ 实现必须与接口完全一致，否则 CS0425。
        public void DestroyPool<T>() { }

        public void ClearPool<T>() { }

        public T Get<T>() where T : Component, IZObjectPoolItem => null;

        public List<T> GetItemsByCount<T>(int count) where T : Component, IZObjectPoolItem => new List<T>(0);

        public void GetItemsByCount<T>(int count, List<T> result) where T : Component, IZObjectPoolItem => result.Clear();

        public void Return<T>(T item) where T : Component, IZObjectPoolItem => Returned.Add(item);

        /// <summary>本批测试不覆盖 GetState()（需要真实池），故返回 null。</summary>
        public ZPoolsState GetPoolsState() => null;

        public void Dispose() { }
    }
}
