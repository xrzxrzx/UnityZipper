using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Audio;
using VContainer.Unity;
using Zipper.Audio.Internal;
using Zipper.Core.Logging;
using Zipper.Pool;
using Zipper.Resources;

namespace Zipper.Audio
{
    public class ZAudioManager : IZAudioManager, ITickable
    {
        IZObjectPoolManager _poolManager;
        IZResourceManager _resourceManager;
        IZLogger _logger;
        ZAudioOptions _options;
        ClipCache _clipCache;

        GameObject _root;
        GameObject _bgmObject;
        AudioBusChannel _bgm;

        float[] _volumes = new float[5];
        bool _volumesLoaded;

        List<ZAudioHandle> _playing;
        bool _disposed;
        int _mainThreadId;

        static readonly ZAudioPlayOptions DefaultOptions = new ZAudioPlayOptions();

        internal bool IsBgmPlaying => _bgm != null && _bgm.IsPlaying;

        // internal（而非 private）：供 EditMode 测试直接断言（见 Zipper.Tests；由 AssemblyInfo.cs 的 InternalsVisibleTo 打通）
        internal static float ToDb(float v01) => v01 <= 0.0001f ? -80f : Mathf.Log10(v01) * 20f;
        internal static string VolumeKey(ZAudioBus bus) => $"Zipper.Audio.Volume.{bus}";

        public ZAudioManager(IZObjectPoolManager poolManager, IZResourceManager resourceManager, IZLogger logger, ZAudioOptions options)
        {
            _poolManager = poolManager;
            _resourceManager = resourceManager;
            _logger = logger;
            _options = options;
            _clipCache = new ClipCache(_resourceManager, _logger);

            _playing = new List<ZAudioHandle>();
            _disposed = false;
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public void AttachRoot(GameObject root)
        {
            _root = root;
            _bgmObject = new GameObject("[Zipper] AudioBgm");
            _bgmObject.transform.SetParent(root.transform);
            _bgm = new AudioBusChannel(_bgmObject, _options, _logger);
        }

        public void PlaySfx(string address, in ZAudioPlayOptions options = default)
        {
            PlaySfxAsync(address, options).Forget();
        }

        public void PlaySfx(string address, Vector3 position, in ZAudioPlayOptions options = default)
        {
            PlaySfxCoreAsync(address, options ?? DefaultOptions, true, position, default).Forget();
        }

        public UniTask<ZAudioHandle> PlaySfxAsync(string address, ZAudioPlayOptions options = default, CancellationToken ct = default)
        {
            if (!CheckMainThread())
                return UniTask.FromResult<ZAudioHandle>(default);

            if (_disposed)
                return UniTask.FromResult<ZAudioHandle>(default);

            return PlaySfxCoreAsync(address, options ?? DefaultOptions, false, default, ct);
        }

        private async UniTask<ZAudioHandle> PlaySfxCoreAsync(string address, ZAudioPlayOptions opts, bool hasPosition, Vector3 position, CancellationToken ct)
        {
            var handle = await _clipCache.GetOrLoadAsync(address, ct);
            if (handle == null)
                return default;

            if (ct.IsCancellationRequested)
                return default;

            if (_disposed)
                return default;

            var item = _poolManager.Get<PooledAudioSource>();
            if (item == null)
            {
                _logger.Warning($"音效池已满，丢弃：{address}");
                return default;
            }

            item.Play(handle.Asset, ResolveVolume(opts.Bus, opts.Volume), opts.Pitch, opts.Loop,
                      hasPosition ? 1f : opts.SpatialBlend, hasPosition, position,
                      ResolveGroup(opts.Bus), opts.FadeInSeconds);

            var h = new ZAudioHandle(this, item);
            _playing.Add(h);

            return h;
        }

        private bool CheckMainThread([System.Runtime.CompilerServices.CallerMemberName] string member = null)
        {
            if (Thread.CurrentThread.ManagedThreadId == _mainThreadId)
                return true;

            _logger.Error($"{member} 必须在主线程调用");
            return false;
        }

        float ResolveVolume(ZAudioBus bus, float volume)
        {
            return Mathf.Clamp01(volume);
        }

        public float GetVolume(ZAudioBus bus)
        {
            EnsureVolumesLoaded();
            return _volumes[(int)bus];
        }

        public void SetVolume(ZAudioBus bus, float volume)
        {
            if (!CheckMainThread() || _disposed)
                return;

            EnsureVolumesLoaded();
            volume = float.IsNaN(volume) ? 0f : Mathf.Clamp01(volume);
            _volumes[(int)bus] = volume;

            ApplyVolume(bus);
            PlayerPrefs.SetFloat(VolumeKey(bus), volume);
        }

        private void EnsureVolumesLoaded()
        {
            if (_volumesLoaded)
                return;
            _volumesLoaded = true;

            for (int i = 0; i < _volumes.Length; i++)
            {
                var bus = (ZAudioBus)i;
                _volumes[i] = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey(bus), 1f));
                ApplyVolume(bus);
            }
        }

