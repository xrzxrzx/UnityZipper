using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;
using UnityEngine.Audio;
using Zipper.Audio.Internal;
using Zipper.Core.Boot;
using Zipper.Core.Logging;
using Zipper.Pool;
using Zipper.Resources;
using Zipper.Resources.Asset;

namespace Zipper.Audio
{
    public class ZAudioBootstrapper : IZModuleBootstrap, System.IDisposable
    {
        public ZBootPhase Phase => ZBootPhase.Audio;

        IZLogger _logger;
        IZObjectPoolManager _poolManager;
        IZResourceManager _resourceManager;
        IZAudioManager _audioManager;
        ZAudioOptions _options;

        GameObject _root;
        GameObject _prototype;
        AssetHandle<AudioMixer> _mixerHandle;

        public ZAudioBootstrapper(IZLogger logger, IZObjectPoolManager poolManager, IZResourceManager resourceManager, IZAudioManager audioManager, ZAudioOptions options)
        {
            _logger = logger;
            _poolManager = poolManager;
            _resourceManager = resourceManager;
            _audioManager = audioManager;
            _options = options;
        }

        public async UniTask InitializeAsync(CancellationToken ct)
        {
            if (_root != null)
                return;

            _root = new GameObject("[Zipper] AudioRoot");
            _audioManager.AttachRoot(_root);
            Object.DontDestroyOnLoad(_root);

            _prototype = new GameObject("[Zipper] AudioSourcePrototype");
            _prototype.transform.SetParent(_root.transform);
            _prototype.SetActive(false);
            _prototype.AddComponent<PooledAudioSource>();

            _poolManager.CreatePool(new ZPoolOptions<PooledAudioSource>
            {
                Prefab = _prototype,
                InitialSize = _options.InitialPoolSize,
                MaxSize = _options.MaxPoolSize,
            });

            if (_options.Mixer == null && !string.IsNullOrEmpty(ZAudioOptions.MixerAddress))
            {
                _mixerHandle = await _resourceManager.LoadAssetAsync<AudioMixer>(
                    ZAudioOptions.MixerAddress, "ZAudioBootstrapper", ct);    
                _options.Mixer = _mixerHandle?.Asset;

                if (_options.Mixer == null)
                    _logger.Warning($"AudioMixer 加载失败，音量分组降级：{ZAudioOptions.MixerAddress}");
            }

            return;
        }

        public void Dispose()
        {
            _audioManager.Dispose();
            _poolManager.DestroyPool<PooledAudioSource>();
            _mixerHandle?.Release();

            if (_prototype != null)
            {
                Object.Destroy(_prototype);
                _prototype = null;
            }

            if (_root != null)
            {
                Object.Destroy(_root);
                _root = null;
            }
        }
    }
}