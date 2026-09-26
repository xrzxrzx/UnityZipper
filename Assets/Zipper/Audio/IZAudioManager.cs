using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;

namespace Zipper.Audio
{
    public interface IZAudioManager : IDisposable
    {
        void AttachRoot(GameObject root);

        void PlaySfx(string address, in ZAudioPlayOptions options = default);
        void PlaySfx(string address, Vector3 position, in ZAudioPlayOptions options = default);

        UniTask<ZAudioHandle> PlaySfxAsync(string address, ZAudioPlayOptions options = default, CancellationToken ct = default);
        UniTask<ZAudioHandle> PlayBgmAsync(string address, float crossfadeSeconds = 0f, CancellationToken ct = default);

        UniTask PreloadAsync(string address, CancellationToken ct = default);

        void ReleaseAllClips();

        float GetVolume(ZAudioBus bus);
        void SetVolume(ZAudioBus bus, float volume);

        ZAudioState GetState();
    }
}