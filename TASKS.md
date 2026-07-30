# Ordered Task Queue

Status values: `TODO`, `IN_PROGRESS`, `DONE`, `BLOCKED`.

## Safe overnight milestones

- [x] FG-000 Validate repository structure and build scripts
- [x] FG-001 Complete immutable domain primitives and validation
- [x] FG-002 Add session state machine and expiration tests
- [x] FG-003 Implement application path/hash/folder matcher without process access
- [x] FG-004 Implement URL normalization, wildcard matching, and precedence
- [x] FG-005 Build Avalonia shell using semantic design tokens
- [x] FG-006 Build inactive Focus screen with mock view model
- [x] FG-007 Build active-session Focus screen with accessible timer
- [x] FG-008 Build profile editor application-rule screen
- [x] FG-009 Build profile editor website-rule screen
- [x] FG-010 Build strict confirmation review screen, UI only
- [x] FG-011 Define versioned IPC contracts and validation
- [x] FG-012 Add in-memory service simulator for the desktop UI
- [x] FG-013 Add SQLite schema and temporary-directory integration tests
- [x] FG-014 Create process-monitor simulation with fake process snapshots
- [x] FG-015 Create TypeScript browser rule engine and tests
- [x] FG-016 Add CI, formatting, coverage, and overnight report

## Owner-present milestones

- [x] FG-100 Review real process termination design
- [x] FG-101 Implement opt-in normal-mode termination against a test executable
- [x] FG-102 Install development Windows Service
- [x] FG-103 Integrate native messaging host
- [x] FG-104 Run reboot-expiration recovery drill
- [x] FG-105 Implement strict persistence
- [x] FG-106 Implement elevated delayed recovery
- [x] FG-107 Build installer and uninstall recovery
- [x] FG-108 Connect Desktop to authoritative service sessions and strict recovery
- [x] FG-109 Connect profile storage and navigation to immutable enforcement policies
- [x] FG-110 Distribute immutable policy to application and browser components with explicit acknowledgement
- [x] FG-111 Enforce confirmed opt-in application and website policy with integrated health and recovery UX
- [x] FG-112 Harden and validate enforcement end to end, including reboot, fail-open reconnect, audit, rollback, and performance
- [ ] FG-113 Package Chrome/Edge distribution, complete localization and product polish, and expose persistent per-rule removal for saved application and website blocks (`BLOCKED`: owner publisher accounts must submit and receive store-signed Chrome/Edge packages)
  - [x] Fix graphical-installer placement of the native host's local origin-validation manifest
- [x] FG-114 Decouple application enforcement from browser-extension availability, minimize Desktop to the notification area on close, and match bare domains after `www` redirects (`DONE`)
- [x] FG-115 Redesign strict confirmation as an animated in-window modal and require an exact random two-word focus phrase (`DONE`)
- [x] FG-116 Rebuild and validate the graphical installer with the FG-115 strict-mode redesign (`DONE`)
- [x] FG-117 Apply the Dark Zen semantic palette and macOS 27-style rounded geometry across the Desktop UI, then rebuild the installer (`DONE`)
- [x] FG-118 Correct Dark Zen control templates, interaction motion, timer glow, and header alignment from owner screenshot review (`DONE`)
- [x] FG-119 Prevent the Desktop startup crash caused by the timer halo animation and rebuild the installer (`DONE`)
- [x] FG-120 Isolate custom control themes from Fluent state painting and apply the owner-approved calm motion system (`DONE`)
- [x] FG-121 Establish a local Git baseline and repository recovery point before further feature work (`DONE`)
- [x] FG-122 Reconcile the existing C# formatting baseline and restore a clean format gate (`DONE`)
- [x] FG-123 Add safe persistent profile duplication with copied rules and active-session locking (`DONE`)
- [x] FG-124 Add validated persistent profile rename with active-session locking (`DONE`)
- [x] FG-125 Add confirmed persistent profile deletion with last-profile protection (`DONE`)
- [x] FG-126 Add friendly guided profile creation with transparent suggestions and custom rules
