using NUnit.Framework;
using UnityEngine;
using Zipper.Audio;
using Zipper.Tests.Fakes;

namespace Zipper.Tests
{
    /// <summary>
    /// 音量体系测试：dB 换算 / 夹紧 / 默认值 / 持久化 / 分组隔离。
    /// 覆盖设计稿 audio-manager-design v0.2 §7.2、§7.3 与 §13-9。
    /// 说明：本组测试要求 <see cref="ZAudioManager.ToDb"/> 与 <see cref="ZAudioManager.VolumeKey"/> 为 internal（由 InternalsVisibleTo 打通）。
    /// </summary>
    public class AudioVolumeTests
    {
        static readonly ZAudioBus[] AllBuses =
        {
            ZAudioBus.Master, ZAudioBus.Bgm, ZAudioBus.Sfx, ZAudioBus.Voice, ZAudioBus.Ui
        };

        FakeLogger _logger;
        FakeObjectPoolManager _pool;
        FakeResourceManager _resources;
        ZAudioOptions _options;
        ZAudioManager _manager;

        [SetUp]
        public void SetUp()
        {
            ClearVolumePrefs();                       // 先清，保证"默认满音量"可断言

            _logger = new FakeLogger();
            _pool = new FakeObjectPoolManager();
            _resources = new FakeResourceManager();
            _options = new ZAudioOptions();
            _manager = new ZAudioManager(_pool, _resources, _logger, _options);
        }

        [TearDown]
        public void TearDown()
        {
            _manager.Dispose();
            ClearVolumePrefs();                       // 不留痕：别污染使用者的 PlayerPrefs
        }

        static void ClearVolumePrefs()
        {
            foreach (var bus in AllBuses)
                PlayerPrefs.DeleteKey(ZAudioManager.VolumeKey(bus));
        }

        // ── dB 换算（§7.2）───────────────────────────────────────
        [Test]
        public void ToDb_FullVolume_IsZero()
            => Assert.AreEqual(0f, ZAudioManager.ToDb(1f), 0.0001f);

        [Test]
        public void ToDb_Half_IsAboutMinusSix()
            => Assert.AreEqual(-6.0206f, ZAudioManager.ToDb(0.5f), 0.01f);

        [Test]
        public void ToDb_Zero_IsMinusEighty()
            => Assert.AreEqual(-80f, ZAudioManager.ToDb(0f), 0.0001f);

        [Test]
        public void ToDb_Negative_IsMinusEighty()
            => Assert.AreEqual(-80f, ZAudioManager.ToDb(-1f), 0.0001f);

        [Test]
        public void ToDb_TinyPositive_IsMinusEighty()
            => Assert.AreEqual(-80f, ZAudioManager.ToDb(0.00001f), 0.0001f);

        // ── 夹紧（§13-9：非法值被夹到 [0,1]）────────────────────
        [Test]
        public void SetVolume_AboveOne_ClampsToOne()
        {
            _manager.SetVolume(ZAudioBus.Sfx, 2f);
            Assert.AreEqual(1f, _manager.GetVolume(ZAudioBus.Sfx));
        }

        [Test]
        public void SetVolume_BelowZero_ClampsToZero()
        {
            _manager.SetVolume(ZAudioBus.Sfx, -1f);
            Assert.AreEqual(0f, _manager.GetVolume(ZAudioBus.Sfx));
        }

        [Test]
        public void SetVolume_NaN_ClampsToZero()
        {
            _manager.SetVolume(ZAudioBus.Sfx, float.NaN);
            Assert.AreEqual(0f, _manager.GetVolume(ZAudioBus.Sfx));
        }

        // ── 默认值与持久化（§7.3）───────────────────────────────
        [Test]
        public void GetVolume_WithNoPrefs_IsFull()
        {
            foreach (var bus in AllBuses)
                Assert.AreEqual(1f, _manager.GetVolume(bus), $"默认音量应为 1：{bus}");
        }

        [Test]
        public void SetVolume_WritesPlayerPrefs_UnderFrameworkKey()
        {
            _manager.SetVolume(ZAudioBus.Bgm, 0.25f);

            Assert.AreEqual(0.25f, PlayerPrefs.GetFloat("Zipper.Audio.Volume.Bgm", -1f), 0.0001f);
        }

        [Test]
        public void Volumes_AreIsolatedPerBus()
        {
            _manager.SetVolume(ZAudioBus.Sfx, 0f);

            Assert.AreEqual(0f, _manager.GetVolume(ZAudioBus.Sfx), "Sfx 应为 0");
            Assert.AreEqual(1f, _manager.GetVolume(ZAudioBus.Bgm), "Sfx 静音不应影响 Bgm（§13-8）");
            Assert.AreEqual(1f, _manager.GetVolume(ZAudioBus.Master), "Sfx 静音不应影响 Master");
        }

        [Test]
        public void NewManager_ReadsBackPersistedVolumes()
        {
            _manager.SetVolume(ZAudioBus.Ui, 0.4f);

            var fresh = new ZAudioManager(_pool, _resources, _logger, _options);
            try
            {
                Assert.AreEqual(0.4f, fresh.GetVolume(ZAudioBus.Ui), 0.0001f);
            }
            finally
            {
                fresh.Dispose();
            }
        }

        [Test]
        public void SetVolume_WithoutMixer_DoesNotThrow_AndDoesNotLogError()
        {
            // Mixer == null 时的降级路径：只更新内存表 + PlayerPrefs，不报错
            Assert.DoesNotThrow(() => _manager.SetVolume(ZAudioBus.Master, 0.5f));
            Assert.AreEqual(0, _logger.Errors.Count);
        }
    }
}
