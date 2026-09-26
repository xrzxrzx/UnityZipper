using System.Collections.Generic;
using Zipper.Core.Logging;

namespace Zipper.Tests.Fakes
{
    /// <summary>
    /// 假日志器：只记录、不落盘。用于断言"失败路径是否按约定记了日志"。
    /// 注意：签名必须与 <see cref="IZLogger"/> 完全一致（含 CallerMemberName 等可选参数）。
    /// </summary>
    internal sealed class FakeLogger : IZLogger
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Infos = new List<string>();

        public ZLogLevel Level { get; private set; } = ZLogLevel.Debug;

        public bool IsEnabled(ZLogLevel level) => level >= Level;

        public void SetGlobalLevel(ZLogLevel level) => Level = level;

        public void Debug(string message, string className = null, string member = null, string filePath = null, int line = 0, UnityEngine.Object context = null) { }

        public void Info(string message, string className = null, string member = null, string filePath = null, int line = 0, UnityEngine.Object context = null)
            => Infos.Add(message);

        public void Warning(string message, string className = null, string member = null, string filePath = null, int line = 0, UnityEngine.Object context = null)
            => Warnings.Add(message);

        public void Error(string message, System.Exception ex = null, string className = null, string member = null, string filePath = null, int line = 0, UnityEngine.Object context = null)
            => Errors.Add(message);

        public void Fatal(string message, System.Exception ex = null, string className = null, string member = null, string filePath = null, int line = 0, UnityEngine.Object context = null)
            => Errors.Add(message);

        public void Dispose() { }
    }
}
