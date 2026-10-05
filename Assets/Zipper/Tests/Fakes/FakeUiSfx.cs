using System.Collections.Generic;
using Zipper.UI;

namespace Zipper.Tests.Fakes
{
    /// <summary>
    /// 假音效接口：只记录调用，不播音（UI 模块可独立测试的前提 ——D8 方案 ② 的价值）。
    /// </summary>
    public sealed class FakeUiSfx : IZUISfx
    {
        public readonly List<string> Played = new List<string>();
        public readonly List<string> Preloaded = new List<string>();

        public void Play(string address) => Played.Add(address);
        public void Preload(string address) => Preloaded.Add(address);
    }
}
