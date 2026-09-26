using System.Runtime.CompilerServices;

// 让测试程序集能看到内部机制类型与成员：
//   internal 类型：ClipCache / PooledAudioSource / AudioBusChannel（命名空间 Zipper.Audio.Internal）
//   internal 成员：ZAudioManager.ToDb / ZAudioManager.VolumeKey / ZAudioManager.IsBgmPlaying / ZAudioManager.AttachRoot...
// 说明：不为"可测性"把这些类型改成 public —— 内部机制不该出现在对外契约面上。
[assembly: InternalsVisibleTo("Zipper.Tests")]
