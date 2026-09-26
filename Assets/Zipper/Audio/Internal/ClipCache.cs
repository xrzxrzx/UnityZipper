using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Zipper.Core.Logging;
using Zipper.Resources;
using Zipper.Resources.Asset;

namespace Zipper.Audio.Internal
{
    internal sealed class ClipCache
    {
        IZResourceManager _resourceManager;
        IZLogger _logger;

        Dictionary<string, AssetHandle<AudioClip>> _map;

        public ClipCache(IZResourceManager resourceManager, IZLogger logger)
        {
            _resourceManager = resourceManager;
            _logger = logger;

            _map = new Dictionary<string, AssetHandle<AudioClip>>();
        }

        public async UniTask<AssetHandle<AudioClip>> GetOrLoadAsync(string address, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(address))
            {
                _logger.Error("音频地址为空");
                return null;
            }

            if (_map.TryGetValue(address, out var cached) && cached.Asset != null)
                return cached;

            try
            {
                var handle = await _resourceManager.LoadAssetAsync<AudioClip>(address, "ZAudioManager", ct);

                if (handle == null || handle.Asset == null)
                {
                    _logger.Error($"音频加载为空：{address}");
                    return null;
                }

                if (_map.TryGetValue(address, out var existing) && existing.Asset != null)
                {
                    handle.Release();
                    return existing;
                }

                _map[address] = handle;
                return handle;
            }
            catch (System.OperationCanceledException)
            {
                return null;
            }
            catch (System.Exception ex)
            {
                _logger.Error($"音频加载失败：{address}", ex);
                return null;
            }
        }

        public bool IsCached(string address) => _map.TryGetValue(address, out var h) && h.Asset != null;

        public void ReleaseAll()
        {
            foreach (var handle in _map.Values)
                handle.Release();
            _map.Clear();
        }
    }
}