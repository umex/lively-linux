using lively.Models;

namespace lively.Core.Settings
{
    public interface ISettingsService
    {
        /// <summary>Current settings. Replaced as a whole by <see cref="Replace"/>.</summary>
        SettingsModel Settings { get; }

        /// <summary>Writes <see cref="Settings"/> to disk.</summary>
        void Save();

        /// <summary>Replaces, saves and raises <see cref="SettingsChanged"/>.</summary>
        void Replace(SettingsModel settings);

        /// <summary>Settings as compact JSON (the D-Bus wire format).</summary>
        string ToJson();

        /// <summary>Parses JSON and calls <see cref="Replace"/>.</summary>
        /// <exception cref="ArgumentException">The JSON is malformed or not a settings object.</exception>
        void ApplyJson(string json);

        event EventHandler<SettingsModel>? SettingsChanged;
    }
}
