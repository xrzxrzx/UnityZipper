using System;
using System.Collections.Generic;
using NUnit.Framework;
using Zipper.Audio;

namespace Zipper.Tests
{
    /// <summary>
    /// <see cref="ZAudioOptions"/> 的映射表测试（纯逻辑，脱离音频硬件与 Mixer 资产）。
    /// 覆盖设计稿 audio-manager-design v0.2 §7.1（组名不硬编码）与 §13-8 的"选项映射"部分。
    /// </summary>
    public class AudioOptionsTests
    {
        static readonly ZAudioBus[] AllBuses =
        {
            ZAudioBus.Master, ZAudioBus.Bgm, ZAudioBus.Sfx, ZAudioBus.Voice, ZAudioBus.Ui
        };

        readonly ZAudioOptions _options = new ZAudioOptions();

        [TestCase(ZAudioBus.Master, "MasterVolume")]
        [TestCase(ZAudioBus.Bgm, "BgmVolume")]
        [TestCase(ZAudioBus.Sfx, "SfxVolume")]
        [TestCase(ZAudioBus.Voice, "VoiceVolume")]
        [TestCase(ZAudioBus.Ui, "UiVolume")]
        public void GetParam_ReturnsExpectedExposedParameter(ZAudioBus bus, string expected)
            => Assert.AreEqual(expected, _options.GetParam(bus));

        [TestCase(ZAudioBus.Master, "Master")]
        [TestCase(ZAudioBus.Bgm, "BGM")]
        [TestCase(ZAudioBus.Sfx, "SFX")]
        [TestCase(ZAudioBus.Voice, "Voice")]
        [TestCase(ZAudioBus.Ui, "UI")]
        public void GetGroupName_ReturnsExpectedMixerGroup(ZAudioBus bus, string expected)
            => Assert.AreEqual(expected, _options.GetGroupName(bus));

        [Test]
        public void EveryBus_IsMappedByBothTables()
        {
            foreach (var bus in AllBuses)
            {
                Assert.IsFalse(string.IsNullOrEmpty(_options.GetParam(bus)), $"参数名缺失：{bus}");
                Assert.IsFalse(string.IsNullOrEmpty(_options.GetGroupName(bus)), $"组名缺失：{bus}");
            }
        }

        [Test]
        public void ParamNames_And_GroupNames_AreTwoDistinctSets()
        {
            // 回归防线：曾出现"把 Exposed Parameter 名当成 Mixer 分组名传给 FindMatchingGroups"的错误
            // —— 两组名字必须是两套，且各自唯一。
            var paramNames = new HashSet<string>();
            var groupNames = new HashSet<string>();

            foreach (var bus in AllBuses)
            {
                Assert.IsTrue(paramNames.Add(_options.GetParam(bus)), $"参数名重复：{bus}");
                Assert.IsTrue(groupNames.Add(_options.GetGroupName(bus)), $"组名重复：{bus}");
            }

            foreach (var groupName in groupNames)
                Assert.IsFalse(paramNames.Contains(groupName), $"参数名与组名撞车：{groupName}");
        }

        [Test]
        public void InvalidBus_Throws_ForBothTables()
        {
            var invalid = (ZAudioBus)99;

            Assert.Throws<ArgumentOutOfRangeException>(() => _options.GetParam(invalid));
            Assert.Throws<ArgumentOutOfRangeException>(() => _options.GetGroupName(invalid));
        }

        [Test]
        public void Defaults_AreSane()
        {
            Assert.AreEqual(4, _options.InitialPoolSize);
            Assert.AreEqual(32, _options.MaxPoolSize);
            Assert.IsNull(_options.Mixer, "默认不带 Mixer：应由组装层或 Addressables 提供");
        }
    }
}
