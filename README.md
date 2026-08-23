# MyTools

[English](README.md) | [简体中文](README.zh-CN.md)

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Build and release](https://github.com/qpingcode/MyTools/actions/workflows/release.yml/badge.svg)](https://github.com/qpingcode/MyTools/actions/workflows/release.yml)

MyTools is a Windows desktop productivity application built with .NET 8 and WPF. It provides fast global search, system tray integration, startup options, clipboard utilities, and an extensible plugin runtime.

## Features

- Open real-time search from anywhere with a configurable global hotkey.
- Run from the system tray with single-instance protection.
- Configure automatic startup, search shortcuts, and general application settings.
- Use built-in tools for files, commands, search engines, processes, bookmarks, calculations, UUIDs, JSON, and XML.
- Load and search extensible Node.js and web plugins.
- Install and update the application through Velopack.
- Keep configuration, databases, plugins, and WebView2 data outside the installation directory.

## System Requirements

- Windows 10 or Windows 11, x64.
- The official release is self-contained and does not require a separate .NET Desktop Runtime or Node.js installation.
- Building from source requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

## Installation

MyTools is published on two channels:

<!-- mytools-downloads:start -->
| Channel | Version | Installer (full setup) | Portable |
| --- | --- | --- | --- |
| **Stable** | — | Not published yet | Not published yet |
| **Beta** | 0.0.18 | [Download](https://github.com/qpingcode/MyTools/releases/download/v0.0.18/MyTools-0.0.18-windows-x64-setup.exe) | [Download](https://github.com/qpingcode/MyTools/releases/download/v0.0.18/MyTools-0.0.18-windows-x64-portable.zip) |
<!-- mytools-downloads:end -->




















Stable is the recommended channel. Push a git tag named `release-YYYY-MM-DD` (for example `release-2026-08-21`) to publish a stable build. Every push to `main` publishes a beta. In Settings, set **Update channel** to `stable` or `beta`.

A portable package can be extracted and run as `MyTools.Desktop.exe`. Portable installations do not support in-app Velopack updates.

> Authenticode signing is not yet enabled. Windows SmartScreen may display an unknown publisher warning, so download MyTools only from this repository's Releases page.

## Contributing

Issues and pull requests are welcome. Before submitting a change, make sure the solution builds successfully and run the tests relevant to your changes.

## License

MyTools is licensed under the [MIT License](LICENSE).
