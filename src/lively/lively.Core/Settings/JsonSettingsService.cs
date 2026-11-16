using lively.Common.Helpers.Storage;
using lively.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace lively.Core.Settings
{
    /// <summary>
    /// Settings stored as a JSON file. A missing file gives defaults with IsFirstRun set,
    /// a corrupt file is kept as "&lt;file&gt;.bak" and replaced with defaults.
    /// </summary>
    public sealed class JsonSettingsService : ISettingsService
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly string path;
        private readonly object sync = new();
        private SettingsModel settings;

        public JsonSettingsService(string path)
        {
            this.path = path;
            settings = Load();
        }

        public SettingsModel Settings
        {
            get { lock (sync) { return settings; } }
        }

        public event EventHandler<SettingsModel>? SettingsChanged;

        public void Save()
        {
            lock (sync)
            {
                Write(settings);
            }
        }

        public void Replace(SettingsModel newSettings)
        {
            ArgumentNullException.ThrowIfNull(newSettings);
            lock (sync)
            {
                settings = newSettings;
                Write(settings);
            }
            SettingsChanged?.Invoke(this, newSettings);
        }

        public string ToJson()
        {
            return JsonConvert.SerializeObject(Settings, Formatting.None);
        }

        public void ApplyJson(string json)
        {
            SettingsModel? parsed;
            try
            {
                if (JToken.Parse(json) is not JObject obj)
                {
                    throw new ArgumentException("Settings JSON must be an object.", nameof(json));
                }
                parsed = obj.ToObject<SettingsModel>();
            }
            catch (JsonException e)
            {
                throw new ArgumentException("Invalid settings JSON: " + e.Message, nameof(json), e);
            }
            if (parsed == null)
            {
                throw new ArgumentException("Invalid settings JSON.", nameof(json));
            }
            Replace(parsed);
        }

        private SettingsModel Load()
        {
            if (!File.Exists(path))
            {
                Logger.Info("No settings file, using defaults: " + path);
                var defaults = new SettingsModel { IsFirstRun = true };
                TryWrite(defaults);
                return defaults;
            }

            try
            {
                return JsonStorage<SettingsModel>.LoadData(path);
            }
            catch (Exception e)
            {
                Logger.Error("Settings file is corrupt, resetting to defaults: " + path + ": " + e.Message);
                try
                {
                    File.Copy(path, path + ".bak", overwrite: true);
                }
                catch (Exception copyError)
                {
                    Logger.Error("Failed to back up corrupt settings: " + copyError.Message);
                }
                var defaults = new SettingsModel();
                TryWrite(defaults);
                return defaults;
            }
        }

        private void TryWrite(SettingsModel data)
        {
            try
            {
                Write(data);
            }
            catch (Exception e)
            {
                Logger.Error("Failed to save settings: " + e.Message);
            }
        }

        private void Write(SettingsModel data)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            JsonStorage<SettingsModel>.StoreData(path, data);
        }
    }
}
