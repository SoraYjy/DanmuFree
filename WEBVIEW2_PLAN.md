# WebView2 去 Node — 实施 Plan（抖音签名）

> 状态：**待做**。这是 exe 分发「两步走」的**第二步**（第一步绿色文件夹版已完成，见 `pack.sh` / master `009f91f`）。
> 本文件自包含，之后随时可从这里开工。背景协议见 `CLAUDE.md`「抖音 WS 协议」节 + `DOUYIN_PLAN.md`。

---

## 0. 目标
把抖音 X-Bogus 签名从 **node + jsdom 子进程** 切到 **系统 WebView2**（真 Chromium）：
- **去掉 node.exe(80MB) + jsdom(25MB)** → 分发体积从 ~260MB 降到 ~155MB，接近单 exe。
- **用户零安装**：WebView2 runtime 系统自带（Win11 默认有、Win10 1809+ 大多有），不用装 Node。
- **抗算法变更**：跑的是**官方 webmssdk**（真浏览器环境），反调试必过（就是浏览器本身）；算法变了不用逆向，跟官方一致。
- 本质：商业弹幕软件（Calabash 嵌整个 Chromium）的**轻量版**——借系统 WebView2 代替打包浏览器。

## 1. 现状（要替换的）
- `App/Services/DouyinSigner.cs`：`IDouyinSigner` 实现，`Process.Start(node, sign/sign_runner.js <md5>)` 拿 X-Bogus。
- `App/sign/`：`sign.js`(webmssdk 1.0.0.53 + **jsdom 补环境**) + `sign_runner.js` + `node_modules/jsdom`(~25MB)。
- `pack.sh` 打包时复制 `node/node.exe`(80MB) 进 dist。
- `Core/Protocol/DouyinSign.cs`：纯 C# 算签名素材（13 字段 param→md5=X-MS-STUB）+ `IDouyinSigner` 抽象。**这层不动**。

## 2. 方案：隐藏 WebView2 跑官方 webmssdk
**原理**：创建一个隐藏的 WebView2（系统 Chromium 内核），加载官方 `webmssdk.es5.js`，JS 侧提供 `sign(md5) → X-Bogus`，C# 经 `ExecuteScriptAsync` 调用拿结果。真浏览器里 webmssdk 反调试天然通过（跟抖音网页一模一样），**不再需要 jsdom 补环境**。

### 2.1 WebView2 runtime
- Evergreen：Win11 自带；Win10 1809+ 大多自带（随 Edge / Office 更新）。`Microsoft.Web.WebView2` NuGet 在 runtime 缺失时会抛清晰异常或触发安装。
- 缺失兜底（实施时定）：① 提示用户装 Evergreen runtime（~20MB，一次性）；② 或随包带 Fixed runtime（大，~150MB，不推荐）；③ 或检测到无 runtime 时**回落到 node 方案**（保留 sign/ 作 fallback，渐进降级）。

### 2.2 webmssdk 加载 + 调用接口（**实施时需验证的关键点**）
两种加载方式，二选一（先试 A）：
- **A（推荐，最干净）**：WebView2 加载本地 `sign/webmssdk.es5.js`（**官方原版**，从抖音页面 preload 抓：`//lf-c-flwb.bytetos.com/.../c-webmssdk/1.0.0.53/webmssdk.es5.js`），再注入一小段 wrapper 暴露签名函数。
- **B（保底）**：直接跑现有 `sign.js`，**去掉顶部的 jsdom 补环境段**（浏览器原生有 `window/document/navigator`），保留 `getSign` 逻辑原样。
- 接口：saermart 逆向出 webmssdk 导出 `window._0x5c2014(param)` 返回 `{X-Bogus}`（`sign.js` 的 `getSign` 即调它）。wrapper：
  ```js
  function sign(md5){ return window._0x5c2014({'X-MS-STUB': md5})['X-Bogus']; }
  ```
- **未验证**：官方 webmssdk 升版后导出函数名可能变（混淆）。**实施第一步先在 Edge devtools 里加载 webmssdk.es5.js，确认 `_0x5c2014` / 等价签名入口仍可用**；若变了，跟 saermart/DouyinLiveWebFetcher（gitee `iuact` 镜像）拿新接口。

## 3. 实施步骤
1. **加 NuGet**：`Microsoft.Web.WebView2`（WPF 版，带 native dll，publish 自包含会带上）。
2. **签名服务**：`App/Services/WebView2Signer.cs` 实现 `IDouyinSigner`：
   - 持一个隐藏 `WebView2`（或 `CoreWebView2Environment` 无控件创建），app 启动时 `EnsureCoreWebView2Async` 一次，**常驻复用**（签名低频，不必每次重建）。
   - 初始化时加载 webmssdk + 注入 `sign(md5)` wrapper（导航到 `about:blank` 后 `ExecuteScriptAsync` 注入本地 js 文件内容）。
   - `SignAsync(md5)`：`await Application.Current.Dispatcher.InvokeAsync(() => webView.CoreWebView2.ExecuteScriptAsync($"sign({JsonSerializer.Serialize(md5)})"))`；`ExecuteScriptAsync` 返回 JSON 串（带引号），反序列化拿 X-Bogus。