        private void ApplyVolume(ZAudioBus bus)
        {
            if (_options.Mixer == null)
                return;

            if (!_options.Mixer.SetFloat(_options.GetParam(bus), ToDb(_volumes[(int)bus])))
                _logger.Warning($"Mixer 参数未暴露：{_options.GetParam(bus)}");
        }

        AudioMixerGroup ResolveGroup(ZAudioBus bus)
        {
            if (_options.Mixer == null)
                return null;

            var groups = _options.Mixer.FindMatchingGroups(_options.GetGroupName(bus));
            if (groups == null || groups.Length == 0)
            {
                _logger.Warning($"Mixer 中找不到分组：{_options.GetGroupName(bus)}");
                return null;
            }

            return groups[0];
        }

        internal void Recycle(ZAudioHandle handle, float fadeOutSeconds = 0f)
        {
            if (handle.Item == null)
            {
                _bgm?.Stop(fadeOutSeconds);   // _bgm 仅在 AttachRoot 之后存在（时序异常/测试下可能为 null）
                handle.MarkFinished();
                return;
            }

            if (fadeOutSeconds <= 0f)
            {
                int i = _playing.IndexOf(handle);
                if (i >= 0) RecycleAt(i);
                return;
            }

            FadeOutThenRecycleAsync(handle, fadeOutSeconds).Forget();
        }

        private async UniTask FadeOutThenRecycleAsync(ZAudioHandle handle, float seconds)
        {
            await handle.Item.FadeToAsync(0f, seconds);

            int i = _playing.IndexOf(handle);
            if (i >= 0)
                RecycleAt(i);
        }

        private void RecycleAt(int index)
        {
            var h = _playing[index];
            _playing.RemoveAt(index);
            h.MarkFinished();
            _poolManager.Return(h.Item);
        }

        public UniTask<ZAudioHandle> PlayBgmAsync(string address, float crossfadeSeconds = 0f, CancellationToken ct = default)
        {
            if (!CheckMainThread())
                return UniTask.FromResult<ZAudioHandle>(default);

            if (_disposed)
                return UniTask.FromResult<ZAudioHandle>(default);

            return PlayBgmCoreAsync(address, crossfadeSeconds, ct);
        }

        private async UniTask<ZAudioHandle> PlayBgmCoreAsync(string address, float crossfadeSeconds, CancellationToken ct)
        {
            var handle = await _clipCache.GetOrLoadAsync(address, ct);
            if (handle == null || ct.IsCancellationRequested || _disposed)
                return default;

            await _bgm.PlayAsync(handle.Asset, address, crossfadeSeconds, ResolveGroup(ZAudioBus.Bgm), 1f, ct);

            return new ZAudioHandle(this, null);
        }

        public UniTask PreloadAsync(string address, CancellationToken ct = default)
        {
            if (!CheckMainThread())
                return UniTask.CompletedTask;

            if (_disposed)
                return UniTask.CompletedTask;

            return PreloadCoreAsync(address, ct);
        }

        private async UniTask PreloadCoreAsync(string address, CancellationToken ct)
        {
            await _clipCache.GetOrLoadAsync(address, ct);
        }

        /// <summary>
        /// 正在播放的会被中断
        /// </summary>
        public void ReleaseAllClips()
        {
            if (!CheckMainThread() || _disposed)
                return;

            _clipCache.ReleaseAll();
        }

        public ZAudioState GetState()
        {
            int total = 0, active = 0;
            var pools = _poolManager.GetPoolsState();                     // ZPoolsState
            foreach (var p in pools.PoolStates)
            {
                if (p.Type == typeof(PooledAudioSource))                  // 一类型一池 → 按类型筛
                { total = p.TotalItems; active = p.ActiveItems; break; }
            }

            return new ZAudioState(_playing.Count, total, active, _bgm?.CurrentAddress, _bgm?.IsPlaying ?? false, _bgm?.Crossfade01 ?? 0f);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            for (int i = _playing.Count - 1; i >= 0; i--)
                RecycleAt(i);

            _clipCache.ReleaseAll();

            _bgm?.Stop();
        }

        public void Tick()
        {
            if (_disposed || _playing.Count == 0)
                return;

            for (int i = _playing.Count - 1; i >= 0; i--)
            {
                var h = _playing[i];
                if (!h.IsFinished)
                    continue;

                RecycleAt(i);
            }
        }
    }
}