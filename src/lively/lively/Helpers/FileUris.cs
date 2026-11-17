using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace lively.Helpers
{
    /// <summary>
    /// Converts between command-line arguments, import targets and the URIs of org.freedesktop.Application.Open.
    /// An import target is an absolute local path, or an http(s) web address that is kept as given.
    /// </summary>
    public static class FileUris
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Import targets of the command-line arguments. Options (starting with "-") are skipped, relative paths are
        /// resolved against the current directory, file:// URIs become paths and http(s) addresses are kept.
        /// </summary>
        public static IReadOnlyList<string> CommandLineTargets(IEnumerable<string> args)
        {
            var targets = new List<string>();
            foreach (var arg in args)
            {
                if (string.IsNullOrWhiteSpace(arg) || arg.StartsWith('-'))
                {
                    continue;
                }
                if (arg.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                {
                    targets.AddRange(FromUris([arg]));
                    continue;
                }
                targets.Add(IsWebAddress(arg) ? arg : Path.GetFullPath(arg));
            }
            return targets;
        }

        /// <summary>URIs of import targets: file:// URIs of the paths, web addresses unchanged.</summary>
        public static IReadOnlyList<string> ToUris(IEnumerable<string> targets) =>
            targets.Select(t => IsWebAddress(t) ? t : new Uri(Path.GetFullPath(t)).AbsoluteUri).ToList();

        /// <summary>
        /// Import targets of the URIs: local paths of file:// URIs, http(s) addresses unchanged; other schemes and
        /// malformed URIs are logged and skipped.
        /// </summary>
        public static IReadOnlyList<string> FromUris(IEnumerable<string> uris)
        {
            var targets = new List<string>();
            foreach (var uri in uris)
            {
                if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile)
                {
                    targets.Add(parsed.LocalPath);
                }
                else if (IsWebAddress(uri))
                {
                    targets.Add(uri);
                }
                else
                {
                    Logger.Warn("Ignoring a URI that is neither file:// nor http(s)://.");
                }
            }
            return targets;
        }

        /// <summary>Whether the target starts with http:// or https:// (ignoring case); the core validates the rest.</summary>
        public static bool IsWebAddress(string target) =>
            target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }
}