3. **VM 切换**：`DanmuViewModel` 把 `_douyinSigner` 从 `DouyinSigner` 换成 `WebView2Signer`（其余不动；`DouyinDanmuClient` 经 `IDouyinSigner` 无感）。
4. **清理 node 痕迹**：
   - 删 `App/sign/`（sign.js / sign_runner.js / node_modules / package.json）。
   - 删 `DouyinSigner.cs`（或保留作"无 WebView2 时的 fallback"，实施时定）。
   - `pack.sh` 去掉复制 `node/node.exe` 的步骤。
   - csproj 去掉 `Content Include sign\**`，加 WebView2 引用。
   - `App.xaml.cs` 的启动 node 探测改成 WebView2 可用性探测（或删）。
5. **publish 验证**：`pack.sh` 重打，确认 dist 不再有 sign/ + node/，体积降到 ~155MB。

## 4. 线程 / 生命周期注意
- WebView2 必须在 **UI 线程（STA）** 创建和调用。`DanmuViewModel.ConnectAsync` 在后台线程跑（await 链），签名调用必须 `Dispatcher.InvokeAsync` 切回 UI 线程——`IDouyinSigner.SignAsync` 本就是 async，套一层 Dispatcher 即可。
- WebView 常驻：app 启动创建、app 退出销毁。重连复用同一个实例（签名每次连接算一次，频率低）。
- 初始化耗时：首次 `EnsureCoreWebView2Async` ~几百 ms（cold）。可 app 启动后台预热，或首次连接时承担一次延迟。

## 5. 风险与权衡
| 风险 | 说明 | 对策 |
|---|---|---|
| WebView2 runtime 缺失 | 极少数老 Win10 没装 | 检测 + 友好提示装 Evergreen；或回落 node 方案 |
| webmssdk 接口名变 | 升版后 `_0x5c2014` 可能改名 | devtools 实测确认；跟 saermart 更新 wrapper |
| 签名异步化 | WebView2 必须 UI 线程 | `SignAsync` + Dispatcher，`ConnectAsync` 已 await，可适配 |
| WebView 常驻内存 | 隐藏 Chromium 占 ~几十 MB | 可接受（签名低频）；或签名完释放、下次重建（牺牲延迟） |
| 调试比 node 难 | WebView2 异步 + JS interop | 先在 Edge devtools 跑通纯 JS，再移植 |

## 6. 验证策略
1. **签名正确性**：连真实活跃房（如 `906591878276`），WS 握手 101 = 签名通过；200 + `auth failed` = 签名错。
2. **对照 node 版**：同 room_id+uid+md5，WebView2 签出的 X-Bogus 与 node 版**应一致**（同一 webmssdk 同一输入）；若不一致先排查 wrapper/接口。（注：X-Bogus 可能含时变因子，对照时固定输入。）
3. **端到端**：抖音连活跃房，弹幕/进场/在线/看过都来（复用现有冒烟路径 `probe/DouyinSmoke`，把 NodeSigner 换成调 app 的 WebView2Signer，或直接 app 里连）。
4. **体积**：`pack.sh` 产出 dist 对比，确认 sign/+node/ 消失。
5. **runtime 缺失降级**：在无 WebView2 的环境（或模拟）启动，确认提示友好、不崩、B站仍可用。

## 7. 平滑 / 回滚
- `IDouyinSigner` 抽象已就位：新旧签名器可**并存**（保留 `DouyinSigner` 作 fallback）。实施时可做成「优先 WebView2，缺失则回落 node」，零风险过渡；稳定后再删 node。
- 在独立分支 `feat/douyin-webview2` 做，验证通过 ff-merge。

## 8. 产物（完成后）
- `App/Services/WebView2Signer.cs`（新）、`sign/webmssdk.es5.js`（官方原版，~400KB，替代 saermart sign.js+jsdom）。
- 删：`App/sign/{sign.js, sign_runner.js, package.json, node_modules/}`、`DouyinSigner.cs`（或留 fallback）、`pack.sh` 的 node 复制段。
- dist：`DanmuFree.exe`（原生 dll 已折进 exe）+ WebView2 native + `sign/webmssdk.es5.js` ≈ **~155MB**（去掉 80MB node + 25MB jsdom）。
- 依赖：系统 WebView2 runtime（不再依赖用户装 Node）。
