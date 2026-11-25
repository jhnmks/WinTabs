using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace wintabs.Utilities
{
    interface ILogFileViewer
    {
        ICollection<string> TextContent { get; }

        void Open();
    }

    class LogFileViewer(string fileName, ICollection<string> textContent) : ILogFileViewer
    {
        private readonly string m_fileName = fileName ?? throw new ArgumentNullException(nameof(fileName));

        public ICollection<string> TextContent { get; } = textContent ?? throw new ArgumentNullException(nameof(textContent));

        public void Open()
        {
            var latest = FindLatest(m_fileName) ?? m_fileName;
            Process.Start("explorer.exe", $"/select,\"{latest}\"");
        }

        internal static string? FindLatest(string basePath)
        {
            var name = Path.GetFileNameWithoutExtension(basePath);
            var extension = Path.GetExtension(basePath);
            return Directory.EnumerateFiles(Path.GetDirectoryName(basePath)!, name + "*" + extension)
                .Where(path =>
                {
                    var stem = Path.GetFileNameWithoutExtension(path);
                    return stem == name || (stem.StartsWith(name + "_", StringComparison.Ordinal) &&
                        stem.AsSpan(name.Length + 1).Length > 0 && int.TryParse(stem.AsSpan(name.Length + 1), out _));
                })
                .MaxBy(File.GetLastWriteTimeUtc);
        }
    }
}
