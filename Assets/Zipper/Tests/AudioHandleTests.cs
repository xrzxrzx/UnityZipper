using NUnit.Framework;
using Zipper.Audio;
using Zipper.Tests.Fakes;

namespace Zipper.Tests
{
    /// <summary>
    /// <see cref="ZAudioHandle"/> 的"BGM 模式"语义（构造时 _item == null）。
    /// 池化模式（_item != null）需要真实 PooledAudioSource 与其 Unity 播放生命周期 → 属 Editor 手工试听范畴。
    /// </summary>
    public class AudioHandleTests
    {
        ZAudioManager _manager;

        [SetUp]
        public void SetUp()
            => _manager = new ZAudioManager(new FakeObjectPoolManager(), new FakeResourceManager(),
                                           new FakeLogger(), new ZAudioOptions());

        [TearDown]
        public void TearDown() => _manager.Dispose();

        [Test]
        public void BgmHandle_WithoutAttachedRoot_IsNotPlaying_ButIsFinished()
        {
            var handle = new ZAudioHandle(_manager, null);

            Assert.IsFalse(handle.IsPlaying, "没有 BGM 载体时不应报告在播");
            Assert.IsTrue(handle.IsFinished, "BGM 句柄不走池化路径 → _item == null 即视为已结束（tick 不该回收它）");
        }

        [Test]
        public void Stop_WithoutAttachedRoot_DoesNotThrow()
        {
            var handle = new ZAudioHandle(_manager, null);

            Assert.DoesNotThrow(() => handle.Stop());
        }

        [Test]
        public void Stop_IsIdempotent()
        {
            var handle = new ZAudioHandle(_manager, null);

            handle.Stop();
            Assert.DoesNotThrow(() => handle.Stop(), "重复 Stop 必须幂等（§13-6）");
        }

        [Test]
        public void MarkFinished_IsIdempotent()
        {
            var handle = new ZAudioHandle(_manager, null);

            handle.MarkFinished();
            Assert.DoesNotThrow(() => handle.MarkFinished(), "重复标记完成不该抛（UniTaskCompletionSource 重复 SetResult 会抛）");
        }

        [Test]
        public void WaitFinishedAsync_CompletesImmediately_AfterMarkFinished()
        {
            var handle = new ZAudioHandle(_manager, null);
            handle.MarkFinished();

            Assert.DoesNotThrow(() => handle.WaitFinishedAsync().GetAwaiter().GetResult());
        }
    }
}
