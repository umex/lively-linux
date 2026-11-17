using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using lively.Common;
using lively.Common.Helpers.Storage;
using lively.Models;

namespace lively.Core.Library
{
    /// <summary>A web page or address could not be added to the library; the message is meant for the user.</summary>
    public sealed class WebImportException(string message, Exception? inner = null) : ImportException(message, inner);

    /// <summary>
    /// Adds web wallpapers to the library: a copy of a local HTML file or page folder (type web), or a
    /// website address (type url). No network access.
    /// </summary>
    public sealed class WebImporter
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        /// <summary>Bytes of the entry page searched for its &lt;title&gt;.</summary>
        internal const int TitleReadLimit = 64 * 1024;
        /// <summary>Longest title kept, the length <see cref="LibraryModel"/> shows.</summary>
        internal const int MaxTitleLength = 100;

        private static readonly Regex TitlePattern = new(@"<title\b[^>]*>(.*?)</title\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        // Optional "scheme://", then everything up to the last "@" before the path, query or fragment.
        private static readonly Regex UserInfoPattern = new(@"^(?<prefix>[^/?#]*//)?[^/?#]*@", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        private readonly string wallpapersDirectory;

        /// <param name="wallpapersDirectory">Parent folder of the library's wallpapers; created when missing.</param>
        public WebImporter(string wallpapersDirectory)
        {
            ArgumentException.ThrowIfNullOrEmpty(wallpapersDirectory);
            this.wallpapersDirectory = wallpapersDirectory;
        }

        public string WallpapersDirectory => wallpapersDirectory;

        /// <summary>Called with the source path after each copied file. Lets tests cancel or fail an import midway.</summary>
        internal Action<string>? FileCopied { get; set; }

        /// <summary>
        /// Copies a web page into a new wallpaper folder. An <c>.html</c>/<c>.htm</c> file is copied alone. A folder
        /// is copied with its subfolders; its page is <c>index.html</c>, else <c>index.htm</c>, else its only HTML
        /// file. A folder that already is a Lively web wallpaper keeps its LivelyInfo.json fields.
        /// </summary>
        /// <remarks>
        /// Symbolic links are not followed out of the folder: links to directories and links whose target is
        /// outside the folder are skipped (and logged); links to files inside it are copied as files.
        /// </remarks>
        /// <returns>The created wallpaper folder (it contains LivelyInfo.json).</returns>
        /// <exception cref="WebImportException">The path is not an importable page or folder, or copying failed.</exception>
        /// <exception cref="OperationCanceledException">Cancelled; nothing is left behind.</exception>
        public Task<string> ImportAsync(string path, CancellationToken ct = default) =>
            // File copies are synchronous; keep them off the caller's thread.
            Task.Run(() => Import(path, ct), CancellationToken.None);

        /// <summary>Adds a website address as a new wallpaper folder (type url).</summary>
        /// <param name="url">An absolute http or https address with a host.</param>
        /// <returns>The created wallpaper folder (it contains LivelyInfo.json).</returns>
        /// <exception cref="WebImportException">The address is not an absolute http(s) address, or writing failed.</exception>
        public string AddUrl(string url)
        {
            var uri = ParseUrl(url);
            var address = uri.AbsoluteUri;
            var path = Uri.UnescapeDataString(uri.AbsolutePath);
            var title = path == "/" ? uri.Authority : uri.Authority + path;
            if (title.Length > MaxTitleLength)
            {
                title = title[..MaxTitleLength];
            }

            string? folder = null;
            try
            {
                folder = LibraryFolders.CreateUnique(wallpapersDirectory, LibraryFolders.Slugify(title, "site"));
                StoreInfo(folder, new LivelyInfoModel
                {
                    Title = title,
                    Desc = address,
                    Type = WallpaperType.url,
                    FileName = address,
                    Thumbnail = null,
                    Preview = null,
                    IsAbsolutePath = false,
                });
                Logger.Info($"Added {address} as {folder}.");
                return folder;
            }
            catch (Exception e)
            {
                Logger.Error($"Adding {address} failed: {e}");
                LibraryFolders.Delete(folder);
                throw new WebImportException($"\"{address}\" could not be added: {e.Message}", e);
            }
        }

        /// <summary>The address as an absolute http(s) <see cref="Uri"/> without user name or password.</summary>
        /// <exception cref="WebImportException">Anything else. The message never contains a user name or password.</exception>
        internal static Uri ParseUrl(string url)
        {
            var text = url?.Trim() ?? string.Empty;
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || string.IsNullOrEmpty(uri.Host))
            {
                throw new WebImportException($"\"{WithoutUserInfo(text)}\" is not a web address; it must start with http:// or https://.");
            }
            if (uri.UserInfo.Length > 0)
            {
                // Credentials would end up in LivelyInfo.json and in logs.
                var safe = new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty }.Uri.AbsoluteUri;
                throw new WebImportException($"\"{safe}\" has a user name or password; addresses with credentials are not supported.");
            }
            return uri;
        }

