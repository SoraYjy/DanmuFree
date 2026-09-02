# DanmuFree

![License](https://img.shields.io/badge/license-MIT-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![Platform](https://img.shields.io/badge/platform-Windows-0078D6?logo=windows11&logoColor=white)
![直播](https://img.shields.io/badge/直播-B站·抖音-FB7299)

> **作者**：**Sora**　·　B站 ID：**SoraYjy**

B站 / 抖音直播间弹幕桌面客户端（Windows）。把弹幕 / 礼物 / 进场关注 / 统计展示在可悬浮、半透明的桌面窗口里；悬浮模式**真·鼠标穿透**，打游戏开着也不挡操作。协议层完全自写（零第三方直播库）。

![DanmuFree 截图](assets/screenshot.png)

## 功能地图

| 模块 | 说明 |
|---|---|
| **双平台** | B站（扫码登录）+ 抖音（匿名免登录），共用一套展示管道 |
| **弹幕窗** | 纯聊天弹幕列表；粉丝勋章 / 时间戳；用户名·正文分开字体颜色；消息可设 N 秒后自动消失；千条虚拟化 + 高弹幕量滚动合并不丢不卡 |
| **通知窗（独立）** | 进场 / 关注 / 礼物·舰长 / SC，四类分开关闭，金色加粗，可显时间戳、可设自动消失，不混进聊天流 |
| **统计** | 在线 / 看过 / 点赞 常驻顶部（B站 60s 轮询；抖音 WS 实时推送） |
| **悬浮沉浸** | 真·鼠标穿透（`WS_EX_TRANSPARENT`），全屏 / 无边框游戏不挡操作、不抢焦点；悬浮态抗 Win+D（被最小化会自动恢复） |
| **弹幕朗读** | 三引擎：**Edge 在线**（Azure 神经音，零配置·默认·14 中文音色）+ GPT-SoVITS 克隆 + 系统内置；礼物连送聚合不刷屏 |
| **定向回复** | 弹幕命中关键词 → 不读原文，改念固定回复或播放音效（wav/mp3）；多条规则从上往下、首条命中即停；总开关一键启停，每条可勾选临时禁用 |
| **持久化** | cookie / 窗口位置·大小 / 悬浮态 自动存盘，重开还原 |

> 弹幕聊天与礼物 / SC / 进场关注分两路：聊天在弹幕窗，事件在通知窗。

## 怎么用

### ① 下载即用（普通用户，推荐）

1. 到 [Releases](https://github.com/SoraYjy/DanmuFree/releases/latest) 下 **`DanmuFree-vX.Y.Z-win-x64.zip`**（~260MB，自带运行环境）
2. 解压到任意目录
3. 双击 **`DanmuFree.exe`** —— 无需装 .NET / Node

### ② 从源码（开发者）

需要 **.NET 8 SDK**；用抖音签名还需本机 **Node.js**（PATH 里有 `node`）。
```bash
dotnet run --project src/DanmuFree.App                                   # debug 运行
# 或双击构建产物：src/DanmuFree.App/bin/Debug/net8.0-windows/DanmuFree.exe
./pack.sh                          # Git Bash 里打包绿色版 → dist/DanmuFree/
```
> Windows 的 **PowerShell / cmd 用 `.\pack.cmd`**（= pack.sh 的入口，别用 `.\pack.sh`——那会走 `.sh` 文件关联弹个 git-bash 窗口又秒关、看不到输出）。

### 登录 & 连接

- **B站**：控制面板 →「扫码登录」用手机 app 扫码（cookie 自动存，重开免重扫）→ 输房间号（短号即可）→「连接」
- **抖音**：免登录，顶部切到「抖音」→ 输 `live.douyin.com/` 后那串房号 →「连接」（首次约 2s，签名开销）

### 日常操作

- **调样式**：弹幕显示后，控制面板调字体 / 颜色 / 透明度 / 字号（实时生效）
- **打游戏**：「弹幕」TAB 勾「**悬浮**」→ 弹幕窗置顶 + 鼠标穿透；打完取消勾选退出
- **读弹幕**：见下「弹幕朗读如何接入」

## 配置一览

控制面板分四个 TAB：

| TAB | 可配置项 |
|---|---|
| **弹幕** | 置顶 · 背景透明度 · 字号(8~100) · 字体 · **文字描边(颜色盘选色/粗细)** · 历史条数 · 保留秒数(0=永久) · 显示账号/勋章/时间/统计条(在线·看过·赞) · 用户名/弹幕字体颜色 · **弹幕窗悬浮** |
| **进场/关注** | 接收进场 · 接收关注 · 显示通知窗 · 显示时间 · 保留秒数(0=永久) · 字号(8~100)/字体/透明度 · **文字描边** · **通知窗悬浮** |
| **朗读** | 开关 · 引擎(Edge在线/GPT-SoVITS/系统) · 音色 · 读哪些(弹幕·SC·礼物·用户名) · 语速 · 语气(采样温度) · 音量 · 屏蔽词 · 测试 |
| **定向回复** | **总开关** · 规则列表（加/删/排序/试听）· 关键词 · 回复方式（念文字 / 播音频）· 音频文件选择 |

配置 / 日志在 `%AppData%\DanmuFree\`（`settings.json`、`log.txt`）。

## 弹幕朗读如何接入

朗读读 **聊天 / SC / 礼物**，不读进场关注；朗读开关独立于显示（可"看着不读" / "不显示但读"）。三档引擎按需选：

**① Edge 在线（推荐 · 零配置）**：朗读 TAB 默认即此项 → 勾「启用」→「测试朗读」。调 Edge 浏览器的 Azure 神经音色（**14 个中文音色**：普通话 / 粤语 / 台湾 / 东北·陕西方言，下拉显示中文名），**免部署、免 key、免参考音频**，音质远胜系统内置、与 GPT-SoVITS 中性朗读持平。需联网（直播本就联网）；非官方端点，偶发失效时切系统内置兜底。

**② 系统内置（离线兜底）**：朗读 TAB → 选「系统内置」。用 Windows 自带中文语音（Huihui），零配置、离线可用，音色偏机械。

**③ GPT-SoVITS（要克隆音色才需要）**

GPT-SoVITS 是**独立的第三方音色克隆服务**，不在本项目内——部署、环境、模型、API 启动方式见其仓库：<https://github.com/RVC-Boss/GPT-SoVITS>（默认端口 9880）。

API 起好后，在 DanmuFree「朗读」TAB 选 **GPT-SoVITS**，填 **服务地址 + 3~10 秒参考音频 + 参考文本（音频里说的字）+ 语言** 即可。能克隆主播自己的声音，是它独有的卖点。

> 📊 **本机部署开销（实测）**：常驻内存约 **3.5 GB**；GPU 推理**官方主推 N 卡（CUDA）、开箱即用**（A 卡仅 Linux 下 ROCm 实验性、Windows 基本跑不通；Mac 走 MPS）；CPU 空闲时几乎不占、仅合成语音时瞬时拉高。无独显也能纯 CPU 跑（慢、更吃内存）。

**可选**：念用户名开关（开=「xx 说，…」，关=只读正文；礼物/SC 恒带用户名不受影响）· SC 念「xx 送了 N 元的 SC，内容」 · 语气滑块（采样温度，高丰富低平稳）· 礼物连送在 1200ms 内合并成「xx 送了 N 个 yy」。

**定向回复**（「朗读」旁独立 TAB）：弹幕命中关键词 → **不读这条弹幕**，改念一句你指定的话，或播放一段音效（wav/mp3）。多条规则**从上往下匹配、前面命中就不再看后面**；TAB 顶部有**总开关**，一键停用全部规则（配置保留）；每条规则前有勾选框，**不勾 = 临时禁用**（配置保留，随时勾回）。独立于「读弹幕」开关——可以关掉普通弹幕朗读、只播定向回复。典型用法：常问的问题（怎么加好友 / 几点下播）配一句固定答复，口号 / 梗配一段音效。支持加 / 删 / 上下移 / 试听，随设置自动保存。

## 致谢

- 抖音签名 `sign/sign.js` 是 webmssdk 的反混淆产物，跟随 [saermart/DouyinLiveWebFetcher](https://github.com/saermart/DouyinLiveWebFetcher) 维护。
- 朗读：[GPT-SoVITS](https://github.com/RVC-Boss/GPT-SoVITS)、Windows SAPI / [System.Speech](https://learn.microsoft.com/dotnet/api/system.speech)；播放 [NAudio](https://github.com/naudio/NAudio)。
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)、[QRCoder](https://github.com/codebude/QRCoder)、Microsoft .NET / WPF。

## 协议

[**MIT**](LICENSE)，随便用、随便改。

> ⚠️ `sign/sign.js` 版权归字节跳动，属第三方反混淆产物；使用须遵循抖音服务条款，本项目仅作技术学习与个人自用。B站 / 抖音名称、Logo 及接口归各自平台所有。

---

> 📌 开发细节 / 协议实现 / 已知限制 / 路线图见 [CLAUDE.md](CLAUDE.md)。
