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

        public void Preload(string address) => _audio.PreloadAsync(address).Forget();
    }
}