        /// <summary><paramref name="text"/> with anything before an "@" in its authority replaced by "…".</summary>
        private static string WithoutUserInfo(string text) => UserInfoPattern.Replace(text, "${prefix}…@");

        private string Import(string path, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
            {
                throw new WebImportException($"\"{path}\" is not an absolute path.");
            }
            path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var isFolder = Directory.Exists(path);
            if (!isFolder && !File.Exists(path))
            {
                throw new WebImportException($"\"{path}\" does not exist.");
            }
            var name = Path.GetFileName(path);
            if (!isFolder && !IsHtml(path))
            {
                throw new WebImportException($"\"{name}\" is not an HTML file (.html or .htm).");
            }

            string? folder = null;
            try
            {
                var info = isFolder ? FolderInfo(path) : NewInfo(path, Path.GetFileNameWithoutExtension(path), name);
                Logger.Info($"Importing web page {path} (entry {info.FileName}).");
                ct.ThrowIfCancellationRequested();
                folder = LibraryFolders.CreateUnique(wallpapersDirectory, LibraryFolders.Slugify(info.Title, "page"));
                if (isFolder)
                {
                    CopyFolder(path, folder, path, ct);
                }
                else
                {
                    File.Copy(path, Path.Combine(folder, name));
                    FileCopied?.Invoke(path);
                }
                ct.ThrowIfCancellationRequested();

                // Written last: a folder without LivelyInfo.json is not a wallpaper.
                StoreInfo(folder, info);
                Logger.Info($"Imported {path} as {folder}.");
                return folder;
            }
            catch (OperationCanceledException)
            {
                Logger.Info($"Import of {path} cancelled.");
                LibraryFolders.Delete(folder);
                throw;
            }
            catch (WebImportException e)
            {
                Logger.Error($"Import of {path} failed: {e.Message}");
                LibraryFolders.Delete(folder);
                throw;
            }
            catch (Exception e)
            {
                Logger.Error($"Import of {path} failed: {e}");
                LibraryFolders.Delete(folder);
                throw new WebImportException($"\"{name}\" could not be imported: {e.Message}", e);
            }
        }

