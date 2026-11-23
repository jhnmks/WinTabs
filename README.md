# WinTabs

WinTabs starts with one tab group per monitor and virtual desktop. Click a tab to focus its window. Use `Ctrl` + arrow to switch tabs, `Ctrl` + `Shift` + arrow to move a tab, `Ctrl` + `Shift` + `1`–`9` to send a tab to the end of that numbered group, and `Ctrl` + `Alt` + arrow to tile the active tab beside its group. Press `Ctrl` + `Alt` + `/` for a cheat sheet of all shortcuts. Each group shows its number in a red badge; drag the badge to move the group.

- **Tile:** Right-click a tab and choose **Tile left**, **Tile right**, **Tile above**, or **Tile below** to make a second group.
- **Move a group:** Drag the circular handle or any empty space on its tab bar. Drop on another group's tab bar to merge the two groups into one, in the middle of its window area to swap places, near an edge of its window area to tile beside it, or onto an empty monitor to move the entire group. This works across monitors too (with multi-monitor support on).
- **Move a tab:** Drag the app window by its normal title bar; its tab follows, also to another monitor. Drop onto another group's tab bar or the center of its window area to join it. Drop in the outer quarter of a group's window area to tile beside it on that side, including splitting the original group in two. A highlight shows where it will land; where there is no highlight, the drop does nothing. No modifier key is needed.
- **Resize:** Drag the divider between two groups. The split adjusts when you release the mouse.

WinTabs pauses layout changes while Windows is locked or asleep and reapplies the existing tab groups after unlock or wake. Layout recovery is in-session; it does not persist across app restarts or sign-out.


Only the tab whose app window has Windows focus is highlighted; other tabs fade. Each group still remembers its selected tab. Choose the Windows accent or a Catppuccin swatch under **Settings → Interface**. Swatches use Latte in light mode and Mocha in dark mode.

WinTabs follows the Windows app theme by default. To override it, open **Settings → Interface → Appearance** and choose **Light** or **Dark**. Custom theme files and accent overrides are not loaded.

## Install

Download `WinTabs-<version>-setup.exe` from [Releases](https://github.com/jhnmks/WinTabs/releases) and run it. It installs for the current user only (no administrator prompt) and includes everything it needs; .NET does not have to be installed. The installer is not code-signed yet, so Windows SmartScreen may show **Windows protected your PC**: choose **More info → Run anyway**. Uninstall from **Settings → Apps → Installed apps**.

## Run from source

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) on Windows, then run from the repository root:

```powershell
dotnet run --project .\wintabs\wintabs.csproj
```

The dependency sources are included in the repository; there are no Git submodules to initialize. The first build may need to restore NuGet packages.

WinTabs keeps settings and logs under `%APPDATA%\wintabs`.

## Build the installer

```powershell
.\installer\build.ps1               # version from Directory.Build.props
.\installer\build.ps1 -Version 1.0.0
```

This writes `artifacts\WinTabs-<version>-setup.exe`; the Inno Setup compiler is downloaded on first use. To publish a release, push a version tag such as `v1.0.0`: GitHub Actions runs the tests, builds the installer, and attaches it to a GitHub release (tags with a suffix, like `v1.0.0-beta`, become pre-releases).

## Test layout recovery

Open several normal, resizable windows on each monitor. Select a tab, lock Windows with `Win` + `L`, and sign back in. Repeat after using **Start → Power → Sleep**. After a short delay, verify that the tab order, position, and monitor assignment are unchanged. Test with an external monitor if possible; if a monitor stays disconnected, its windows may join a group on a remaining display.

## License

See [LICENSE](LICENSE) for the terms that apply to the application source, and [Third-party notices](THIRD-PARTY-NOTICES.md) for bundled dependency licenses. The application license restricts providing competing products; review it before distributing a build.
