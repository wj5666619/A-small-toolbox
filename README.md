# 小工具箱 · A Small Toolbox

> 一个集定时关机、拼图游戏、系统工具、游戏辅助于一体的 Windows 桌面工具箱。
> A Windows desktop toolbox combining scheduled shutdown, puzzle games, system utilities and game aids.

![Platform](https://img.shields.io/badge/Platform-Windows-blue)
![.NET](https://img.shields.io/badge/.NET-4.8-512BD4)
![License](https://img.shields.io/badge/License-GPL--3.0-orange)

## ✨ 功能一览 / Features

| 功能 | 说明 |
| --- | --- |
| ⏱️ **定时关机** | 倒计时 / 指定时刻 / 每天重复 / 每周指定 四种模式，关机、锁定两种动作；到点弹窗确认、任务日志、托盘常驻 |
| 🧩 **拼图小游戏** | 在线图库无上限，点「下一张」才联网下载；3x3 / 4x4 两档难度，拖动拼装，自动计时，退出自动清理缓存 |
| 🎮 **游戏作弊** | 单机游戏修改器管理器：风灵月影、小幸、CE 脚本、GCM 专属、社区合集；一键下载 / 解压 / 归档 / 自动更新（仅单机） |
| 🏆 **Steam 成就** | 一键打开 Steam 成就管理器，解锁 / 锁定成就、修改统计数据（需 Steam 运行并登录） |
| 🖥️ **一键重装系统** | 官方重置向导（保留文件或清空）、重启进 WinRE、打开恢复设置 |
| 🧹 **C 盘清理** | 均衡清理（白名单保护）与深度清理（全系统缓存扫描），开源引擎 Light-Help |
| 🎬 **视频下载** | 支持 B站 / 抖音 / 快手 / YouTube / 微博 / 腾讯 / 爱奇艺 / 优酷等 1000+ 平台，粘贴链接一键解析，可视化进度，FFmpeg 自动合并高清 |
| 🎵 **音乐下载** | 网易云曲库在线搜索、双击下载 MP3；支持粘贴网易云 / QQ 音乐链接 |
| 🚀 **电脑开荒** | 新机一站式：系统体检、Bing 壁纸、默认路径换盘、纯净软件安装（winget）、启动项管理、磁盘分析（绿色本地组件） |

## 下载
『来自123云盘用户的分享』小工具箱_安装程序.exe
链接：https://1840545247.share.123pan.cn/123pan/0LSCTd-uA21A


## 🎨 界面特性 / UI

- 黑色天蓝主题，液态玻璃（Acrylic）背景效果
- 中 / 英双语一键切换
- 圆角渐变按钮、进度环、侧栏渐变选中
- 托盘常驻：关闭窗口不中断运行中的任务


## 🚀 快速开始 / Quick Start

- 双击 `小工具箱_安装程序.exe` 按向导安装
- 或直接运行解压出的 `小工具箱.exe`（绿色单文件，首次使用某功能时自动部署对应组件）


## 📁 目录结构 / Structure

```
github_repo/
├── 定时关机.cs          # 主程序源码（WinForms，单文件，含全部功能页）
├── 安装程序.cs          # 安装器源码（向导式安装、卸载、快捷方式）
├── 定时关机.ico         # 应用图标
├── build.bat            # 一键构建脚本（需先放置组件包）
├── README.md
├── LICENSE
└── .gitignore
```

> 源码文件名为历史遗留的 `定时关机.cs`，程序产物为 `小工具箱.exe`。二进制、组件压缩包（`gcm_tool.zip` / `dnkh_tool.zip` / `video_tool.zip` / `ffmpeg_tool.zip` 等）与构建缓存均不入库，通过 Releases 附件分发。

## 🧩 第三方组件与许可 / Third-Party Components

| 组件 | 用途 | 许可 |
| --- | --- | --- |
| [Game-Cheats-Manager](https://github.com/dyang886/Game-Cheats-Manager) | 游戏作弊管理 | GPL-3.0 |
| [Steam Achievement Manager](https://github.com/gibbed/SteamAchievementManager) | Steam 成就管理 | GPL-3.0（社区分支） |
| [dnkhw](https://github.com/chenfengyimei/dnkh) | 电脑开荒（新机部署） | MIT |
| [Light-Help](https://github.com/leiting6/Light-Help) | C 盘清理引擎 | GPL-3.0 |
| [yt-dlp](https://github.com/yt-dlp/yt-dlp) | 视频下载引擎 | Unlicense |
| FFmpeg (gyan.dev builds) | 视频合并 / 转码 | GPL |
| 拼图在线图库 | 拼图图片源 | 图片版权归原作者，仅作个人娱乐使用 |

## ⚠️ 免责声明 / Disclaimer

- 游戏作弊功能**严格仅限单机游戏**，请勿用于任何联机 / 竞技环境，后果自负
- 定时关机、一键重装等电源操作前请保存工作进度
- 本软件按「AS IS」提供，不附带任何担保（详见 LICENSE）

## 📄 许可证 / License

[GPL-3.0](LICENSE) © 2026 wj5666619
