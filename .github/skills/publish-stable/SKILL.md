---
name: publish-stable
description: >-
  Publish a MyTools official stable release by tagging release-YYYY-MM-DD and
  pushing it so GitHub Actions builds the stable Velopack channel. Use when the
  user asks to ship a stable/formal/production release, cut a release tag, or
  promote the current main build to Stable.
---

# 发布 MyTools 正式稳定版

Stable 只由 **git tag** 触发，不要在 GitHub 网页上先建 Release。Beta 会在每次推送到 `main` / `master` 时自动发布，本 skill 只覆盖正式稳定版。

工作流：`.github/workflows/release.yml`。

## 1. 发布前确认

- 要发布的提交已经在 `main`（或 `master`）上，并且该提交的 CI 已通过。
- 工作区干净：没有未提交、未推送的改动。
- 不要用带 `[skip release]` 的提交当发布点（那种提交本来就不会打 Beta）。

默认把 **当前 `origin/main` 的 HEAD** 打成稳定版。用户指定了某个 commit / 已有 Beta 版本时，改打那个 SHA。

## 2. 打 tag 并推送

Tag 必须匹配：

```text
release-YYYY-MM-DD
release-YYYYMMDD
release-YYYY-MM-DD-N    # 同一天第二份及以后，N 从 2 起
```

推荐第一种。同一天已经发过 `release-2026-08-21` 时，下一份用 `release-2026-08-21-2`。

在仓库根目录执行（把日期换成今天，UTC+8）：

```powershell
git fetch origin
git checkout main
git pull --ff-only origin main
git tag release-2026-08-21
git push origin release-2026-08-21
```

只推这个 tag，不要 `git push --tags` 把本地其它 tag 一起推上去。

打到指定提交：

```powershell
git tag release-2026-08-21 <sha>
git push origin release-2026-08-21
```

## 3. CI 会做什么

推送成功后，`Build and release` 会：

1. 跑测试并打 Velopack **stable** 通道包
2. 用该 tag 创建 **Latest** GitHub Release（标题形如 `MyTools v1.2.3 (Stable)`）
3. 上传 Windows x64 完整安装包和 Portable
4. 回写 `README.md` / `README.zh-CN.md` 下载矩阵（提交信息带 `[skip release]`）

应用版本号仍是递增 SemVer（`1.x.y`），不是日期。日期只出现在 git tag 上。

## 4. 不要做的事

- 不要在 GitHub 网页上对同一个 tag 先创建 Release，工作流里的 `gh release create` 会失败。
- 不要用 `v1.2.3`、`stable`、`release` 这种 tag，工作流不会当稳定版。
- 不要改 `release.yml` 来“手动发一版”；稳定版入口就是 tag。
- 不要把未合并的功能分支直接打 `release-*` tag，除非用户明确要求发那个提交。

## 5. 验收

1. 打开 [Actions](https://github.com/qpingcode/MyTools/actions/workflows/release.yml)，确认这次 tag 的 run 成功。
2. 打开 Releases：该 tag 不是 prerelease，且被标为 Latest。
3. README 下载矩阵里 Stable 行已指向新的 setup / portable 链接。
4. 安装版设置里 **更新通道** 为 `stable`（旧值 `win` 会当成 `stable`）才能收到这版应用内更新。

失败时先看 workflow 日志。Tag 格式不对时，步骤 `Determine release channel` 会直接报错。需要重发时：删掉 GitHub Release 和远程 tag 后，用新的合法 tag 再推（同一天已用过的日期要加 `-2`）。
