#nullable enable

using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using WinMan;

using wintabs.ViewModels;

namespace wintabs.Tests
{
    [TestClass]
    public class DropPreviewTest
    {
        [TestMethod]
        public void SettingThePreviewAnnouncesThatItBecameVisible()
        {
            var viewModel = new TilingOverlayViewModel();
            List<string?> changed = [];
            viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            viewModel.PreviewRectangle = new Rectangle(0, 0, 100, 100);

            Assert.IsTrue(viewModel.IsPreviewRectangleVisible);
            CollectionAssert.Contains(changed, nameof(TilingOverlayViewModel.IsPreviewRectangleVisible));
        }

        [TestMethod]
        public void TitleBarDragsMoveAndBorderDragsResize()
        {
            var start = new Rectangle(100, 100, 900, 700);
            Assert.IsTrue(TilingService.IsMoveGesture(start, new Rectangle(300, 150, 1100, 750)));
            // Moved onto a monitor with other scaling: the size changes too, but no edge stays put.
            Assert.IsTrue(TilingService.IsMoveGesture(start, new Rectangle(2100, 150, 3300, 1050)));
            Assert.IsFalse(TilingService.IsMoveGesture(start, new Rectangle(100, 100, 1000, 700)));
            Assert.IsFalse(TilingService.IsMoveGesture(start, new Rectangle(50, 40, 900, 700)));
            Assert.IsFalse(TilingService.IsMoveGesture(start, start));
        }
    }
}
