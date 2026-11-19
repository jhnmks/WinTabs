using System.Windows.Media;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using wintabs.Models;

namespace wintabs.Tests
{
    [TestClass]
    public class FocusBorderPaletteTest
    {
        private static double Luminance(Brush brush)
        {
            var c = ((SolidColorBrush)brush).Color;
            return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
        }

        [TestMethod]
        public void LightTintsStayPastelSoDarkTextRemainsReadable()
        {
            foreach (var swatch in FocusBorderPalette.Swatches)
                Assert.IsTrue(Luminance(swatch.CreateBrush(false)) > 0.6, swatch.Name);
        }

        [TestMethod]
        public void DarkTintsStayMutedSoLightTextRemainsReadable()
        {
            foreach (var swatch in FocusBorderPalette.Swatches)
                Assert.IsTrue(Luminance(swatch.CreateBrush(true)) < 0.45, swatch.Name);
        }

        [TestMethod]
        public void UnknownNameFallsBackToWindowsAccent()
        {
            Assert.AreSame(FocusBorderPalette.Swatches[0], FocusBorderPalette.Find("does-not-exist"));
        }
    }
}
