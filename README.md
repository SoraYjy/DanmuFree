# DanmuFree

![License](https://img.shields.io/badge/license-MIT-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![Platform](https://img.shields.io/badge/platform-Windows-0078D6?logo=windows11&logoColor=white)
![直播](https://img.shields.io/badge/直播-B站·抖音-FB7299)

> **作者**：**Sora**　·　B站 ID：**SoraYjy**

一个 **B站 / 抖音直播间弹幕桌面客户端**（Windows / WPF / .NET 8）。自用替代品：连一个直播间，把弹幕 / 礼物 / SC / 进场关注 / 统计展示在干净、可悬浮、半透明的桌面窗口里；悬浮模式**真正鼠标穿透**，打游戏（全屏 / 无边框 FPS）开着也不挡操作、不抢焦点。

> 自用；协议层完全自写（零第三方直播库）——B站走自写 protobuf/JSON，抖音走自写 protobuf + node 跑官方 webmssdk 签名，避免依赖腐烂。

## 效果展示

> 📷 截图待补：把一张「悬浮弹幕窗 + 进场通知窗 + 控制面板」的截图存为 `assets/screenshot.png`，然后把本段替换成 `![DanmuFree 截图](assets/screenshot.png)` 即可显示。

## 功能

### 弹幕展示（弹幕窗）
- 单房间连接（输短号，自动解析真实 room_id）。
- 列表式展示**纯聊天弹幕**（白色文本）。礼物 / SC / 进场 / 关注 都在下方独立的「进场/关注/礼物/SC」窗，不混进聊天流。
- **发送者粉丝勋章**：带勋章的弹幕用户名前显示「勋章名·等级」（金色），可开关。
- **时间戳**：每条弹幕前显示 `HH:mm:ss`，可在设置开关。
- **用户名 / 弹幕 分开字体 + 颜色**：各自独立可配（字体下拉 + 颜色输入 + 实时色块预览）。
- 历史留存最近 N 条（默认 1000），自动滚到最新。
- **虚拟化渲染**：弹幕上千条时仍流畅。

### 进场 / 关注 / 礼物 / SC（独立通知窗）
- **单独一个窗口**，和弹幕窗一样可独立拖动、调节大小、独立字体 / 字号 / 透明度 / 沉浸。
- **进场、关注、礼物、SC 分开开关**：只收其中任意几类，或全收（都关=都不显示）。
- **礼物 / SC 显示在这里**：礼物与进场/关注同属「事件流」，金色加粗显示（如「辣条 x1」）；SC 醒目留言也在这里（昵称 + 留言 + 金色「¥价格」）。主弹幕窗只剩纯聊天，避免事件被刷屏淹没。
- 整个窗口也可一键隐藏（消息后台仍收，随时再开）。
- 解析 B站现行的 `INTERACT_WORD_V2`（交互信息编码在 `data.pb` protobuf 里，自写解码）。

### 直播间统计
- **在线 / 看过 / 点赞**：弹幕窗顶部常驻一排显示，连接后每 60s 轮询真实值，大数自动「万」化。

### 弹幕朗读（TTS，双引擎）
- 收到弹幕合成语音读出来：**读聊天 / SC / 礼物，不读进场关注**；朗读开关独立于显示开关（可"看着但不读"/"不显示但读"）。
- **两种语音引擎可选**（「朗读」TAB 切换，立即生效）：
  - **GPT-SoVITS（音色克隆）**：音质最像主播/自定义音色，需本机起 GPT-SoVITS 服务 + 3~10 秒参考音频。
  - **系统内置（免参考音频）**：零配置，用 Windows 自带语音（如 Microsoft Huihui 中文）。无脑入门，随开随用。
- **可选是否念用户名**：开则「xx 说，…」，关则只读正文（礼物恒带用户名，不受此开关影响）。
- **礼物朗读聚合**：连送同礼物在 1200ms 内合并成一句「xx 送了 N 个 yy」，不再刷屏读 100 次。
- 串行播放（一条播完再下一条）+ DropOldest 节流：弹幕洪水时自动丢旧紧跟新，不会越积越滞后。

### 连接
- **Cookie 优先、匿名兜底**。
- **应用内扫码登录**：显示二维码，手机 B站 app 扫码确认，自动从 `Set-Cookie` 响应头提取 cookie（SESSDATA / DedeUserID / bili_jct）+ 补 buvid3 并存盘。**关软件重开不用重扫**（cookie 持久化）。控制面板的 Cookie 框**只读**（扫码自动填入，不可手改）。
- 断线指数退避重连；单帧解析失败不影响后续。

### 抖音直播（第二平台）
- 控制面板顶部 **B站 / 抖音 单选**切换；同一套展示管道（弹幕窗 / 进场关注窗 / 统计），切平台即换数据源。
- **匿名连接**（无需登录）：输入 `live.douyin.com/` 后那串房间号，自动解析真实 room_id + ttwid。
- **弹幕 / 进场 / 关注 / 礼物** 都来；**在线 / 看过** 走 WS 推送（`WebcastRoomUserSeqMessage` + `WebcastRoomStatsMessage`，近实时，不轮询）。弹幕带**用户等级**（抖音等级是图标，数字嵌在图片 URL 里，解析后显示「Lv.N」）。
- 注：抖音 WS **不推累计点赞总数**（只推增量点赞），故抖音「赞」恒显示「-」；「看过」要等房间推送 `RoomUserSeqMessage` 才有（部分挂机房不推）。
- **签名依赖本机 node**：抖音 WS 握手要 X-Bogus，算法是 webmssdk 的 `getSign`（反调试 JS，Jint 跑不动），故用 node 子进程跑 `sign/sign.js`（webmssdk 1.0.0.53 + jsdom）。首次连接约 2s（node 启动 + jsdom 加载）。无 node 时仅抖音不可用，B站不受影响。

### 窗口（控制面板常驻 + 弹幕窗 / 通知窗纯展示，共享同一 ViewModel）
- **控制面板（常驻）**：随主窗启动、独立任务栏项、`—` 最小化（从任务栏恢复）、`×` 退出程序。**可拖右下角调整大小**（设置多时拉大即可看全，位置/大小自动记忆）。包含 房间号 / 连接 / 断开 / 状态 / 统计 / 账号 UID / 扫码登录 / 全部设置。设置分两个 TAB：
  - **弹幕** TAB：置顶、背景透明度、字号、字体、历史条数、显示账号 / 勋章 / 时间、用户名 / 弹幕字体颜色、**弹幕窗悬浮**。
  - **进场/关注** TAB：接收进场 / 接收关注、显示通知窗、字号 / 字体 / 透明度、**通知窗悬浮**。
  - **朗读** TAB：开关 / **引擎选择（GPT-SoVITS 克隆 · 系统内置免参考音频）** / 系统音色下拉 / 读哪些（弹幕·SC·礼物·用户名）/ 语速 / **语气（GPT-SoVITS 采样温度，高丰富低平稳）** / 音量 / 屏蔽词 / 测试按钮。
- **弹幕窗 / 通知窗（纯展示）**：无标题栏按钮，整体半透明（背景透、文字不透）、置顶、按住空白拖动、右下角拖拽缩放。
- **悬浮（沉浸）= 真正鼠标穿透**：勾选对应 TAB 的「悬浮」后，该窗口自动置顶 + 背景更透 + **OS 级完全穿透**（`WS_EX_TRANSPARENT`）——鼠标点击直接落到下层窗口，**打游戏时点中弹幕窗也不会让游戏失焦**。悬浮期间窗口不可点，需移动/缩放时在控制面板取消悬浮、调好再勾上。
- **位置 / 大小记忆**：弹幕窗、通知窗的位置和大小自动存盘，下次启动还原到原位（拔显示器会自动夹回屏内）。

## 使用方法

### 运行
需要 **.NET 8 SDK**；用抖音还需本机装 **Node.js**（签名用，PATH 里有 `node` 即可）。
```bash
dotnet run --project src/DanmuFree.App
```
或双击 exe：
```
src/DanmuFree.App/bin/Debug/net8.0-windows/DanmuFree.exe
```

### 打包分发（绿色文件夹，给别人用）
`pack.sh` 一键打出**自包含**版本（用户**无需装 .NET / Node**，解压即用）：
```bash
./pack.sh                 # 自动找本机 node.exe
./pack.sh /path/node.exe  # 或指定 node.exe（从 nodejs.org 下 Windows Binary）
```
产出 `dist/DanmuFree/`（~260MB）：`DanmuFree.exe`（**单文件**，.NET runtime + 原生 dll 全折进 exe）+ `sign/`（抖音签名，含 jsdom）+ `node/node.exe`。把整个文件夹打 zip 发给别人，解压双击 exe 即用。
> 体积大是因为自带 .NET runtime + node.exe + jsdom。**第二步会用 WebView2 替换 Node**（系统自带，去掉 80MB node.exe + 25MB jsdom），届时更接近单 exe。

### 首次使用
- **B站**：启动后控制面板已常驻打开 → 点「扫码登录」用手机 B站 app 扫码（cookie 自动保存）→ 输入房间号（短号即可）→「连接」。
- **抖音**：控制面板顶部切到「抖音」→ 输入房间号（`live.douyin.com/` 后那串，匿名免登录）→「连接」。首次连接约 2s（签名开销；绿色分发版自带 node，无需另装）。
- 弹幕显示后，在设置里调字体 / 颜色 / 透明度 / 字号（实时生效）。
- 要打游戏：在「弹幕」TAB 勾「**悬浮**」→ 弹幕窗置顶 + 鼠标穿透，开着游戏也不挡操作；打完取消勾选退出悬浮。
- 进场 / 关注：在「进场/关注」TAB 开关接收、显示窗口、悬浮。
- **弹幕朗读**：最快上手——「朗读」TAB 选**系统内置**引擎 → 直接勾「启用」→「测试朗读」（免参考音频，用 Windows 自带中文语音 Huihui）。要更逼真音色再切 **GPT-SoVITS**：本机启动 api（`python api_v2.py -a 127.0.0.1 -p 9880 -c GPT_SoVITS/configs/tts_infer.yaml`），填 **3~10 秒参考音频** + 参考文本。含英文弹幕还需 GPT-SoVITS 装 NLTK（见下「已知限制」）。可选是否念用户名（「xx 说，…」）。

配置 / 日志在 `%AppData%\DanmuFree\`（`settings.json`、`log.txt`）。

## 测试
```bash
dotnet test    # Core 层：B站协议编解码 / INTERACT_WORD_V2 pb 解码 / 房间解析 / 统计 / 扫码登录；抖音 protobuf 解码 / 签名素材 / 房间解析；TTS 文本预处理 / 礼物聚合 / GPT-SoVITS 客户端（共 108 单测）
```

## 已知限制 / 风险
- **B站协议会变**（已踩过：`getRoomInfoOld` 弃用、op3 在线失真、登录 cookie 改走 `Set-Cookie`、`INTERACT_WORD_V2` 把数据塞进 `data.pb` protobuf）。协议层自写 + 单测（含真实抓包样本回归），坏了改一处即可。
- **抖音签名是最大维护点**：X-Bogus 算法随 webmssdk 版本变（_signature → X-Bogus → a_bogus）。用 node 跑官方 JS 换来的红利：算法变了不用重新逆向，更新 `sign/sign.js`（跟 saermart/DouyinLiveWebFetcher，GitHub 被墙走 gitee `iuact` 镜像）即可。webmssdk 反调试只认 jsdom 这种真 DOM 环境（Jint 撞 >32000 层递归跑不动）。
- **抖音依赖 node + jsdom**（`sign/`，~25MB）：违背纯单 exe 轻量，但自用机已有 node 可接受；分发需随 exe 带 `sign/`（含 `node_modules/jsdom`）。
- **账号昵称 / 等级**：B站风控接口（nav / space）限制，暂只显示从 cookie 解析的 UID。
- **匿名连接**易被风控，B站建议扫码登录带 cookie；抖音目前匿名可用。
- **弹幕朗读（GPT-SoVITS）实测坑**（自写 GET 客户端，字段以实测为准）：① `/tts` **必须 GET + query 参数**（POST JSON 被服务拒 `parsing the body`）；② 语速字段是 **`speed_factor`**（非 `speed`）；③ `ref_audio_path` **必填**（V2 无默认音色）且参考音频必须 **3~10 秒**（超范围被拒）；④ 弹幕含英文（被切出独立片段）触发英文分词，需 GPT-SoVITS 装 NLTK `averaged_perceptron_tagger_eng`（整合包默认没下、缺则 400；修复：在其 venv `python -c "import nltk; nltk.download('averaged_perceptron_tagger_eng'); nltk.download('punkt')"`）。朗读失败详情写 `%AppData%\DanmuFree\log.txt`。
- **系统内置 TTS（SAPI）音色取决于系统已装的语音包**：Win 中文版自带 Microsoft Huihui（zh-CN，实测可用）；英文版可能无中文语音（读中文会变调/不读），可在「设置 → 时间和语言 → 语音」装中文语音包。系统引擎仅作零配置入门，要音色逼真请用 GPT-SoVITS。

## 路线图

### ✅ 已完成
- **绿色文件夹分发版**（`pack.sh`）：self-contained 单文件 exe + 打包 `node.exe`，用户解压即用（**无需装 .NET / Node**，~260MB）。
- **抖音直播**：第二数据源，弹幕/进场/关注/礼物/在线/看过/用户等级（详见上文）。
- **弹幕朗读（TTS）**：独立第三管道（读聊天/SC/礼物，不读进场关注；朗读开关独立于显示）。**双引擎**——GPT-SoVITS（音色克隆）+ 系统内置 SAPI（免参考音频零配置）；可选是否念用户名；**礼物连送聚合**（恒带用户名，不刷屏）。

### ⏳ 下一步（已写 plan，随时可开工）
- **WebView2 去 Node**（详见 [`WEBVIEW2_PLAN.md`](WEBVIEW2_PLAN.md)）：用系统 WebView2（真 Chromium）跑官方 webmssdk 签名，去掉 `node.exe` + jsdom —— 省 ~105MB、接近单 exe、抗签名算法变更。

### 候选（未排期）
- **多房间**：同时连多个直播间。

### 已讨论 · 暂缓 / 做不了
- **B站 实时统计推送**（`ONLINE_RANK_COUNT` 等替代 60s 轮询）：暂不需要。
- **抖音累计点赞总数**：平台 WS 只推增量点赞、不推总数，拿不到。
- **真实在线人数**（B站/抖音）：平台均不对外提供，当前「在线」是人气值口径。

## 致谢

- 抖音签名 `sign/sign.js` 是 webmssdk 的反混淆产物，跟随 [saermart/DouyinLiveWebFetcher](https://github.com/saermart/DouyinLiveWebFetcher) 维护（GitHub 被墙走 gitee `iuact` 镜像）。
- 朗读引擎：[GPT-SoVITS](https://github.com/RVC-Boss/GPT-SoVITS)（音色克隆）、Windows SAPI / [System.Speech](https://learn.microsoft.com/dotnet/api/system.speech)（系统内置）；播放：[NAudio](https://github.com/naudio/NAudio)。
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)、[QRCoder](https://github.com/codebude/QRCoder)、Microsoft .NET / WPF。

## 协议

本项目基于 [**MIT**](LICENSE) 协议开源，随便用、随便改。

> ⚠️ `sign/sign.js`版权归字节跳动，属第三方反混淆产物；其使用须遵循抖音相关服务条款，本项目仅作技术学习与个人自用，不提供任何直播内容。B站 / 抖音的名称、Logo 及相关接口归各自平台所有。