        /// <summary>The LivelyInfo.json for a page folder: its own one when it has one, else a new one for its entry page.</summary>
        private LivelyInfoModel FolderInfo(string folder)
        {
            var name = Path.GetFileName(folder);
            var library = Path.TrimEndingDirectorySeparator(Path.GetFullPath(wallpapersDirectory));
            if (IsSameOrInside(library, folder))
            {
                throw new WebImportException($"\"{name}\" contains the wallpaper library and cannot be imported.");
            }

            var infoPath = Path.Combine(folder, LibraryScanner.LivelyInfoFileName);
            if (File.Exists(infoPath))
            {
                return ExistingInfo(folder, infoPath);
            }

            var pages = new DirectoryInfo(folder).EnumerateFiles()
                .Where(f => IsHtml(f.Name) && IsCopyableFile(f, folder))
                .Select(f => f.Name)
                .ToList();
            var entry = SingleOrNone(pages, p => p.Equals("index.html", StringComparison.OrdinalIgnoreCase), name)
                ?? SingleOrNone(pages, p => p.Equals("index.htm", StringComparison.OrdinalIgnoreCase), name);
            if (entry == null)
            {
                entry = pages.Count switch
                {
                    1 => pages[0],
                    0 => throw new WebImportException($"\"{name}\" has no HTML file (.html or .htm) at its top level."),
                    _ => throw new WebImportException(
                        $"\"{name}\" has several HTML files and no index.html ({string.Join(", ", pages.Order(StringComparer.Ordinal))}); import the page file instead."),
                };
            }
            return NewInfo(Path.Combine(folder, entry), name, entry);
        }

        /// <summary>The LivelyInfo.json of a folder that already is a Lively wallpaper, checked and kept.</summary>
        private static LivelyInfoModel ExistingInfo(string folder, string infoPath)
        {
            var name = Path.GetFileName(folder);
            LivelyInfoModel info;
            try
            {
                info = JsonStorage<LivelyInfoModel>.LoadData(infoPath);
            }
            catch (Exception e)
            {
                throw new WebImportException($"The {LibraryScanner.LivelyInfoFileName} of \"{name}\" cannot be read: {e.Message}", e);
            }
            if (info == null)
            {
                throw new WebImportException($"The {LibraryScanner.LivelyInfoFileName} of \"{name}\" is empty.");
            }
            if (info.Type is not (WallpaperType.web or WallpaperType.webaudio))
            {
                throw new WebImportException($"\"{name}\" is a Lively wallpaper of type {info.Type}; only web and webaudio wallpapers can be imported as a page folder.");
            }
            var entry = info.IsAbsolutePath || string.IsNullOrWhiteSpace(info.FileName) ? null : Path.GetFullPath(Path.Combine(folder, info.FileName));
            if (entry == null || !IsCopyableFile(new FileInfo(entry), folder))
            {
                throw new WebImportException($"The page \"{info.FileName}\" named in the {LibraryScanner.LivelyInfoFileName} of \"{name}\" is not in the folder.");
            }

            if (string.IsNullOrWhiteSpace(info.Title))
            {
                info.Title = ReadTitle(entry) ?? name;
            }
            // Missing pictures would show as broken tiles; without one a thumbnail is made later.
            info.Thumbnail = ExistingFile(folder, info.Thumbnail);
            info.Preview = ExistingFile(folder, info.Preview);
            return info;
        }

        private static LivelyInfoModel NewInfo(string page, string fallbackTitle, string fileName) => new()
        {
            Title = ReadTitle(page) ?? fallbackTitle,
            Desc = null,
            Type = WallpaperType.web,
            FileName = fileName,
            Thumbnail = null,
            Preview = null,
            IsAbsolutePath = false,
        };

