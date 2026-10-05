using Cysharp.Threading.Tasks;
using Zipper.Audio;
using Zipper.UI;

namespace Zipper.DI
{
    public sealed class ZUiSfxAdapter : IZUISfx
    {
        readonly IZAudioManager _audio;

        public ZUiSfxAdapter(IZAudioManager audio) => _audio = audio;

        public void Play(string address) => _audio.PlaySfx(address);

        // PreloadAsync 返回【非泛型 UniTask】→ 它没有 Forget()（那是 UniTask<T>/UniTaskVoid 的扩展）✗
        // 非泛型 UniTask 直接调用即是 fire-and-forget：它本身不参与 await，失败由 UniTask 调度器上报
        // （音频侧已先记 Error）✓
        public void Preload(string address)
        {
            _audio.PreloadAsync(address);
        }
    }
}