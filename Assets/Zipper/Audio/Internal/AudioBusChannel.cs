using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;
using UnityEngine.Audio;
using Zipper.Core.Logging;

namespace Zipper.Audio.Internal
{
    internal sealed class AudioBusChannel
    {
        readonly AudioSource _a, _b;
        readonly ZAudioOptions _options;
        readonly IZLogger _logger;

        AudioSource _current;
        AudioSource _crossPrev;
        CancellationTokenSource _crossCts;
        float _targetVolume = 1f;

        public string CurrentAddress { get; private set; }
        public bool IsPlaying => _current != null && _current.isPlaying;
        public float Crossfade01 { get; private set; }

        public AudioBusChannel(GameObject host, ZAudioOptions options, IZLogger logger)
        {
            _options = options; _logger = logger;

            _a = host.AddComponent<AudioSource>();
            _b = host.AddComponent<AudioSource>();
            foreach (var s in new[] { _a, _b })
            {
                s.playOnAwake = false;//否则一挂上就尝试播
                s.loop = true;//BGM 默认循环
                s.volume = 0f;
            }
        }

        public async UniTask PlayAsync(AudioClip clip, string address, float crossfadeSeconds, AudioMixerGroup group, float volume, CancellationToken ct)
        {
            _targetVolume = Mathf.Clamp01(volume);

            CancelCross();

            var next = (_current == _a) ? _b : _a;
            var prev = _current;

            if (next == null)     // 宿主已被销毁（退出播放/卸载）→ 放弃这次播放，不碰 Unity 对象
            {
                _current = null;
                CurrentAddress = null;
                Crossfade01 = 0f;
                return;
            }

            next.clip = clip;
            next.outputAudioMixerGroup = group;
            next.volume = 0f;
            next.Play();
            _current = next;
            CurrentAddress = address;

            if (prev == null || crossfadeSeconds <= 0f)
            {
                if (prev != null) { prev.Stop(); prev.clip = null; }
                next.volume = _targetVolume;
                Crossfade01 = 1f;
                return;
            }

            await CrossFadeAsync(prev, next, crossfadeSeconds, ct);
        }

        private async UniTask CrossFadeAsync(AudioSource prev, AudioSource next, float seconds, CancellationToken ct)
        {
            CancelCross();
            _crossCts = new CancellationTokenSource();
            var token = _crossCts.Token;

            if (prev == null || next == null)      // 宿主已被销毁 → 不进入交叉
            {
                CancelCross();
                return;
            }

            float prevStart = prev.volume;
            float t = 0f;

            _crossPrev = prev;
            try
            {
                while (t < seconds)
                {
                    if (token.IsCancellationRequested || ct.IsCancellationRequested) return;

                    if (prev == null || next == null)   // 交叉途中宿主被销毁（退出播放/卸载）
                        return;

                    t += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(t / seconds);

                    next.volume = Mathf.Lerp(0f, _targetVolume, k);
                    prev.volume = Mathf.Lerp(prevStart, 0f, k);
                    Crossfade01 = k;

                    await UniTask.Yield(PlayerLoopTiming.Update);
                }

                next.volume = _targetVolume;
                Crossfade01 = 1f;
            }
            finally
            {
                if (!token.IsCancellationRequested && prev != null)
                {
                    prev.Stop();
                    prev.clip = null;
                    prev.volume = 0f;
                }

                if(_crossPrev == prev)
                    _crossPrev = null;
            }
        }

        public void SetVolume(float volume)
        {
            _targetVolume = Mathf.Clamp01(volume);

            if (_crossCts == null && _current != null)
                _current.volume = _targetVolume;
        }

        public void Stop(float fadeOutSeconds = 0f)
        {
            CancelCross();

            if (_current != null)
            {
                _current.Stop();
                _current.clip = null;
                _current.volume = 0f;
            }

            _current = null;
            CurrentAddress = null;
            Crossfade01 = 0f;
        }

        private void CancelCross()
        {
            if (_crossPrev != null)
            {
                _crossPrev.Stop();
                _crossPrev.clip = null;
                _crossPrev.volume = 0f;
                _crossPrev = null;
            }

            if (_crossCts != null)
            {
                _crossCts.Cancel();
                _crossCts.Dispose();
                _crossCts = null;
            }
        }
    }
}