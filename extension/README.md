# FocusGuard browser rule engine

This package contains the local, dependency-free URL rule engine used by the future Manifest V3 extension. It evaluates only the URL supplied by the browser event, strips query strings and fragments before matching/logging, fails open for browser-internal or malformed URLs, and never transmits browsing history.

Run tests with:

```powershell
npm test
```
