using System.Text;

namespace lively.Core.Library
{
    /// <summary>A wallpaper could not be imported into the library; the message is meant for the user.</summary>
    public class ImportException(string message, Exception? inner = null) : Exception(message, inner);

    /// <summary>Creates and removes wallpaper folders for the importers.</summary>
    internal static class LibraryFolders
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private const int MaxSlugLength = 60;

        // Folder name choice and creation are one step, so that parallel imports of the same title differ,
        // also when they come from different importers.
        private static readonly Lock FolderLock = new();

        /// <summary>
        /// Folder name for a title: lower-case a-z and 0-9, runs of other characters become one "-",
        /// at most 60 characters, <paramref name="fallback"/> when nothing is left.
        /// </summary>
        public static string Slugify(string title, string fallback)
        {
            var sb = new StringBuilder();
            var dash = false;
            foreach (var c in title.ToLowerInvariant())
            {
                if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
                {
                    if (dash && sb.Length > 0)
                    {
                        sb.Append('-');
                    }
                    dash = false;
                    sb.Append(c);
                    if (sb.Length >= MaxSlugLength)
                    {
                        break;
                    }
                }
                else
                {
                    dash = true;
                }
            }
            if (sb.Length > MaxSlugLength)
            {
                sb.Length = MaxSlugLength;
            }
            var slug = sb.ToString().Trim('-');
            return slug.Length == 0 ? fallback : slug;
        }

        /// <summary>Path of <paramref name="slug"/> under <paramref name="parent"/>, with -2, -3, … appended while taken.</summary>
        public static string UniqueFolder(string parent, string slug)
        {
            var candidate = Path.Combine(parent, slug);
            for (var i = 2; Directory.Exists(candidate) || File.Exists(candidate); i++)
            {
                candidate = Path.Combine(parent, $"{slug}-{i}");
            }
            return candidate;
        }

        /// <summary>Creates <paramref name="parent"/> when missing and a new, empty <see cref="UniqueFolder"/> in it.</summary>
        /// <returns>The created folder.</returns>
        public static string CreateUnique(string parent, string slug)
        {
            Directory.CreateDirectory(parent);
            lock (FolderLock)
            {
                var folder = UniqueFolder(parent, slug);
                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        /// <summary>Removes an incomplete wallpaper folder; null is ignored, failures are logged.</summary>
        public static void Delete(string? folder)
        {
            if (folder == null)
            {
                return;
            }
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Logger.Error($"Could not remove the incomplete wallpaper folder {folder}: {e.Message}");
            }
        }
    }
}
