using System;
using System.Collections.Generic;

using wintabs.Layouts.Tiling;
using wintabs.Tests.TestUtilities;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using WinMan;

namespace wintabs.Tests
{
    [TestClass]
    public class TilingWorkspaceTest
    {
        private readonly VirtualDesktopMockFactory m_desktopFactory = new();
        private readonly WindowMockFactory m_windowFactory = new();
        private readonly Rectangle m_workarea = new(0, 0, 1920, 1080);

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(2)]
        [DataRow(3)]
        [DataRow(8)]
        public void WindowGapSeparatesTilesAndWorkAreaEdges(int gap)
        {
            var area = new Rectangle(-1200, -800, 0, 0);
            var workspace = new TilingWorkspace(tabsOnly: true);
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, TilingService.InsetWorkArea(area, gap), PanelOrientation.Horizontal);
            var tree = workspace.GetTree(desktop)!;
            var first = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var second = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var row = (StackPanelNode)first.Parent!;
            row.Padding = new Rectangle(0, 18 + gap, 0, 0);
            row.Spacing = gap;
            tree.Arrange();
            Assert.IsTrue(workspace.SplitTab(second, TilingDirection.Right));
            tree.Measure();
            tree.Arrange();

            Assert.AreEqual(gap, first.ComputedRectangle.Left - area.Left);
            Assert.AreEqual(gap, second.ComputedRectangle.Left - first.ComputedRectangle.Right);
            Assert.AreEqual(gap, area.Right - second.ComputedRectangle.Right);
            Assert.AreEqual(gap, area.Bottom - first.ComputedRectangle.Bottom);
            int stripTop = TilingWorkspace.GetTabBarBounds(row).Top + gap / 2;
            Assert.AreEqual(gap, stripTop - area.Top);
            Assert.AreEqual(gap, first.ComputedRectangle.Top - (stripTop + 18));
        }

        [TestMethod]
        public void TestAddRemoveDesktop()
        {
            var workspace = new TilingWorkspace();
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            workspace.UnregisterDesktop(desktop);
        }

        [TestMethod]
        public void TestAddDesktopTwice()
        {
            var workspace = new TilingWorkspace();
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            Assert.ThrowsException<ArgumentException>(() =>
            {
                workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            });
        }

        [TestMethod]
        public void TestRemoveMissingDesktop()
        {
            var workspace = new TilingWorkspace();
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            Assert.ThrowsException<ArgumentException>(() =>
            {
                workspace.UnregisterDesktop(desktop);
            });
        }

        [TestMethod]
        public void TestAddWindowNoParent()
        {
            var workspace = new TilingWorkspace();
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
        }

        [TestMethod]
        public void TabsShareTheWorkAreaBelowTheHeader()
        {
            var workspace = new TilingWorkspace(tabsOnly: true) { AutoCollapse = true };
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            var tree = workspace.GetTree(desktop)!;
            var root = tree.Root!;
            root.Padding = new Rectangle(0, 24, 0, 0);
            var first = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow(), maxTreeWidth: 1);
            var second = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow(), maxTreeWidth: 1);

            tree.Arrange();

            Assert.IsInstanceOfType(root, typeof(StackPanelNode));
            Assert.AreSame(root, first.Parent);
            Assert.AreSame(root, second.Parent);
            Assert.AreEqual(new Rectangle(0, 24, 1920, 1080), first.ComputedRectangle);
            Assert.AreEqual(first.ComputedRectangle, second.ComputedRectangle);

            tree.WorkArea = new Rectangle(1920, 40, 3200, 1064);
            tree.Arrange();

            Assert.AreEqual(new Rectangle(1920, 64, 3200, 1064), first.ComputedRectangle);
            Assert.AreEqual(first.ComputedRectangle, second.ComputedRectangle);
            Assert.AreSame(first, root.Children[0]);
            Assert.AreSame(second, root.Children[1]);

            workspace.UnregisterWindow(first.WindowReference);
            Assert.AreSame(root, tree.Root);
            Assert.AreSame(second, root.Children[0]);
        }

        [TestMethod]
        public void TabGroupsSplitResizeSwapAndCollapse()
        {
            var workspace = new TilingWorkspace(tabsOnly: true);
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            var tree = workspace.GetTree(desktop)!;
            var first = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var second = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var original = (StackPanelNode)first.Parent!;
            original.Padding = new Rectangle(0, 24, 0, 0);
            tree.Arrange();

            Assert.IsTrue(workspace.SplitTab(second, TilingDirection.Right));
            var split = (SplitPanelNode)tree.Root!;
            var newGroup = (StackPanelNode)second.Parent!;
            var third = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            Assert.AreSame(newGroup, third.Parent);
            tree.Measure();
            tree.Arrange();
            var width = first.ComputedRectangle.Width;
            Assert.AreEqual(24, first.ComputedRectangle.Top);
            Assert.AreEqual(first.ComputedRectangle.Right, second.ComputedRectangle.Left);
            Assert.IsTrue(split.ResizeDivider(0, 120));
            tree.Arrange();
            Assert.AreEqual(width + 120, first.ComputedRectangle.Width);
            Assert.AreEqual(second.ComputedRectangle, third.ComputedRectangle);

            tree.WorkArea = new Rectangle(1920, 0, 3840, 1080);
            tree.Arrange();
            Assert.AreEqual(width + 120, first.ComputedRectangle.Width);
            Assert.AreEqual(1920, first.ComputedRectangle.Left);
            tree.WorkArea = m_workarea;
            tree.Arrange();

            Assert.IsTrue(workspace.MoveTabGroup(newGroup, original, edge: null));
            tree.Measure();
            tree.Arrange();
            Assert.IsTrue(second.ComputedRectangle.Left < first.ComputedRectangle.Left);
            Assert.AreSame(second, tree.FindNode(second.WindowReference));

            Assert.IsTrue(workspace.MoveTabGroup(newGroup, original, TilingDirection.Down));
            tree.Measure();
            tree.Arrange();
            Assert.AreEqual(PanelOrientation.Vertical, ((SplitPanelNode)tree.Root!).Orientation);
            Assert.IsTrue(second.ComputedRectangle.Top > first.ComputedRectangle.Top);

            workspace.UnregisterWindow(first.WindowReference);
            Assert.AreSame(newGroup, tree.Root);
            tree.Arrange();
            Assert.AreEqual(new Rectangle(0, 24, 1920, 1080), second.ComputedRectangle);
        }

        [TestMethod]
        public void DraggingATabToAnotherGroupPreservesTheWindowAndCollapsesAnEmptyGroup()
        {
            var workspace = new TilingWorkspace(tabsOnly: true);
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            var tree = workspace.GetTree(desktop)!;
            var first = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var tab = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var target = (StackPanelNode)first.Parent!;
            tree.Arrange();
            Assert.IsTrue(workspace.SplitTab(tab, TilingDirection.Right));
            var originalPosition = workspace.GetOriginalPosition(tab.WindowReference);

            tree.Measure();
            tree.Arrange();
            var bounds = TilingWorkspace.GetGroupBounds(target);
            Assert.IsTrue(workspace.DropTab(tab, bounds.Center));
            Assert.AreSame(target, tree.Root);
            CollectionAssert.AreEqual(new TilingNode[] { first, tab }, new List<TilingNode>(target.Children));
            Assert.AreSame(tab, workspace.GetFocus(desktop));
            Assert.AreSame(tab, tree.FindNode(tab.WindowReference));
            Assert.AreEqual(originalPosition, workspace.GetOriginalPosition(tab.WindowReference));
            Assert.IsFalse(workspace.MoveTabToGroup(tab, target));
        }

        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void DroppingAtTheSideOfTheSameGroupCreatesANewGroup(bool left)
        {
            var workspace = new TilingWorkspace(tabsOnly: true);
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, new Rectangle(0, 0, 1200, 800), PanelOrientation.Horizontal);
            var tree = workspace.GetTree(desktop)!;
            var other = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var moved = workspace.RegisterWindow(m_windowFactory.CreateDiscordWindow());
            tree.Arrange();

            Assert.IsFalse(workspace.DropTab(moved, new Point(600, 400)));
            Assert.IsTrue(workspace.DropTab(moved, new Point(left ? 1 : 1198, 400)));
            Assert.AreNotSame(other.Parent, moved.Parent);
            var split = (SplitPanelNode)tree.Root!;
            Assert.AreEqual(PanelOrientation.Horizontal, split.Orientation);
            Assert.AreSame(moved.Parent, split.Children[left ? 0 : 1]);
            tree.Measure();
            tree.Arrange();
        }

        [DataTestMethod]
        [DataRow(30, 400, TilingDirection.Left)]
        [DataRow(1170, 400, TilingDirection.Right)]
        public void ATabTilesOnWhicheverSideItIsDroppedAndThePreviewSaysSo(int x, int y, TilingDirection side)
        {
            var workspace = new TilingWorkspace(tabsOnly: true);
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, new Rectangle(0, 0, 1200, 800), PanelOrientation.Horizontal);
            var tree = workspace.GetTree(desktop)!;
            workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var moved = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            tree.Arrange();

            var preview = workspace.GetTabDropTarget(moved, new Point(x, y));
            Assert.AreEqual(side, preview?.Edge);
            Assert.IsTrue(workspace.DropTab(moved, new Point(x, y)));
            var split = (SplitPanelNode)tree.Root!;
            Assert.AreSame(moved.Parent, split.Children[side == TilingDirection.Left ? 0 : 1]);
        }

        [TestMethod]
        public void ThePreviewNeverPromisesADropThatIsRefused()
        {
            var workspace = new TilingWorkspace(tabsOnly: true);
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            // Too narrow for two Discord windows (940 px minimum) side by side; tall enough to stack them.
            workspace.RegisterDesktop(desktop, new Rectangle(0, 0, 1200, 1100), PanelOrientation.Horizontal);
            var tree = workspace.GetTree(desktop)!;
            workspace.RegisterWindow(m_windowFactory.CreateDiscordWindow());
            var moved = workspace.RegisterWindow(m_windowFactory.CreateDiscordWindow());
            tree.Arrange();

            Assert.IsNull(workspace.GetTabDropTarget(moved, new Point(30, 550)));
            Assert.IsFalse(workspace.DropTab(moved, new Point(30, 550)));
            Assert.IsNull(workspace.GetTabDropTarget(moved, new Point(600, 30)));
            Assert.IsTrue(workspace.SplitTab(moved, TilingDirection.Up));
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow(TilingDirection.Left)]
        public void ATabMovesToAGroupOnAnotherDisplay(TilingDirection? edge)
        {
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            var left = new TilingWorkspace(tabsOnly: true);
            var right = new TilingWorkspace(tabsOnly: true);
            left.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            right.RegisterDesktop(desktop, new Rectangle(1920, 0, 3840, 1080), PanelOrientation.Horizontal);
            var moving = left.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var resident = right.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var target = (StackPanelNode)resident.Parent!;
            right.GetTree(desktop)!.Arrange();
            var position = left.GetOriginalPosition(moving.WindowReference);

            var moved = left.TransferTab(moving, right, target, edge, desktop);

            Assert.IsNotNull(moved);
            Assert.IsFalse(left.HasWindow(moving.WindowReference));
            Assert.AreEqual(0, left.GetTree(desktop)!.Root!.Children.Count);
            Assert.AreEqual(edge == null, moved!.Parent == target);
            Assert.AreSame(moved, right.GetFocus(desktop));
            Assert.AreEqual(position, right.GetOriginalPosition(moved.WindowReference));
        }

        [TestMethod]
        public void ATabMovesOntoAnEmptyDisplay()
        {
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            var left = new TilingWorkspace(tabsOnly: true);
            var right = new TilingWorkspace(tabsOnly: true);
            left.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            right.RegisterDesktop(desktop, new Rectangle(1920, 0, 3840, 1080), PanelOrientation.Horizontal);
            var moving = left.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            left.RegisterWindow(m_windowFactory.CreateExplorerWindow());

            var moved = left.TransferTab(moving, right, null, null, desktop);

            Assert.IsNotNull(moved);
            Assert.AreSame(right.GetTree(desktop)!.Root, moved!.Parent);
            Assert.AreEqual(1, left.GetTree(desktop)!.Root!.Children.Count);
        }

        [TestMethod]
        public void MovingAGroupToAnEmptyDisplayPreservesTabsFocusAndOriginalPositions()
        {
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            var source = new TilingWorkspace(tabsOnly: true);
            var destination = new TilingWorkspace(tabsOnly: true);
            source.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            destination.RegisterDesktop(desktop, new Rectangle(1920, 0, 3840, 1080), PanelOrientation.Horizontal);
            var first = source.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var second = source.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var group = (StackPanelNode)first.Parent!;
            source.SetFocus(second);
            var originalPosition = source.GetOriginalPosition(second.WindowReference);

            Assert.IsTrue(source.TransferTabGroup(group, destination, desktop));
            Assert.AreEqual(0, source.GetTree(desktop)!.Root!.Children.Count);
            Assert.IsFalse(source.HasWindow(first.WindowReference));
            Assert.IsNull(source.GetFocus(desktop));
            Assert.AreSame(group, destination.GetTree(desktop)!.Root);
            CollectionAssert.AreEqual(new TilingNode[] { first, second }, new List<TilingNode>(group.Children));
            Assert.AreSame(second, destination.GetFocus(desktop));
            Assert.AreEqual(originalPosition, destination.GetOriginalPosition(second.WindowReference));
            destination.GetTree(desktop)!.Arrange();
            Assert.AreEqual(1920, second.ComputedRectangle.Left);
        }

        [TestMethod]
        public void FixedSizeWindowKeepsItsDimensionsWhenPlacedInATabGroup()
        {
            var size = new Point(1024, 768);
            var target = TilingService.CenterFixedSizeWindow(new Rectangle(-1280, 24, 0, 1023), size);
            Assert.AreEqual(size, target.Size);
            Assert.AreEqual(new Point(-1152, 139), target.TopLeft);

            var smallerTile = new Rectangle(-800, 24, 0, 624);
            var oversizedTarget = TilingService.CenterFixedSizeWindow(smallerTile, size);
            Assert.AreEqual(size, oversizedTarget.Size);
            Assert.AreEqual(smallerTile.TopLeft, oversizedTarget.TopLeft);
        }

        [TestMethod]
        public void EachTabGroupRemembersItsSelectionWhenFocusMovesToAnotherGroup()
        {
            var workspace = new TilingWorkspace(tabsOnly: true);
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            var tree = workspace.GetTree(desktop)!;
            var first = workspace.RegisterWindow(m_windowFactory.CreateNotepadWindow());
            var second = workspace.RegisterWindow(m_windowFactory.CreateNotepadWindow());
            var third = workspace.RegisterWindow(m_windowFactory.CreateNotepadWindow());
            tree.Arrange();
            Assert.IsTrue(workspace.SplitTab(third, TilingDirection.Right));
            var left = (StackPanelNode)first.Parent!;
            var right = (StackPanelNode)third.Parent!;

            workspace.SetFocus(second);
            workspace.SetFocus(third);

            Assert.AreSame(second, left.SelectedTab);
            Assert.AreSame(third, right.SelectedTab);
            workspace.UnregisterWindow(second.WindowReference);
            Assert.AreSame(first, left.SelectedTab);
            Assert.AreSame(third, right.SelectedTab);
        }

        [TestMethod]
        public void TestRemoveMissingWindow()
        {
            var workspace = new TilingWorkspace();
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            Assert.ThrowsException<ArgumentException>(() =>
            {
                workspace.UnregisterWindow(m_windowFactory.CreateExplorerWindow());
            });
        }

        [TestMethod]
        public void TestAddWindowTwice()
        {
            var workspace = new TilingWorkspace();
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            var explorer = m_windowFactory.CreateExplorerWindow();
            workspace.RegisterWindow(explorer);
            Assert.ThrowsException<WindowAlreadyRegisteredException>(() =>
            {
                workspace.RegisterWindow(explorer);
            });
        }

        [TestMethod]
        public void TestFindWindow()
        {
            var workspace = new TilingWorkspace();
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            var explorer = m_windowFactory.CreateExplorerWindow();
            workspace.RegisterWindow(explorer);

            Assert.IsNotNull(workspace.FindWindow(explorer));
        }

        [TestMethod]
        public void TestFindWindowReturnsNull()
        {
            var workspace = new TilingWorkspace();
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            var explorer = m_windowFactory.CreateExplorerWindow();
            // var node = workspace.RegisterWindow(explorer);

            Assert.IsNull(workspace.FindWindow(explorer));
        }

        [TestMethod]
        public void TestGetFocusIsNull()
        {
            var workspace = new TilingWorkspace();
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            Assert.IsNull(workspace.GetFocus(desktop));
        }

        [TestMethod]
        public void TestSetFocus()
        {
            var workspace = new TilingWorkspace();
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            var explorer = m_windowFactory.CreateExplorerWindow();
            var node = workspace.RegisterWindow(explorer);

            workspace.SetFocus(node);
            Assert.AreEqual(workspace.GetFocus(desktop), node);
        }

        [TestMethod]
        public void CtrlArrowCyclesTabsAndWraps()
        {
            var workspace = new TilingWorkspace(tabsOnly: true);
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            var first = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var second = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var third = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());

            workspace.SetFocus(second);
            Assert.AreSame(third, workspace.GetCycledTab(desktop, 1));
            Assert.AreSame(first, workspace.GetCycledTab(desktop, -1));

            workspace.SetFocus(third);
            Assert.AreSame(first, workspace.GetCycledTab(desktop, 1));
        }

        [TestMethod]
        public void CtrlShiftArrowMovesTheTabOnePositionAndStopsAtTheEnds()
        {
            var workspace = new TilingWorkspace(tabsOnly: true);
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            var first = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var second = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var group = (StackPanelNode)first.Parent!;

            workspace.SetFocus(first);
            Assert.IsFalse(workspace.MoveFocusedTab(desktop, -1));
            Assert.IsTrue(workspace.MoveFocusedTab(desktop, 1));
            Assert.AreSame(second, group.Children[0]);
            Assert.AreSame(first, group.Children[1]);
            Assert.IsFalse(workspace.MoveFocusedTab(desktop, 1));
        }

        [TestMethod]
        public void CtrlShiftNumberMovesTheTabToTheEndOfThatGroup()
        {
            var workspace = new TilingWorkspace(tabsOnly: true);
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            var tree = workspace.GetTree(desktop)!;
            var a = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var b = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var c = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            tree.Arrange();
            var first = (StackPanelNode)a.Parent!;
            Assert.IsTrue(workspace.SplitTab(c, TilingDirection.Right));
            var second = (StackPanelNode)c.Parent!;

            // Into another group: lands last there.
            workspace.SetFocus(a);
            Assert.IsTrue(workspace.MoveFocusedTabToGroup(desktop, second.GroupNumber));
            Assert.AreSame(second, a.Parent);
            Assert.AreSame(a, second.Children[^1]);

            // Within its own group: jumps to the end.
            workspace.SetFocus(c);
            Assert.IsTrue(workspace.MoveFocusedTabToGroup(desktop, second.GroupNumber));
            Assert.AreSame(c, second.Children[^1]);
            Assert.IsFalse(workspace.MoveFocusedTabToGroup(desktop, second.GroupNumber));

            // Unknown number: nothing happens.
            Assert.IsFalse(workspace.MoveFocusedTabToGroup(desktop, int.MaxValue));
            Assert.AreSame(first, b.Parent);
        }

        [TestMethod]
        public void ALoneTabRefusesToSplitAndKeepsTheLayout()
        {
            var workspace = new TilingWorkspace(tabsOnly: true);
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            var tree = workspace.GetTree(desktop)!;
            var a = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var b = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            tree.Arrange();
            Assert.IsTrue(workspace.SplitTab(b, TilingDirection.Right));
            var split = tree.Root;
            var lonely = (StackPanelNode)b.Parent!;
            workspace.SetFocus(b);

            foreach (var direction in new[] { TilingDirection.Left, TilingDirection.Right, TilingDirection.Up, TilingDirection.Down })
            {
                Assert.IsFalse(workspace.SplitTab(b, direction), direction.ToString());
                Assert.AreSame(lonely, b.Parent);
                Assert.AreSame(split, tree.Root);
                Assert.AreSame(b, workspace.GetFocus(desktop));
            }

            // The only window on a desktop cannot split either.
            var solo = new TilingWorkspace(tabsOnly: true);
            var soloDesktop = m_desktopFactory.CreateVirtualDesktop();
            solo.RegisterDesktop(soloDesktop, m_workarea, PanelOrientation.Horizontal);
            var only = solo.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            solo.GetTree(soloDesktop)!.Arrange();
            Assert.IsFalse(solo.SplitTab(only, TilingDirection.Right));
        }

        [TestMethod]
        public void TransferFocusedTabMovesItToAnotherDisplaysGroup()
        {
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            var left = new TilingWorkspace(tabsOnly: true);
            var right = new TilingWorkspace(tabsOnly: true);
            left.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            right.RegisterDesktop(desktop, new Rectangle(1920, 0, 3840, 1080), PanelOrientation.Horizontal);
            var moving = left.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var staying = left.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var resident = right.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var target = (StackPanelNode)resident.Parent!;

            left.SetFocus(moving);
            var moved = left.TransferFocusedTab(desktop, right, target);

            Assert.IsNotNull(moved);
            Assert.IsFalse(left.HasWindow(moving.WindowReference));
            Assert.IsTrue(left.HasWindow(staying.WindowReference));
            Assert.AreSame(target, moved!.Parent);
            Assert.AreSame(moved, target.Children[^1]);
            Assert.AreSame(moved, right.GetFocus(desktop));

            // Moving it again to where it already is does nothing and loses nothing.
            right.SetFocus(moved);
            Assert.IsNull(right.TransferFocusedTab(desktop, right, target));
            Assert.IsTrue(right.HasWindow(moved.WindowReference));
        }

        [TestMethod]
        public void ContainerReservesAnInnerBarAndUsesTheExistingTabShortcuts()
        {
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            var workspace = new TilingWorkspace(tabsOnly: true);
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            var tree = workspace.GetTree(desktop)!;
            var group = (StackPanelNode)tree.Root!;
            group.Padding = new Rectangle(0, 24, 0, 0);
            var first = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var second = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var third = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var container = workspace.WrapInContainer(second)!;
            Assert.AreEqual($"Group {container.GroupNumber}", container.ContainerTitle);
            Assert.AreSame(container, group.Children[0]);
            container.Name = "Work";
            Assert.AreEqual("Work", container.ContainerTitle);
            workspace.SetFocus(second);
            tree.Arrange();

            Assert.IsTrue(container.GroupNumber > 0);
            Assert.AreNotEqual(group.GroupNumber, container.GroupNumber);
            Assert.AreEqual(48, first.ComputedRectangle.Top);
            Assert.AreEqual(48, second.ComputedRectangle.Top);
            var bar = TilingWorkspace.GetContainerBarBounds(container);
            Assert.AreEqual(24, bar.Height);
            Assert.IsTrue(workspace.DropTab(third, bar.Center));
            Assert.AreSame(container, group.SelectedChild);
            Assert.AreSame(third, group.SelectedTab);

            Assert.AreSame(second, workspace.GetCycledTab(desktop, 1));
            Assert.IsTrue(workspace.MoveFocusedTab(desktop, -1));
            Assert.AreSame(third, container.Children[0]);
            Assert.IsTrue(workspace.MoveFocusedTab(desktop, 1));
            workspace.SetFocus(first);
            Assert.IsFalse(workspace.MoveFocusedTab(desktop, -1));
            Assert.IsNull(TilingWorkspace.FindContainerBarAt(group, bar.Center));
            Assert.IsNull(workspace.GetCycledTab(desktop, 1));

            Assert.IsTrue(workspace.Unpack(container));
            CollectionAssert.AreEqual(new TilingNode[] { second, third, first }, new List<TilingNode>(group.Children));
            Assert.AreSame(first, group.SelectedTab);
            Assert.AreSame(third, tree.FindNode(third.WindowReference));
        }

        [TestMethod]
        public void UnpackingAllGroupsKeepsWindowOrderAndFocus()
        {
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            var workspace = new TilingWorkspace(tabsOnly: true);
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            var a = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var b = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var loose = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var tile = loose.Parent!;
            var first = workspace.WrapInContainer(a)!;
            var second = workspace.WrapInContainer(b)!;
            workspace.SetFocus(b);

            Assert.IsTrue(workspace.UnpackGroups(desktop));
            CollectionAssert.AreEqual(new TilingNode[] { a, b, loose }, new List<TilingNode>(tile.Children));
            Assert.AreSame(b, workspace.GetFocus(desktop));
            Assert.AreSame(b, ((StackPanelNode)tile).SelectedTab);
            Assert.IsNull(first.Parent);
            Assert.IsNull(second.Parent);
            Assert.IsFalse(workspace.UnpackGroups(desktop));
        }

        [TestMethod]
        public void CreatingAGroupFromAnInnerTabMakesASiblingAndKeepsTheTabSelected()
        {
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            var workspace = new TilingWorkspace(tabsOnly: true);
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            var first = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var original = workspace.WrapInContainer(first)!;
            workspace.SetFocus(first);
            var focused = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            workspace.SetFocus(focused);
            var outer = (StackPanelNode)original.Parent!;

            var created = workspace.WrapInContainer(focused)!;

            CollectionAssert.AreEqual(new TilingNode[] { original, created }, new List<TilingNode>(outer.Children));
            Assert.AreSame(first, original.SelectedTab);
            Assert.AreSame(focused, created.SelectedTab);
            Assert.AreSame(created, outer.SelectedChild);
            Assert.AreSame(focused, workspace.GetFocus(desktop));
        }

        [TestMethod]
        public void OuterGroupShortcutsRememberWindowSelectionAndCanReorderUngrouped()
        {
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            var workspace = new TilingWorkspace(tabsOnly: true);
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            var loose = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var first = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var second = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var group = (StackPanelNode)loose.Parent!;
            var a = workspace.WrapInContainer(first)!;
            var b = workspace.WrapInContainer(second)!;
            workspace.SetFocus(loose);
            workspace.SetFocus(first);
            var inner = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            workspace.SetFocus(inner);

            Assert.AreSame(second, workspace.GetCycledGroupTab(desktop, 1));
            Assert.AreSame(loose, workspace.GetCycledGroupTab(desktop, -1));
            Assert.IsTrue(workspace.MoveFocusedGroup(desktop, 1));
            CollectionAssert.AreEqual(new TilingNode[] { b, a, loose }, new List<TilingNode>(group.Children));
            Assert.IsTrue(workspace.MoveFocusedGroup(desktop, 1));
            Assert.IsFalse(workspace.MoveFocusedGroup(desktop, 1));
            Assert.AreSame(inner, group.SelectedTab);
            Assert.AreSame(loose, workspace.GetCycledGroupTab(desktop, -1));
            Assert.AreSame(first, workspace.GetCycledTab(desktop, 1));
            workspace.SetFocus(loose);
            Assert.AreSame(second, workspace.GetCycledGroupTab(desktop, -1));
            Assert.AreSame(inner, workspace.GetCycledGroupTab(desktop, 1));
            Assert.IsTrue(workspace.MoveFocusedGroup(desktop, -1));
            Assert.AreEqual(0, group.UngroupedIndex);
            Assert.IsFalse(workspace.MoveFocusedGroup(desktop, -1));
        }

        [TestMethod]
        public void MultipleContainersAreNumberedAndComeBeforeWindowTabs()
        {
            GroupNumbers.Reset();
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            var workspace = new TilingWorkspace(tabsOnly: true);
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            var regular = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var first = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var second = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var group = (StackPanelNode)regular.Parent!;

            var container1 = workspace.WrapInContainer(first)!;
            var container2 = workspace.WrapInContainer(second)!;

            Assert.AreEqual(5, group.GroupNumber);
            Assert.AreEqual("Group 1", container1.ContainerTitle);
            Assert.AreEqual("Group 3", container2.ContainerTitle);
            CollectionAssert.AreEqual(new TilingNode[] { container1, container2, regular }, new List<TilingNode>(group.Children));
            Assert.IsTrue(workspace.Unpack(container1));
            CollectionAssert.AreEqual(new TilingNode[] { container2, first, regular }, new List<TilingNode>(group.Children));
        }

        [TestMethod]
        public void NumberedShortcutCanMoveIntoNestedAndUngroupedRows()
        {
            GroupNumbers.Reset();
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            var workspace = new TilingWorkspace(tabsOnly: true);
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            var loose = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var resident = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var outer = (StackPanelNode)loose.Parent!;
            var nested = workspace.WrapInContainer(resident)!;
            workspace.GetTree(desktop)!.Arrange();
            Assert.AreEqual(3, outer.GroupNumber);
            Assert.AreEqual(1, nested.GroupNumber);
            workspace.SetFocus(loose);
            Assert.IsTrue(workspace.MoveFocusedTabToGroup(desktop, 1));
            Assert.AreSame(nested, loose.Parent);
            Assert.AreEqual(0, outer.GroupNumber);
            Assert.AreEqual(1, nested.GroupNumber);

            workspace.SetFocus(resident);
            var ungrouped = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow(), outer);
            Assert.AreEqual(3, outer.GroupNumber);
            Assert.IsTrue(workspace.MoveFocusedTabToGroup(desktop, 3));
            Assert.AreSame(outer, resident.Parent);
            Assert.AreSame(outer, ungrouped.Parent);
        }

        [DataTestMethod]
        [DataRow(2, 2)]
        [DataRow(960, 540)]
        [DataRow(384, 540)]
        [DataRow(1535, 540)]
        public void CenterAndHeaderDropsJoinTheSelectedGroup(int x, int y)
        {
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            var workspace = new TilingWorkspace(tabsOnly: true);
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            var tree = workspace.GetTree(desktop)!;
            tree.Root!.Padding = new Rectangle(0, 24, 0, 0);
            var incoming = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var resident = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var group = workspace.WrapInContainer(resident)!;
            workspace.SetFocus(resident);
            tree.Arrange();

            var point = new Point(x, y);
            Assert.AreSame(group, workspace.GetTabDropTarget(incoming, point)?.Group);
            Assert.IsTrue(workspace.DropTab(incoming, point));
            Assert.AreSame(group, incoming.Parent);
            Assert.IsNull(workspace.GetTabDropTarget(incoming, point));
        }

        [DataTestMethod]
        [DataRow(239, TilingDirection.Left)]
        [DataRow(960, TilingDirection.Right)]
        public void InnerTabCanTileAtEitherEdgeWithoutCountingItsSizeTwice(int x, TilingDirection side)
        {
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            var workspace = new TilingWorkspace(tabsOnly: true);
            workspace.RegisterDesktop(desktop, new Rectangle(0, 0, 1200, 800), PanelOrientation.Horizontal);
            var tree = workspace.GetTree(desktop)!;
            tree.Root!.Padding = new Rectangle(0, 24, 0, 0);
            var incoming = workspace.RegisterWindow(m_windowFactory.CreateDiscordWindow());
            var resident = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var group = workspace.WrapInContainer(resident)!;
            tree.Arrange();
            Assert.IsTrue(workspace.MoveTabToContainer(incoming, group));
            tree.Arrange();

            var point = new Point(x, 400);
            Assert.AreEqual(side, workspace.GetTabDropTarget(incoming, point)?.Edge);
            Assert.IsTrue(workspace.DropTab(incoming, point));
            Assert.AreSame(group, resident.Parent);
            var split = (SplitPanelNode)tree.Root!;
            Assert.AreSame(incoming.Parent, split.Children[side == TilingDirection.Left ? 0 : 1]);
        }

        [TestMethod]
        public void MovingTheLastInnerTabToItsOuterBarRemovesTheContainer()
        {
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            var workspace = new TilingWorkspace(tabsOnly: true);
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            var tree = workspace.GetTree(desktop)!;
            var group = (StackPanelNode)tree.Root!;
            group.Padding = new Rectangle(0, 24, 0, 0);
            var tab = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var container = workspace.WrapInContainer(tab)!;
            tree.Arrange();

            Assert.IsTrue(workspace.DropTab(tab, TilingWorkspace.GetTabBarBounds(group).Center, dropOnUngrouped: true));
            Assert.IsNull(container.Parent);
            Assert.AreSame(tab, group.SelectedChild);
            Assert.AreSame(tab, tree.FindNode(tab.WindowReference));
        }

        [TestMethod]
        public void TransferringATabBetweenContainersPreservesItsNodeAndOriginalPosition()
        {
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            var left = new TilingWorkspace(tabsOnly: true);
            var right = new TilingWorkspace(tabsOnly: true);
            left.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            right.RegisterDesktop(desktop, new Rectangle(1920, 0, 3840, 1080), PanelOrientation.Horizontal);
            var tab = left.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var source = left.WrapInContainer(tab)!;
            var existing = right.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var destination = right.WrapInContainer(existing)!;
            right.GetTree(desktop)!.Arrange();
            var original = left.GetOriginalPosition(tab.WindowReference);

            Assert.AreSame(tab, left.TransferTab(tab, right, destination, null, desktop));
            Assert.IsNull(source.Parent);
            Assert.IsFalse(left.HasWindow(tab.WindowReference));
            Assert.AreSame(tab, destination.SelectedTab);
            Assert.AreEqual(original, right.GetOriginalPosition(tab.WindowReference));
        }

        [TestMethod]
        public void MergingAGroupAppendsItsTabsInOrderAndKeepsItsSelection()
        {
            GroupNumbers.Reset();
            var workspace = new TilingWorkspace(tabsOnly: true);
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            var tree = workspace.GetTree(desktop)!;
            var a = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var b = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var c = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            tree.Arrange();
            Assert.IsTrue(workspace.SplitTab(b, TilingDirection.Right));
            var target = (StackPanelNode)a.Parent!;
            var source = (StackPanelNode)b.Parent!;
            workspace.MoveTabToGroup(c, source);
            workspace.SetFocus(c);
            tree.Measure();
            tree.Arrange();
            Assert.IsTrue(TilingWorkspace.IsOnTabBar(target, new Point(10, 0)));

            Assert.IsTrue(workspace.MergeTabGroup(source, target));

            Assert.AreSame(target, tree.Root);
            CollectionAssert.AreEqual(new TilingNode[] { a, b, c }, new List<TilingNode>(target.Children));
            Assert.AreSame(c, target.SelectedTab);
            Assert.AreSame(c, workspace.GetFocus(desktop));
            Assert.AreEqual(1, target.GroupNumber);
            Assert.IsFalse(workspace.MergeTabGroup(target, target));
        }

        [TestMethod]
        public void AGroupMergesIntoAGroupOnAnotherDisplay()
        {
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            var left = new TilingWorkspace(tabsOnly: true);
            var right = new TilingWorkspace(tabsOnly: true);
            left.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            right.RegisterDesktop(desktop, new Rectangle(1920, 0, 3840, 1080), PanelOrientation.Horizontal);
            var a = left.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var b = left.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var c = right.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var group = (StackPanelNode)a.Parent!;
            var target = (StackPanelNode)c.Parent!;
            right.GetTree(desktop)!.Arrange();
            left.SetFocus(b);
            var position = left.GetOriginalPosition(a.WindowReference);

            Assert.IsTrue(left.MergeTabGroupInto(group, right, target, desktop));

            CollectionAssert.AreEqual(new TilingNode[] { c, a, b }, new List<TilingNode>(target.Children));
            Assert.AreEqual(0, left.GetTree(desktop)!.Root!.Children.Count);
            Assert.IsNull(left.GetFocus(desktop));
            Assert.AreSame(b, right.GetFocus(desktop));
            Assert.IsTrue(right.HasWindow(a.WindowReference));
            Assert.AreEqual(position, right.GetOriginalPosition(a.WindowReference));
        }

        [TestMethod]
        public void AGroupThatDoesNotFitIsNotMerged()
        {
            var workspace = new TilingWorkspace(tabsOnly: true);
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, new Rectangle(0, 0, 1200, 1100), PanelOrientation.Horizontal);
            var tree = workspace.GetTree(desktop)!;
            var small = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var large = workspace.RegisterWindow(m_windowFactory.CreateDiscordWindow());
            tree.Arrange();
            Assert.IsTrue(workspace.SplitTab(small, TilingDirection.Up));
            var target = (StackPanelNode)small.Parent!;
            var source = (StackPanelNode)large.Parent!;
            tree.Measure();
            tree.Arrange();
            // Discord needs 500 px of height; the upper group is too short after a forced shrink.
            ((SplitPanelNode)tree.Root!).ResizeDivider(0, -400);
            tree.Arrange();

            Assert.IsFalse(TilingWorkspace.CanMergeInto(source, target));
            Assert.IsFalse(workspace.MergeTabGroup(source, target));
            Assert.AreSame(source, large.Parent);
        }

        [TestMethod]
        public void GroupsSwapAcrossDisplays()
        {
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            var left = new TilingWorkspace(tabsOnly: true);
            var right = new TilingWorkspace(tabsOnly: true);
            left.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            right.RegisterDesktop(desktop, new Rectangle(1920, 0, 3840, 1080), PanelOrientation.Horizontal);
            var a = left.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var b = left.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            left.GetTree(desktop)!.Arrange();
            Assert.IsTrue(left.SplitTab(b, TilingDirection.Right));
            var c = right.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var moving = (StackPanelNode)b.Parent!;
            var target = (StackPanelNode)c.Parent!;
            left.GetTree(desktop)!.Measure();
            left.GetTree(desktop)!.Arrange();
            right.GetTree(desktop)!.Arrange();
            left.SetFocus(b);
            var position = left.GetOriginalPosition(b.WindowReference);

            Assert.IsTrue(left.MoveTabGroupTo(moving, right, target, null, desktop));

            Assert.AreSame(moving, right.GetTree(desktop)!.Root);
            Assert.IsTrue(right.HasWindow(b.WindowReference));
            Assert.IsFalse(right.HasWindow(c.WindowReference));
            Assert.IsTrue(left.HasWindow(c.WindowReference));
            Assert.IsFalse(left.HasWindow(b.WindowReference));
            Assert.AreSame(left.GetTree(desktop)!.Root, a.Parent!.Parent);
            Assert.AreSame(b, right.GetFocus(desktop));
            Assert.AreEqual(position, right.GetOriginalPosition(b.WindowReference));
            Assert.IsNotNull(left.GetOriginalPosition(c.WindowReference));
        }

        [TestMethod]
        public void GroupsTileBesideAGroupOnAnotherDisplay()
        {
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            var left = new TilingWorkspace(tabsOnly: true);
            var right = new TilingWorkspace(tabsOnly: true);
            left.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            right.RegisterDesktop(desktop, new Rectangle(1920, 0, 3840, 1080), PanelOrientation.Horizontal);
            var a = left.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var c = right.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var moving = (StackPanelNode)a.Parent!;
            var target = (StackPanelNode)c.Parent!;
            right.GetTree(desktop)!.Arrange();

            Assert.IsTrue(left.MoveTabGroupTo(moving, right, target, TilingDirection.Left, desktop));

            var split = (SplitPanelNode)right.GetTree(desktop)!.Root!;
            CollectionAssert.AreEqual(new TilingNode[] { moving, target }, new List<TilingNode>(split.Children));
            Assert.AreEqual(0, left.GetTree(desktop)!.Root!.Children.Count);
            Assert.IsFalse(left.HasWindow(a.WindowReference));
            right.GetTree(desktop)!.Measure();
            right.GetTree(desktop)!.Arrange();
            Assert.AreEqual(1920, a.ComputedRectangle.Left);
            Assert.AreEqual(a.ComputedRectangle.Right, c.ComputedRectangle.Left);
        }

        [TestMethod]
        public void GroupsAreNumberedFromOneAndReuseFreedNumbers()
        {
            GroupNumbers.Reset();
            var workspace = new TilingWorkspace(tabsOnly: true);
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            var tree = workspace.GetTree(desktop)!;

            // Empty groups (one per display and desktop) never take a number.
            Assert.AreEqual(0, ((StackPanelNode)tree.Root!).GroupNumber);

            var first = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var second = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var third = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            tree.Arrange();
            var original = (StackPanelNode)first.Parent!;
            Assert.AreEqual(1, original.GroupNumber);

            Assert.IsTrue(workspace.SplitTab(second, TilingDirection.Right));
            var split = (StackPanelNode)second.Parent!;
            Assert.AreEqual(3, split.GroupNumber);

            // Closing the second group frees 3 for the next split.
            workspace.UnregisterWindow(second.WindowReference);
            tree.Measure();
            tree.Arrange();
            Assert.IsTrue(workspace.SplitTab(third, TilingDirection.Right));
            Assert.AreEqual(3, ((StackPanelNode)third.Parent!).GroupNumber);
            Assert.AreEqual(1, original.GroupNumber);
        }

        [TestMethod]
        public void ClosingAGroupMovesTheLaterNumbersUpInItsSequence()
        {
            GroupNumbers.Reset();
            var workspace = new TilingWorkspace(tabsOnly: true);
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            var tree = workspace.GetTree(desktop)!;
            var a = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var b = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var c = workspace.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            tree.Arrange();
            Assert.IsTrue(workspace.SplitTab(b, TilingDirection.Right));
            tree.Measure();
            tree.Arrange();
            Assert.IsTrue(workspace.SplitTab(c, TilingDirection.Down));
            var first = (StackPanelNode)a.Parent!;
            var second = (StackPanelNode)b.Parent!;
            var third = (StackPanelNode)c.Parent!;
            Assert.AreEqual(1, first.GroupNumber);
            Assert.AreEqual(3, second.GroupNumber);
            Assert.AreEqual(5, third.GroupNumber);

            // Group 1 closes: the remaining groups become 1 and 3, keeping their order.
            workspace.UnregisterWindow(a.WindowReference);
            Assert.AreEqual(1, second.GroupNumber);
            Assert.AreEqual(3, third.GroupNumber);
        }

        [TestMethod]
        public void PrimaryUsesOddNumbersAndSecondaryUsesEvenNumbersInRowPriorityOrder()
        {
            GroupNumbers.Reset();
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            var right = new TilingWorkspace(tabsOnly: true);
            var left = new TilingWorkspace(tabsOnly: true);
            right.RegisterDesktop(desktop, new Rectangle(-1920, 0, 0, 1080), PanelOrientation.Horizontal);
            right.GetTree(desktop)!.GroupNumberLane = 1;
            left.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            var rightLoose = right.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var rightNested = right.WrapInContainer(right.RegisterWindow(m_windowFactory.CreateExplorerWindow()))!;
            var leftLoose = left.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var leftNested = left.WrapInContainer(left.RegisterWindow(m_windowFactory.CreateExplorerWindow()))!;
            var splitTab = left.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            left.GetTree(desktop)!.Arrange();
            Assert.IsTrue(left.SplitTab(splitTab, TilingDirection.Right));

            Assert.AreEqual(1, leftNested.GroupNumber);
            Assert.AreEqual(3, ((StackPanelNode)splitTab.Parent!).GroupNumber);
            Assert.AreEqual(5, ((StackPanelNode)leftLoose.Parent!).GroupNumber);
            Assert.AreEqual(2, rightNested.GroupNumber);
            Assert.AreEqual(4, ((StackPanelNode)rightLoose.Parent!).GroupNumber);
            Assert.AreSame(leftNested, left.FindGroup(desktop, 1));
            Assert.AreSame(rightNested, right.FindGroup(desktop, 2));
            Assert.IsNull(left.FindGroup(desktop, 2));
            Assert.IsNull(right.FindGroup(desktop, 1));

            left.GetTree(desktop)!.GroupNumberLane = 1;
            right.GetTree(desktop)!.GroupNumberLane = 0;
            Assert.AreEqual(2, leftNested.GroupNumber);
            Assert.AreEqual(1, rightNested.GroupNumber);
        }

        [TestMethod]
        public void RemovingALayoutFreesItsStandaloneAndNestedGroupNumbers()
        {
            GroupNumbers.Reset();
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            var removed = new TilingWorkspace(tabsOnly: true);
            var remaining = new TilingWorkspace(tabsOnly: true);
            removed.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            remaining.RegisterDesktop(desktop, new Rectangle(1920, 0, 3840, 1080), PanelOrientation.Horizontal);
            var a = removed.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var b = removed.RegisterWindow(m_windowFactory.CreateExplorerWindow());
            var outer = (StackPanelNode)a.Parent!;
            var nested = removed.WrapInContainer(b)!;
            var survivor = (StackPanelNode)remaining.RegisterWindow(m_windowFactory.CreateExplorerWindow()).Parent!;
            Assert.AreEqual(3, survivor.GroupNumber);

            removed.UnregisterDesktop(desktop);

            Assert.AreEqual(1, survivor.GroupNumber);
            Assert.AreEqual(0, outer.GroupNumber);
            Assert.AreEqual(0, nested.GroupNumber);
        }

        [TestMethod]
        public void TestGetOriginalPosition()
        {
            var workspace = new TilingWorkspace();
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);

            var explorer = m_windowFactory.CreateExplorerWindow();
            workspace.RegisterWindow(explorer);

            Assert.AreEqual(workspace.GetOriginalPosition(explorer), explorer.Position);
        }

        [TestMethod]
        public void TestGetOriginalPositionNotRegistered()
        {
            var workspace = new TilingWorkspace();
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);

            var explorer = m_windowFactory.CreateExplorerWindow();
            //var node = workspace.RegisterWindow(explorer);

            Assert.ThrowsException<KeyNotFoundException>(() =>
            {
                Assert.AreEqual(workspace.GetOriginalPosition(explorer), explorer.Position);
            });
        }

        [TestMethod]
        public void TestGetTree()
        {
            var workspace = new TilingWorkspace();
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);

            Assert.IsNotNull(workspace.GetTree(desktop));
        }


        [TestMethod]
        public void TestGetTreeNotRegistered()
        {
            var workspace = new TilingWorkspace();
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            // workspace.RegisterDesktop(desktop, m_rectangle, PanelOrientation.Horizontal);

            Assert.IsNull(workspace.GetTree(desktop));
        }


        [TestMethod]
        public void TestHasWindow()
        {
            var workspace = new TilingWorkspace();
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);

            var explorer = m_windowFactory.CreateExplorerWindow();
            workspace.RegisterWindow(explorer);

            Assert.IsTrue(workspace.HasWindow(explorer));
        }

        [TestMethod]
        public void TestHasWindowNotRegistered()
        {
            var workspace = new TilingWorkspace();
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);

            var explorer = m_windowFactory.CreateExplorerWindow();
            //var node = workspace.RegisterWindow(explorer);

            Assert.IsFalse(workspace.HasWindow(explorer));
        }


        [TestMethod]
        public void TestMoveNodeFlat()
        {
            var workspace = new TilingWorkspace();
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);

            var explorer1 = m_windowFactory.CreateExplorerWindow();
            var node1 = workspace.RegisterWindow(explorer1);

            var explorer2 = m_windowFactory.CreateExplorerWindow();
            var node2 = workspace.RegisterWindow(explorer2);

            var tree = workspace.GetTree(desktop);
            tree.WorkArea = new WinMan.Rectangle(0, 0, 2000, 2000);
            tree.Measure();
            tree.Arrange();

            workspace.MoveNode(node1, node2.ComputedRectangle.Center);

            Assert.AreEqual(node1.Parent.IndexOf(node1), 1);
            Assert.AreEqual(node2.Parent.IndexOf(node2), 0);
        }

        [TestMethod]
        public void TestMoveNodeFlatLong()
        {
            var workspace = new TilingWorkspace();
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);

            var explorer1 = m_windowFactory.CreateExplorerWindow();
            var node1 = workspace.RegisterWindow(explorer1);

            var explorer2 = m_windowFactory.CreateExplorerWindow();
            var node2 = workspace.RegisterWindow(explorer2);

            var explorer3 = m_windowFactory.CreateExplorerWindow();
            var node3 = workspace.RegisterWindow(explorer3);

            var explorer4 = m_windowFactory.CreateExplorerWindow();
            var node4 = workspace.RegisterWindow(explorer4);

            var explorer5 = m_windowFactory.CreateExplorerWindow();
            var node5 = workspace.RegisterWindow(explorer5);

            var tree = workspace.GetTree(desktop);
            tree.WorkArea = new WinMan.Rectangle(0, 0, 2000, 2000);
            tree.Measure();
            tree.Arrange();

            void AssertPositions(params TilingNode[] nodes)
            {
                var parent = tree.Root;
                for (int i = 0; i < nodes.Length; i++)
                {
                    Assert.AreEqual(parent.IndexOf(nodes[i]), i);
                }
            }

            AssertPositions(node1, node2, node3, node4, node5);

            workspace.MoveNode(node1, node2.ComputedRectangle.Center);
            tree.Measure();
            tree.Arrange();

            AssertPositions(node2, node1, node3, node4, node5);

            workspace.MoveNode(node3, node2.ComputedRectangle.Center);
            tree.Measure();
            tree.Arrange();

            AssertPositions(node3, node2, node1, node4, node5);

            workspace.MoveNode(node5, node3.ComputedRectangle.Center);
            tree.Measure();
            tree.Arrange();

            AssertPositions(node5, node3, node2, node1, node4);

            workspace.MoveNode(node2, node1.ComputedRectangle.Center);
            tree.Measure();
            tree.Arrange();

            AssertPositions(node5, node3, node1, node2, node4);
        }

        [TestMethod]
        public void TestMoveWindowFlat()
        {
            var workspace = new TilingWorkspace();
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);

            var explorer1 = m_windowFactory.CreateExplorerWindow();
            var node1 = workspace.RegisterWindow(explorer1);

            var explorer2 = m_windowFactory.CreateExplorerWindow();
            var node2 = workspace.RegisterWindow(explorer2);

            var tree = workspace.GetTree(desktop);
            tree.WorkArea = new WinMan.Rectangle(0, 0, 2000, 2000);
            tree.Measure();
            tree.Arrange();

            workspace.MoveWindow(explorer1, node2.ComputedRectangle.Center, allowNesting: false);

            Assert.AreEqual(node1.Parent.IndexOf(node1), 1);
            Assert.AreEqual(node2.Parent.IndexOf(node2), 0);
        }

        [TestMethod]
        public void TestMoveAfterSameParent()
        {
            var workspace = new TilingWorkspace();
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);

            var explorer1 = m_windowFactory.CreateExplorerWindow();
            var node1 = workspace.RegisterWindow(explorer1);

            var explorer2 = m_windowFactory.CreateExplorerWindow();
            var node2 = workspace.RegisterWindow(explorer2);

            var explorer3 = m_windowFactory.CreateExplorerWindow();
            var node3 = workspace.RegisterWindow(explorer3);

            workspace.SetFocus(node1);
            Assert.ThrowsException<ArgumentException>(() =>
            {
                workspace.MoveAfter(node1, node2);
            });
        }

        [TestMethod]
        public void TestMoveBeforeSameParent()
        {
            var workspace = new TilingWorkspace();
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);

            var explorer1 = m_windowFactory.CreateExplorerWindow();
            var node1 = workspace.RegisterWindow(explorer1);

            var explorer2 = m_windowFactory.CreateExplorerWindow();
            var node2 = workspace.RegisterWindow(explorer2);

            var explorer3 = m_windowFactory.CreateExplorerWindow();
            var node3 = workspace.RegisterWindow(explorer3);

            workspace.SetFocus(node1);
            Assert.ThrowsException<ArgumentException>(() =>
            {
                workspace.MoveBefore(node1, node2);
            });
        }

        [TestMethod]
        public void TestPullUpFromChain()
        {
            var workspace = new TilingWorkspace();
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            var root = workspace.GetTree(desktop).Root;

            var explorer1 = m_windowFactory.CreateExplorerWindow();
            var node1 = workspace.RegisterWindow(explorer1);

            var explorer2 = m_windowFactory.CreateExplorerWindow();
            var node2 = workspace.RegisterWindow(explorer2);

            workspace.WrapInSplitPanel(node1, true);
            node2.Parent.Detach(node2);
            node1.Parent.Attach(node2);
            workspace.WrapInStackPanel(node1);
            node2.Parent.Detach(node2);

            Assert.AreEqual(root.Children.Count, 1);
            var splitPanelNode = (SplitPanelNode)root.Children[0];
            Assert.AreEqual(splitPanelNode.Children.Count, 1);
            var stackPanelNode = (StackPanelNode)splitPanelNode.Children[0];
            Assert.AreEqual(stackPanelNode.Children.Count, 1);
            Assert.AreEqual(stackPanelNode.Children[0], node1);

            workspace.PullUp(node1);
        }

        [TestMethod]
        public void TestRealWorkspaceInstance()
        {
            for (int i = 0; i < 2; i++)
            {
                using var workspace = new WinMan.Windows.Win32Workspace();
                workspace.Open();
                Assert.IsTrue(workspace.VirtualDesktopManager.CanManageVirtualDesktops);
                Assert.IsTrue(workspace.DisplayManager.Displays.Count > 0);
            }
        }
    }
}
