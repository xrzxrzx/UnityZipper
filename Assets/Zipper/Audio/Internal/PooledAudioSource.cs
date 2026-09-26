using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;
using UnityEngine.Audio;
using Zipper.Pool;

namespace Zipper.Audio.Internal
{
    internal sealed class PooledAudioSource : MonoBehaviour, IZObjectPoolItem
    {
        public IZObjectPoolItem.ReturnToPoolDelegate ReturnToPool { get; set; }

        AudioSource _source;
        CancellationTokenSource _cts;
        CancellationToken _ct;

        bool _paused;
        public bool IsPaused => _paused;

        public bool IsPlaying => _source != null && _source.isPlaying;

        public void OnInitialize()
        {
            EnsureSource();
            gameObject.SetActive(false);
            DontDestroyOnLoad(gameObject);
        }

        public void OnGet()
        {
            gameObject.SetActive(true);
            ResetState();
            _cts = new CancellationTokenSource();
            _ct = _cts.Token;
        }

        public void OnReturn()
        {
            CancelAction();
            _source.Stop();
            ResetState();
            gameObject.SetActive(false);
        }

        public void OnClear()
        {
            CancelAction();
        }

        private void EnsureSource()
        {
            if (!TryGetComponent(out _source))
                _source = gameObject.AddComponent<AudioSource>();
        }

        private void ResetState()
        {
            _source.Stop();
            _source.clip = null;
            _source.volume = 0f;
            _source.pitch = 1f;
            _source.loop = false;
            _source.spatialBlend = 0f;
            _source.outputAudioMixerGroup = null;
            _paused = false;
        }

        public void Play(AudioClip clip, float volume, float pitch, bool loop,
                     float spatialBlend, bool hasPosition, Vector3 position,
                     AudioMixerGroup group, float fadeInSeconds)
        {
            if (hasPosition) transform.position = position;
            _source.clip = clip;
            _source.pitch = pitch;
            _source.loop = loop;
            _source.spatialBlend = spatialBlend;
            _source.outputAudioMixerGroup = group;

            if (fadeInSeconds > 0f)
            {
                _source.volume = 0f;
                _source.Play();
                FadeToAsync(volume, fadeInSeconds).Forget();
            }
            else
            {
                _source.volume = volume;
                _source.Play();
            }
        }

        public async UniTask FadeToAsync(float target, float seconds)
        {
            var ct = _ct;
            float from = _source.volume;
            float t = 0f;

            while (t < seconds)
            {
                if (ct.IsCancellationRequested)
                    return;

                if (_source == null)
                    return;

                if (!_source.isPlaying)
                    return;

                t += Time.unscaledDeltaTime;
                _source.volume = Mathf.Lerp(from, target, Mathf.Clamp01(t / seconds));

                await UniTask.Yield(PlayerLoopTiming.Update);
            }
            if (!ct.IsCancellationRequested && _source != null)
                _source.volume = target;
        }

        public void PauseSource()
        {
            if (_source == null || _paused) return;
            _source.Pause();
            _paused = true;
        }

        public void ResumeSource()
        {
            if (_source == null || !_paused) return;
            _source.UnPause();
            _paused = false;
        }

        private void CancelAction()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
        }
    }
}