# ADR 0001: C# .NET 10 and Avalonia

## Status

Accepted.

## Decision

Use C# and .NET 10 for Windows integration and core services. Use Avalonia for the desktop UI. Use TypeScript for the browser extension.

## Rationale

This balances Windows integration, development velocity, testability, and a modern highly customizable UI.

Rust may be introduced only for a measured, isolated need supported by an ADR.
