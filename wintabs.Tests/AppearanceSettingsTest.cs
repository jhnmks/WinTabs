using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using wintabs.Models;

namespace wintabs.Tests
{
    [TestClass]
    public class AppearanceSettingsTest
    {

        [TestMethod]
        public void DefaultsToFollowingWindowsWhenNoAppearanceIsSaved()
        {
            var settings = JsonSerializer.Deserialize<Settings>("{}", AppState.CreateSettingsJsonOptions());
            Assert.AreEqual(AppearanceMode.System, settings!.Appearance);
        }

        [DataTestMethod]
        [DataRow(AppearanceMode.System, "system")]
        [DataRow(AppearanceMode.Light, "light")]
        [DataRow(AppearanceMode.Dark, "dark")]
        public void AppearanceRoundTrips(AppearanceMode appearance, string value)
        {
            var options = AppState.CreateSettingsJsonOptions();
            var json = JsonSerializer.Serialize(new Settings { Appearance = appearance }, options);
            using var document = JsonDocument.Parse(json);

            Assert.AreEqual(value, document.RootElement.GetProperty(nameof(Settings.Appearance)).GetString());
            Assert.AreEqual(appearance, JsonSerializer.Deserialize<Settings>(json, options)!.Appearance);
        }
    }
}
