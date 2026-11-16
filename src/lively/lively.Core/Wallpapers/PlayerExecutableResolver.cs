namespace lively.Core.Wallpapers
{
    /// <summary>
    /// Finds a .NET executable that is deployed next to the running assembly
    /// (referenced Exe projects are copied into the referrer's output).
    /// </summary>
    public static class PlayerExecutableResolver
    {
        /// <summary>
        /// The apphost <c>&lt;BaseDirectory&gt;/&lt;assemblyName&gt;</c> if it exists and is executable,
        /// else <c>dotnet &lt;assemblyName&gt;.dll</c>.
        /// </summary>
        /// <exception cref="FileNotFoundException">Neither the apphost nor the dll exists.</exception>
        public static PlayerLaunchInfo Resolve(string assemblyName, IEnumerable<string> arguments)
        {
            return Resolve(AppContext.BaseDirectory, assemblyName, arguments);
        }

        public static PlayerLaunchInfo Resolve(string directory, string assemblyName, IEnumerable<string> arguments)
        {
            var args = arguments.ToList();
            var apphost = Path.Combine(directory, OperatingSystem.IsWindows() ? assemblyName + ".exe" : assemblyName);
            if (File.Exists(apphost) && IsExecutable(apphost))
            {
                return new PlayerLaunchInfo(apphost, args);
            }

            var dll = Path.Combine(directory, assemblyName + ".dll");
            if (File.Exists(dll))
            {
                return new PlayerLaunchInfo(DotnetHost(), [dll, .. args]);
            }

            throw new FileNotFoundException($"Executable \"{assemblyName}\" was not found in {directory}.", apphost);
        }

        private static bool IsExecutable(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                return true;
            }
            try
            {
                return (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static string DotnetHost()
        {
            // Set by the dotnet CLI (dotnet run/test); otherwise prefer the host that runs us.
            var hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (!string.IsNullOrEmpty(hostPath) && File.Exists(hostPath))
            {
                return hostPath;
            }
            var processPath = Environment.ProcessPath;
            if (processPath != null && Path.GetFileNameWithoutExtension(processPath) == "dotnet")
            {
                return processPath;
            }
            return "dotnet";
        }
    }
}
