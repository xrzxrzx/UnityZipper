using Cysharp.Threading.Tasks;
using System.Threading;
using Zipper.Audio.Internal;

namespace Zipper.Audio
{
    public class ZAudioHandle
    {
        ZAudioManager _manager;
        PooledAudioSource _item;                       // internal 引用，对外不暴露
        UniTaskCompletionSource _finished = new UniTaskCompletionSource();
        bool _finishedFlag;

        public bool IsPlaying => !_finishedFlag && (_item != null ? _item.IsPlaying : _manager.IsBgmPlaying);
        internal PooledAudioSource Item => _item;
        internal bool IsFinished => _finishedFlag || _item == null || (!_item.IsPaused && !_item.IsPlaying);

        internal ZAudioHandle(ZAudioManager manager, PooledAudioSource item)
        {
            _manager = manager;
            _item = item;
            _finishedFlag = false;
        }

        public void Stop(float fadeOutSeconds = 0f)
        {
            _manager.Recycle(this, fadeOutSeconds);
        }

        public void Pause()
        {
            if (IsPlaying)
                _item.PauseSource();
        }

        public void Resume()
        {
            if (_finishedFlag || _item == null)
                return;

            _item.ResumeSource();
        }

        public UniTask WaitFinishedAsync(CancellationToken ct = default)
        {
            return _finished.Task.AttachExternalCancellation(ct);
        }

        internal void MarkFinished()
        {
            if (_finishedFlag)
                return;

            _finishedFlag = true;
            _finished.TrySetResult();
        }
    }
}