        /// <summary>
        /// The text of the page's first &lt;title&gt; within its first <see cref="TitleReadLimit"/> bytes,
        /// entity-decoded, whitespace collapsed, at most <see cref="MaxTitleLength"/> characters; null when none.
        /// </summary>
        internal static string? ReadTitle(string page)
        {
            string head;
            try
            {
                using var stream = File.OpenRead(page);
                var buffer = new byte[TitleReadLimit];
                var length = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
                using var reader = new StreamReader(new MemoryStream(buffer, 0, length), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                head = reader.ReadToEnd();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Logger.Warn($"Could not read the title of {page}: {e.Message}");
                return null;
            }

            Match match;
            try
            {
                match = TitlePattern.Match(head);
            }
            catch (RegexMatchTimeoutException)
            {
                return null;
            }
            if (!match.Success)
            {
                return null;
            }
            var title = Whitespace.Replace(WebUtility.HtmlDecode(match.Groups[1].Value), " ").Trim();
            if (title.Length > MaxTitleLength)
            {
                title = title[..MaxTitleLength].TrimEnd();
            }
            return title.Length == 0 ? null : title;
        }

        /// <summary>Copies <paramref name="source"/> into <paramref name="target"/>, except the top-level LivelyInfo.json.</summary>
        private void CopyFolder(string source, string target, string root, CancellationToken ct)
        {
            Directory.CreateDirectory(target);
            foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
            {
                ct.ThrowIfCancellationRequested();
                var destination = Path.Combine(target, entry.Name);
                if (source == root && entry.Name == LibraryScanner.LivelyInfoFileName)
                {
                    // Written last by Import.
                    continue;
                }
                if (entry.LinkTarget != null)
                {
                    if (entry is FileInfo file && IsCopyableFile(file, root))
                    {
                        File.Copy(entry.FullName, destination);
                        FileCopied?.Invoke(entry.FullName);
                    }
                    else
                    {
                        Logger.Warn($"Skipping {entry.FullName}: a link to {entry.LinkTarget} (directory, outside the folder or missing).");
                    }
                }
                else if (entry is DirectoryInfo directory)
                {
                    CopyFolder(directory.FullName, destination, root, ct);
                }
                else
                {
                    File.Copy(entry.FullName, destination);
                    FileCopied?.Invoke(entry.FullName);
                }
            }
        }

        /// <summary>
        /// True for a file below <paramref name="root"/> that the copy reaches: not below a linked directory, and
        /// either a regular file or a link that ends at such a file.
        /// </summary>
        private static bool IsCopyableFile(FileInfo file, string root)
        {
            if (!file.Exists || !IsReachable(file.FullName, root))
            {
                return false;
            }
            if (file.LinkTarget == null)
            {
                return true;
            }
            FileSystemInfo? target;
            try
            {
                target = file.ResolveLinkTarget(returnFinalTarget: true);
            }
            catch (IOException)
            {
                return false;
            }
            return target is FileInfo { Exists: true } && target.LinkTarget == null && IsReachable(target.FullName, root);
        }

        /// <summary>Whether full path <paramref name="path"/> is below <paramref name="root"/> without a linked directory on the way.</summary>
        private static bool IsReachable(string path, string root)
        {
            path = Path.TrimEndingDirectorySeparator(path);
            if (path == root || !IsSameOrInside(path, root))
            {
                return false;
            }
            for (var dir = Path.GetDirectoryName(path); dir != null && dir != root; dir = Path.GetDirectoryName(dir))
            {
                if (new DirectoryInfo(dir).LinkTarget != null)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary><paramref name="name"/> when it is a file inside <paramref name="folder"/>, else null.</summary>
        private static string? ExistingFile(string folder, string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }
            return IsCopyableFile(new FileInfo(Path.GetFullPath(Path.Combine(folder, name))), folder) ? name : null;
        }

        private static string? SingleOrNone(List<string> pages, Func<string, bool> predicate, string folderName)
        {
            var matches = pages.Where(predicate).ToList();
            return matches.Count switch
            {
                0 => null,
                1 => matches[0],
                _ => throw new WebImportException($"\"{folderName}\" has several index pages ({string.Join(", ", matches.Order(StringComparer.Ordinal))})."),
            };
        }

        private static void StoreInfo(string folder, LivelyInfoModel info) =>
            JsonStorage<LivelyInfoModel>.StoreData(Path.Combine(folder, LibraryScanner.LivelyInfoFileName), info);

        private static bool IsHtml(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".html", StringComparison.OrdinalIgnoreCase) || extension.Equals(".htm", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Whether full path <paramref name="path"/> is <paramref name="folder"/> or below it.</summary>
        private static bool IsSameOrInside(string path, string folder)
        {
            path = Path.TrimEndingDirectorySeparator(path);
            folder = Path.TrimEndingDirectorySeparator(folder);
            var prefix = Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;
            return path == folder || path.StartsWith(prefix, StringComparison.Ordinal);
        }
    }
}
