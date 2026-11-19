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
            Assert.AreSame(second, first.GetAdjacentWindow(TilingDirection.Right));

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
        public void TestGetFocusAdjacentWindowNoFocus()
        {
            var workspace = new TilingWorkspace();
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            var explorer = m_windowFactory.CreateExplorerWindow();
            var node = workspace.RegisterWindow(explorer);

            Assert.ThrowsException<TilingFailedException>(() =>
            {
                Assert.IsNull(workspace.GetFocusAdjacentWindow(desktop, TilingDirection.Left));
            });
            Assert.ThrowsException<TilingFailedException>(() =>
            {
                Assert.IsNull(workspace.GetFocusAdjacentWindow(desktop, TilingDirection.Up));
            });
            Assert.ThrowsException<TilingFailedException>(() =>
            {
                Assert.IsNull(workspace.GetFocusAdjacentWindow(desktop, TilingDirection.Right));
            });
            Assert.ThrowsException<TilingFailedException>(() =>
            {
                Assert.IsNull(workspace.GetFocusAdjacentWindow(desktop, TilingDirection.Down));
            });
        }

        [TestMethod]
        public void TestGetFocusAdjacentWindowNoAdjacent()
        {
            var workspace = new TilingWorkspace();
            var desktop = m_desktopFactory.CreateVirtualDesktop();
            workspace.RegisterDesktop(desktop, m_workarea, PanelOrientation.Horizontal);
            var explorer = m_windowFactory.CreateExplorerWindow();
            var node = workspace.RegisterWindow(explorer);

            workspace.SetFocus(node);

            Assert.ThrowsException<TilingFailedException>(() =>
            {
                Assert.IsNull(workspace.GetFocusAdjacentWindow(desktop, TilingDirection.Left));
            });
            Assert.ThrowsException<TilingFailedException>(() =>
            {
                Assert.IsNull(workspace.GetFocusAdjacentWindow(desktop, TilingDirection.Up));
            });
            Assert.ThrowsException<TilingFailedException>(() =>
            {
                Assert.IsNull(workspace.GetFocusAdjacentWindow(desktop, TilingDirection.Right));
            });
            Assert.ThrowsException<TilingFailedException>(() =>
            {
                Assert.IsNull(workspace.GetFocusAdjacentWindow(desktop, TilingDirection.Down));
            });
        }

        [TestMethod]
        public void TestGetFocusAdjacentWindowFlat()
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

            workspace.SetFocus(node3);

            Assert.AreEqual(workspace.GetFocusAdjacentWindow(desktop, TilingDirection.Left), node2);
            Assert.AreEqual(workspace.GetFocusAdjacentWindow(desktop, TilingDirection.Right), node4);
            Assert.ThrowsException<TilingFailedException>(() =>
            {
                Assert.IsNull(workspace.GetFocusAdjacentWindow(desktop, TilingDirection.Up));
            });
            Assert.ThrowsException<TilingFailedException>(() =>
            {
                Assert.IsNull(workspace.GetFocusAdjacentWindow(desktop, TilingDirection.Down));
            });
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
