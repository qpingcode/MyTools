# MyTools

[English](README.md) | [简体中文](README.zh-CN.md)

[![许可证：MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![构建和发布](https://github.com/qpingcode/MyTools/actions/workflows/release.yml/badge.svg)](https://github.com/qpingcode/MyTools/actions/workflows/release.yml)

MyTools 是一款使用 .NET 8 和 WPF 开发的 Windows 桌面效率工具。它提供快速全局搜索、系统托盘集成、开机启动选项、剪贴板工具以及可扩展的插件运行时。

## 功能

- 使用可配置的全局快捷键，随时打开实时搜索。
- 在系统托盘中运行，并提供单实例保护。
- 配置开机启动、搜索快捷键和常规应用设置。
- 使用文件、命令、搜索引擎、进程、书签、计算器、UUID、JSON 和 XML 等内置工具。
- 加载和搜索可扩展的 Node.js 与 Web 插件。
- 通过 Velopack 安装和更新应用程序。
- 将配置、数据库、插件和 WebView2 数据保存在安装目录之外。

## 系统要求

- Windows 10 或 Windows 11，x64。
- 官方发布包为 self-contained，无需单独安装 .NET Desktop Runtime 或 Node.js。
- 从源码构建需要安装 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

## 安装

MyTools 提供两个发布通道：

<!-- mytools-downloads:start -->
| 通道 | 版本 | 完整安装包 | 便携版 |
| --- | --- | --- | --- |
| **Stable** | — | 尚未发布 | 尚未发布 |
| **Beta** | 0.0.19 | [下载](https://github.com/qpingcode/MyTools/releases/download/v0.0.19/MyTools-0.0.19-windows-x64-setup.exe) | [下载](https://github.com/qpingcode/MyTools/releases/download/v0.0.19/MyTools-0.0.19-windows-x64-portable.zip) |
<!-- mytools-downloads:end -->





















Stable 是推荐通道。推送名为 `release-YYYY-MM-DD` 的 git tag（例如 `release-2026-08-21`）会发布稳定版。每次推送到 `main` 都会发布 Beta。在设置中将 **更新通道** 设为 `stable` 或 `beta`。

便携包解压后直接运行 `MyTools.Desktop.exe` 即可。便携版不支持应用内 Velopack 自动更新。

> Authenticode 签名尚未启用。Windows SmartScreen 可能显示“未知发布者”警告，请仅从本仓库的 Releases 页面下载 MyTools。

## 贡献

欢迎提交 Issue 和 Pull Request。提交改动前，请确保解决方案能够成功构建，并运行与改动相关的测试。

## 许可证

MyTools 使用 [MIT License](LICENSE) 授权。
