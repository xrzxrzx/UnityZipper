using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Zipper.Core.Boot;
using Zipper.Core.Logging.Sink;

namespace Zipper.Core.Logging
{
    public class ZLoggerBootstrapper : IZModuleBootstrap, IDisposable
    {
        public ZBootPhase Phase => ZBootPhase.Logging;
        readonly ZLogger _logger;
        IZLogSink[] _sinks;
        ZMainThreadDispatcher _dispatcher;
        ZLogRouter _router;
        GameObject _driverObject;

        public ZLoggerBootstrapper(ZLogger logger)
        {
            _logger = logger;
        }

        public UniTask InitializeAsync(CancellationToken ct)
        {
            if (_router != null)
                return UniTask.CompletedTask;

            ct.ThrowIfCancellationRequested();

            _dispatcher = new ZMainThreadDispatcher();
            _sinks = CreateSinks().ToArray();
            _router = new ZLogRouter(_sinks, ZLogLevel.Info, _dispatcher);
            _driverObject = new GameObject("[Zipper] ZMainThreadDispatcher");

            _logger.Attach(_router);

            UnityEngine.Object.DontDestroyOnLoad(_driverObject);//跨场景保活
            var driver = _driverObject.AddComponent<ZMainThreadDispatcherDriver>();
            driver.Init(_dispatcher);

            return UniTask.CompletedTask;
        }

        private List<IZLogSink> CreateSinks()
        {
            var dir = System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, "ZipperLogs");
            var name = $"zipper-{System.DateTime.Now:yyyyMMdd}.log";

            return new List<IZLogSink>
            {
                new ConsoleSink(ZLogLevel.Debug),
                new FileSink(System.IO.Path.Combine(dir, name), ZLogLevel.Info, _dispatcher)
            };
        }

        public void Dispose()
        {
            if (_sinks != null)
            {
                foreach (var sink in _sinks)
                {
                    sink?.Dispose();
                }
            }

            _dispatcher?.Dispose();
            _dispatcher = null;

            if (_driverObject != null)
            {
                UnityEngine.Object.Destroy(_driverObject);
                _driverObject = null;
            }
        }
    }
}