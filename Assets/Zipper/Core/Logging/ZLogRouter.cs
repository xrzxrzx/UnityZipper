using System;
using System.Threading;
using Zipper.Core.Logging.Sink;

namespace Zipper.Core.Logging
{
    internal sealed class ZLogRouter
    {
        readonly IZLogSink[] _sinks;
        ZLogLevel _globalMinLevel;
        readonly ZMainThreadDispatcher _mainThreadDispatcher;

        public ZLogRouter(IZLogSink[] sinks, ZLogLevel globalMinLevel, ZMainThreadDispatcher mainThreadDispatcher)
        {
            _sinks = sinks;
            _globalMinLevel = globalMinLevel;
            _mainThreadDispatcher = mainThreadDispatcher;
        }

        public void Dispatch(ZLogLevel level, Exception exception, string className, string member, int line, string message, UnityEngine.Object context)
        {
            if (!IsEnable(level))
                return;

            var entry = new ZLogEntry(level, className, member, line, message, DateTime.Now, Thread.CurrentThread.ManagedThreadId, context, exception);

            foreach (var sink in _sinks)
            {
                if (sink.MinimumLevel > level)//每个sink都有自己的最小等级，只有当日志等级大于等于sink的最小等级时才会被输出到该sink
                    continue;

                if (sink.RequiresMainThread && !_mainThreadDispatcher.IsMainThread)
                {
                    _mainThreadDispatcher.Enqueue(() => sink.Write(entry));
                }
                else
                {
                    try
                    {
                        sink.Write(entry);
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.LogError($"ZLogRouter: 日志写入错误 {sink.GetType().Name}: {e}");
                    }
                }
            }
        }

        public void SetGlobalLevel(ZLogLevel level) => _globalMinLevel = level;

        public bool IsEnable(ZLogLevel level) => level >= _globalMinLevel;
    }
}