using System;

namespace Zipper.Core.Logging.Sink
{
    internal static class LogFormatter
    {
        public static string Format(in ZLogEntry entry)
        {
            string identity = entry.ClassName ?? ShortenPath(entry.FilePath) ?? "?";

            // 行号紧跟来源（文件路径或类名）之后，形如 [Assets/…/Foo.cs:99 | Dispose]
            if (entry.Line > 0)
                identity += ":" + entry.Line;

            string member = entry.Member ?? "?";
            string exception = entry.Exception != null ? entry.Exception.Message : "";

            return $"[{entry.Time:HH:mm:ss}] [{identity} | {member}] {exception} {entry.Message}";
        }

        // 截到项目相对路径（Assets/…）；不在 Assets 下的退化为文件名
        static string ShortenPath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return null;

            string path = filePath.Replace('\\', '/');

            int index = path.IndexOf("Assets/", StringComparison.Ordinal);
            if (index >= 0)
                return path.Substring(index);

            int slash = path.LastIndexOf('/');
            return slash >= 0 ? path.Substring(slash + 1) : path;
        }
    }
}
