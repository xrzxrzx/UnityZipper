using System;
using System.Threading;
using NUnit.Framework;
using Zipper.Audio.Internal;
using Zipper.Tests.Fakes;

namespace Zipper.Tests
{
    /// <summary>
    /// <see cref="ClipCache"/> 的失败路径测试——这是"PlaySfx 不抛异常"（设计稿 §9.1 两条保证）的落点。
    /// 成功路径（真的拿到 AudioClip）需要真实 Addressables，属 Editor 手工试听范畴。
    ///
    /// ⚠️ 写法约定：测试方法必须是【同步 void】。
    /// Unity Test Framework 的 runner 不等待 `async Task`（只认 void / IEnumerator），
    /// 写成 async 会被判为失败。这里的假实现全部同步完成（内部只 await UniTask.CompletedTask），
    /// 因此 `GetAwaiter().GetResult()` 立即返回、不会阻塞 —— 一旦换成真实 Addressables 就必须改回 IEnumerator 写法。
    /// </summary>
    public class ClipCacheTests
    {
        const string Address = "Assets/Audio/test_click.wav";

        FakeLogger _logger;
        FakeResourceManager _resources;
        ClipCache _cache;

        [SetUp]
        public void SetUp()
        {
            _logger = new FakeLogger();
            _resources = new FakeResourceManager();
            _cache = new ClipCache(_resources, _logger);
        }

        [Test]
        public void EmptyAddress_ReturnsNull_LogsError_AndSkipsResourceManager()
        {
            var handle = _cache.GetOrLoadAsync(string.Empty, CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsNull(handle);
            Assert.AreEqual(1, _logger.Errors.Count, "空地址要记 Error");
            Assert.AreEqual(0, _resources.LoadCallCount, "空地址不该走到资源管理器（那边会同步抛）");
        }

        [Test]
        public void NullAddress_ReturnsNull_LogsError_AndSkipsResourceManager()
        {
            var handle = _cache.GetOrLoadAsync(null, CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsNull(handle);
            Assert.AreEqual(1, _logger.Errors.Count);
            Assert.AreEqual(0, _resources.LoadCallCount);
        }

        [Test]
        public void LoadThrows_ReturnsNull_AndDoesNotRethrow()
        {
            _resources.ThrowOn = _ => new InvalidOperationException("boom");

            var handle = _cache.GetOrLoadAsync(Address, CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsNull(handle, "加载失败必须返回 null，绝不向外抛（UI 点击不该崩）");
            Assert.AreEqual(1, _logger.Errors.Count, "失败要记 Error");
        }

        [Test]
        public void LoadCancelled_ReturnsNull_WithoutErrorLog()
        {
            _resources.ThrowOn = _ => new OperationCanceledException();

            var handle = _cache.GetOrLoadAsync(Address, CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsNull(handle);
            Assert.AreEqual(0, _logger.Errors.Count, "取消是正常路径，不该记 Error");
        }

        [Test]
        public void LoadReturnsNullHandle_ReturnsNull_AndLogsError()
        {
            // 假资源管理器默认返回 null handle（模拟"加载完成但拿不到资源"）
            var handle = _cache.GetOrLoadAsync(Address, CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsNull(handle);
            Assert.AreEqual(1, _logger.Errors.Count);
            Assert.AreEqual(1, _resources.LoadCallCount);
        }

        [Test]
        public void FailedLoad_IsNotCached_SoNextCallRetries()
        {
            _resources.ThrowOn = _ => new InvalidOperationException("boom");
            _cache.GetOrLoadAsync(Address, CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsFalse(_cache.IsCached(Address), "失败不该入缓存，否则一次失败会被永久记住");

            _resources.ThrowOn = null;
            _cache.GetOrLoadAsync(Address, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(2, _resources.LoadCallCount, "第二次应重新尝试加载");
            Assert.IsFalse(_cache.IsCached(Address), "仍未拿到真实句柄，缓存应为空");
        }

        [Test]
        public void ReleaseAll_OnEmptyCache_DoesNotThrow()
            => Assert.DoesNotThrow(() => _cache.ReleaseAll());
    }
}
