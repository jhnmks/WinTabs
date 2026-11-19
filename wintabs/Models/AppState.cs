using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace wintabs.Models
{
    public class AppState
    {
        public IObservableFileEntity<Settings> Settings { get; }

        public AppState()
        {
            Settings = new ObservableJsonEntityWithCommentPreservation<Settings>(Path.GetFullPath("settings.json"),
                () => new Settings(),
                CreateSettingsJsonOptions());
        }

        internal static JsonSerializerOptions CreateSettingsJsonOptions() => new()
        {
            AllowTrailingCommas = true,
            WriteIndented = true,
            PropertyNamingPolicy = null,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
            }
        };
    }
}
