# WinTabs user guide

WinTabs puts your Windows apps into tabs, like a browser. You can keep several apps in the same spot or split the screen so you can see them side by side.

## Get started

- **Open WinTabs.** Open a few apps, then click their tabs at the top of the screen to switch between them. The highlighted tab is the window you’re using.
- **Close a window.** Hover over its tab and click the **×**, or middle-click the tab. This closes the app window, not just its tab.
- **Find Settings.** Right-click the WinTabs icon next to the Windows clock and choose **Settings**. If you don’t see the icon, check the hidden icons arrow. Use **Quit** in the same menu to stop WinTabs.
- **See the shortcuts.** Press `Ctrl + Alt + /` or choose **Keyboard shortcuts** from the tray menu.

## Move or tile a window

Drag the window by its normal Windows title bar. Look for the highlight before you let go:

- **Middle 60% of a tile:** Join its selected group. If your window is already in that group, there’s nothing to move.
- **Left or right 20%:** Create a tile beside the target. This works even when the window starts inside a named group.
- **A group tab in the top row:** Send the window straight to that group, even if it isn’t selected. Drop on **Ungrouped** to take a window out of a named group when that row exists.
- **No highlight:** WinTabs isn’t offering a drop there. Some windows can’t shrink enough to fit a split.

You can also right-click a window tab and choose **Tile left**, **Tile right**, **Tile above**, or **Tile below**. Drag the divider between tiles to resize them.

## Organize windows into named groups

- **Create a group.** Right-click a window tab and choose **Put in new Group**. The top row shows your groups, and the bottom row shows the windows in the group you’ve selected. Windows outside named groups appear under **Ungrouped**.
- **Add a window.** Right-click its tab and choose **Move to group**, or drag its title bar onto a named group tab or into the middle of the tile while that group is selected.
- **Rename or color a group.** Double-click its tab to rename it, or right-click and choose **Rename group** or **Group color**. Names and colors last while the group exists.
- **Take windows out.** Drop a window on **Ungrouped** if it’s visible. To take every window out at once, right-click the named group and choose **Unpack group**. This doesn’t close any windows. An empty named group disappears.

## Move, merge, or swap whole tiles

A tile is one area of the screen. It can hold one row of window tabs or several named groups. To move the whole tile, **drag a red number badge**, not a window’s title bar.

- **Drop on another tile’s top tab strip to merge.** The two tiles become one, keeping their windows and named groups.
- **Drop in the middle of another tile’s window area to swap.** The two tiles trade places; their tabs don’t mix.
- **Drop near an edge of another tile to tile beside it.** Use the left, right, top, or bottom edge to place the whole tile on that side.
- **Drop on an empty monitor to move the tile there.** You can also merge, swap, and tile across monitors when **Use tabs on all displays** is on.

## Keyboard shortcuts

- `Ctrl + arrows`: Switch window tabs in the current row.
- `Ctrl + Shift + arrows`: Move the active window tab one position in its row.
- `Ctrl + Win + arrows`: Switch groups in the top row, including Ungrouped.
- `Ctrl + Win + Shift + arrows`: Move the active named group one position. Ungrouped stays last.
- `Ctrl + Shift + 1–9`: Send the active window to the group with that red number, even on another monitor. Numbers are assigned screen by screen, with named groups first, standalone groups next, and Ungrouped last on each screen. They can change as groups move or close.
- `Ctrl + Alt + arrows`: Tile the active window on that side. Use up or down for a vertical split.
- `Ctrl + Alt + /`: Show or hide the shortcut cheat sheet.

## Let a window float

A floating window isn’t tied to a tab. You can move and resize it normally.

- **Float just one window.** Move the pointer near the top edge of its window to reveal the small controls, then click **Float window**. On a floating window, use **Put back in tabs** to return it.
- **Always float an app.** Open the **⋯** menu near the top of the window and choose **Always float**. You can also open **Settings → Rules → Add app** and pick a running app.
- **Remove a rule.** Click the **×** next to its entry in Rules or choose **Stop always floating** from the window’s **⋯** menu. Windows that were floating only because of that rule return to tabs if they can be tiled.
- **Float only certain dialogs.** **Settings → Rules → Advanced: window class rules** matches an internal window type instead of the whole app. Only use this if you know the window class.

## Make WinTabs yours

- **Settings → General:** Start WinTabs when you sign in, manage all displays or just the primary one, turn failure sounds and virtual desktop warnings on or off, or open the settings and logs folder. Restart WinTabs after changing the display setting.
- **Settings → Interface:** Choose light, dark, or the Windows theme. Change the active tab color, tab bar height, text size, or **Window gap**. The gap controls the spacing around tab strips, tiled windows, and the work area edges, including the taskbar.
- **Virtual desktops:** Each Windows virtual desktop has its own layout when supported. WinTabs pauses layout changes while Windows is locked or asleep and reapplies the layout on return. Layouts and named groups are not saved when you quit WinTabs.
