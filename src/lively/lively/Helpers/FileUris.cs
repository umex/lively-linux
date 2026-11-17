using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace lively.Helpers
{
    /// <summary>
    /// Converts between command-line file arguments, absolute paths and file:// URIs
    /// (as used by org.freedesktop.Application.Open).
    /// </summary>
    public static class FileUris
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Absolute paths of the file arguments. Options (starting with "-") are skipped, relative paths are
        /// resolved against the current directory, and file:// URIs are accepted too.
        /// </summary>
        public static IReadOnlyList<string> CommandLinePaths(IEnumerable<string> args)
        {
            var paths = new List<string>();
            foreach (var arg in args)
            {
                if (string.IsNullOrWhiteSpace(arg) || arg.StartsWith('-'))
                {
                    continue;
                }
                if (arg.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                {
                    paths.AddRange(ToLocalPaths([arg]));
                    continue;
                }
                paths.Add(Path.GetFullPath(arg));
            }
            return paths;
        }

        /// <summary>file:// URIs of absolute paths.</summary>
        public static IReadOnlyList<string> ToUris(IEnumerable<string> paths) =>
            paths.Select(p => new Uri(Path.GetFullPath(p)).AbsoluteUri).ToList();

        /// <summary>Local paths of the file:// URIs; other schemes and malformed URIs are logged and skipped.</summary>
        public static IReadOnlyList<string> ToLocalPaths(IEnumerable<string> uris)
        {
            var paths = new List<string>();
            foreach (var uri in uris)
            {
                if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile)
                {
                    paths.Add(parsed.LocalPath);
                }
                else
                {
                    Logger.Warn($"Ignoring \"{uri}\": only file:// URIs can be opened.");
                }
            }
            return paths;
        }
    }
}
