# WinTabs

WinTabs starts with one tab group per monitor and virtual desktop. Click a tab to focus its window. Use `Ctrl` + arrow to switch tabs, `Ctrl` + `Shift` + arrow to move a tab, `Ctrl` + `Shift` + `1`–`9` to send a tab to the end of that numbered group, and `Ctrl` + `Alt` + arrow to tile the active tab beside its group. Press `Ctrl` + `Alt` + `/` for a cheat sheet of all shortcuts. Each group shows its number in a red badge; drag the badge to move the group.

- **Tile:** Right-click a tab and choose **Tile left**, **Tile right**, **Tile above**, or **Tile below** to make a second group.
- **Move a group:** Drag the circular handle or any empty space on its tab bar. Drop on another tab bar to swap groups, near the edge of another group's window area to tile beside it, or onto an empty monitor to move the entire group. Moving a group between monitors requires multi-monitor support.
- **Move a tab:** Drag the app window by its normal title bar; its tab follows. Drop onto another group's tab bar or the center of its window area to join it. Drop near a left or right edge to tile beside the group, including splitting the original group in two. No modifier key is needed.
- **Resize:** Drag the divider between two groups. The split adjusts when you release the mouse.

WinTabs pauses layout changes while Windows is locked or asleep and reapplies the existing tab groups after unlock or wake. Layout recovery is in-session; it does not persist across app restarts or sign-out.


Only the tab whose app window has Windows focus is highlighted; other tabs fade. Each group still remembers its selected tab. Choose the Windows accent or a Catppuccin swatch under **Settings → Interface**. Swatches use Latte in light mode and Mocha in dark mode.

WinTabs follows the Windows app theme by default. To override it, open **Settings → Interface → Appearance** and choose **Light** or **Dark**. Custom theme files and accent overrides are not loaded.

## Run from source

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) on Windows, then run from the repository root:

```powershell
dotnet run --project .\wintabs\wintabs.csproj
```

The dependency sources are included in the repository; there are no Git submodules to initialize. The first build may need to restore NuGet packages. For a Windows packaging build, open `wintabs.sln` in Visual Studio with the Windows Application Packaging Project tooling installed. To distribute a signed package, use your own signing certificate and matching publisher identity.

The unpackaged app keeps settings and logs under `%APPDATA%\wintabs`. It does not import settings or startup entries from other installations.

## Test layout recovery

Open several normal, resizable windows on each monitor. Select a tab, lock Windows with `Win` + `L`, and sign back in. Repeat after using **Start → Power → Sleep**. After a short delay, verify that the tab order, position, and monitor assignment are unchanged. Test with an external monitor if possible; if a monitor stays disconnected, its windows may join a group on a remaining display.

## License

See [LICENSE](LICENSE) for the terms that apply to the application source, and [Third-party notices](THIRD-PARTY-NOTICES.md) for bundled dependency licenses. The application license restricts providing competing products; review it before distributing a build.
