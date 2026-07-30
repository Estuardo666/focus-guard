# FocusGuard graphical installer

The distribution includes `FocusGuard.Setup.exe`, a self-contained Windows
installer UI. Its embedded manifest requests administrator elevation. The GUI
delegates installation and uninstallation to the reviewed PowerShell scripts;
it does not duplicate service, ACL, native-host, or rollback logic.

Branding comes from `assets/branding/focusguard-icon.png` (the owner-provided
shield/camera icon). The build preserves that source and generates the Setup
`.ico` plus the 16/32/48/128 pixel browser icons from it.

Keep the executable beside `install-focusguard.ps1`, `uninstall-focusguard.ps1`,
`publish/`, `native-messaging/`, `browser-extension/`, and `manifest.json`.
The operation log shows script output and exit status. Uninstallation requires
confirmation and remains guarded by strict-session recovery checks.

Build with `.\scripts\build-installer.ps1`. The executable is at
`artifacts/installer/FocusGuard-0.1.0/FocusGuard.Setup.exe`, and the complete
archive is `artifacts/installer/FocusGuard-0.1.0-win-x64.zip`.

The FG-117 Dark Zen build was emitted to the alternate, reviewable package
directory `artifacts/installer/FocusGuard-0.1.0-dark-zen/` because the default
directory was open in Explorer during packaging. Its complete archive is
`artifacts/installer/FocusGuard-0.1.0-dark-zen-win-x64.zip`.

The FG-118 screenshot-review build, including corrected control templates and
motion, is in `artifacts/installer/FocusGuard-0.1.0-ui-motion/`. Its complete
archive is `artifacts/installer/FocusGuard-0.1.0-ui-motion-win-x64.zip`.

Do not reinstall the FG-118 package: its unsupported transform keyframe causes
the Desktop to terminate during XAML initialization. The corrected FG-119
package is in `artifacts/installer/FocusGuard-0.1.0-startup-fix/`, with complete
archive `artifacts/installer/FocusGuard-0.1.0-startup-fix-win-x64.zip`.

The FG-120 package isolates FocusGuard-owned button/navigation/preset templates
from Fluent state painting and applies the calm-motion review. It is in
`artifacts/installer/FocusGuard-0.1.0-calm-motion/`, with complete archive
`artifacts/installer/FocusGuard-0.1.0-calm-motion-win-x64.zip`.
