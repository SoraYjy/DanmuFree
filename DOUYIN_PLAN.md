# 抖音直播接入 DanmuFree — 实施 Plan

> 状态：探针（`probe/DouyinProbe` + `probe/probe_node`）已**实测通过**（热门房 256438100956 收到弹幕文本+昵称/进场/关注/统计）。本文件是产品化实施的蓝图，**自包含**所有验证过的协议细节。compact 后照此实施。

---

## 0. 目标
把抖音直播弹幕作为**第二数据源**接入 DanmuFree，复用现有 `Core→Channel<RichMessage>→UiBatchPump→三窗` 管道。用户在控制面板切换 B站/抖音，输入对应房间号即可。B站逻辑不动。

## 1. 架构
- **复用**：`RichMessage` / `MessageType` / `Channel` 管道 / `UiBatchPump` / `DanmuWindow`+`NotifyWindow`+`ControlWindow` / 设置持久化。
- **新增抖音数据源**：`DouyinDanmuClient`（Core）产出 `RichMessage` 喂进同一 `Messages`/`NotifyMessages`。
- **VM 按平台选 client**：`Platform=Bilibili`→`BilibiliDanmuClient`；`Platform=Douyin`→`DouyinDanmuClient`。同一时刻只连一个。
- **签名**：node 子进程（`DouyinSigner`，App 层）。理由见 §10。

## 2. 协议实现细节（全部探针验证过，照搬）

### 2.1 取 room_id + ttwid（`DouyinRoomResolver`）
1. `GET https://live.douyin.com/{web_rid}`，headers：`User-Agent`(Chrome 126) + `Referer: https://live.douyin.com/`。从 `Set-Cookie` 抠 `ttwid`（`ttwid=...;` 取等号后到分号）。
2. `GET https://live.douyin.com/webcast/room/web/enter/?aid=6383&app_name=douyin_web&live_id=1&device_platform=web&language=zh-CN&cookie_enabled=true&screen_width=1920&screen_height=1080&browser_language=zh-CN&browser_platform=Win32&browser_name=Chrome&browser_version=126.0.0.0&web_rid={web_rid}&enter_from=web_live&is_need_double_stream=false`，带 `Cookie: ttwid={ttwid}` + Referer。**不需要 a_bogus**。
3. 响应 JSON 里正则 `"id_str"\s*:\s*"(\d{15,25})"` = 真实 room_id（19 位长号，如 `7669017417082850102`）。web_rid 是短号（如 `256438100956`），**不能**直接连 WS。
- HttpClient 用 CookieContainer 自动带 ttwid（探针验证 handler 自动管理可用）。

### 2.2 signature（X-Bogus，`DouyinSigner` 调 node）
算法（saermart）：
1. `ParamOrder = [live_id, aid, version_code, webcast_sdk_version, room_id, sub_room_id, sub_channel_id, did_rule, user_unique_id, device_platform, device_type, ac, identity]`
2. 从**不含 signature 的** WSS URL 的 query 取值（URL 解码），缺失字段空值。
3. `param = "k=v,k=v,..."`（严格按 ParamOrder 逗号拼接）。
4. `md5 = MD5(param).ToLowerHex()`（= X-MS-STUB）。
5. `getSign({"X-MS-STUB": md5})["X-Bogus"]` = signature。**这步用 node 跑**。
- C# 算 md5（`MD5.HashData` + `Convert.ToHexString`），subprocess `node sign_runner.js <md5>` 拿 X-Bogus（16 位左右字符串）。
- `user_unique_id`：本地随机 19 位数字（首位非 0），每次连接新生成。

### 2.3 WS 连接（`DouyinDanmuClient`）
- URL：`wss://webcast3-ws-web-lf.douyin.com/webcast/im/push/v2/?app_name=douyin_web&version_code=180800&webcast_sdk_version=1.3.0&update_version_code=1.3.0&compress=gzip&live_id=1&aid=6383&did_rule=3&device_platform=web&identity=audience&room_id={roomId}&user_unique_id={uid}&cursor=d-1_u-1&host=https://live.douyin.com&im_path=/webcast/im/fetch/&need_persist_msg_count=15&support_wrds_1&internal_ext={URL编码的 internal_src:dim|wss_push_room_id:{roomId}|wss_push_did:{uid}|first_req_ms:0|fetch_time:0|seq:1|wss_info:0-0-0-0|wrds_v:0}&signature={sig}`
- 域名必须带 `-ws-web-`（`webcast3-ws-web-lf`）；老的 `webcast3-normal-lf` 已 NXDOMAIN。
- headers：`User-Agent`(Chrome 126) + `Origin: https://live.douyin.com` + `Cookie: ttwid={ttwid}`。
- `ClientWebSocket`。握手 101 = signature 通过；返回 200 + `Handshake-Msg: auth failed` = signature 错。

### 2.4 protobuf 解码（`DouyinProto`，手写 varint，**零第三方**）
字段号（saermart `protobuf/douyin.py` 确认 + 探针 dump 实测）：
- **PushFrame**：`f1`=seq_id(varint), `f2`=log_id(varint), `f5`=headers(repeated), `f8`=payload(bytes, **gzip**)。取 `f2` + `f8`(gunzip→Response)。
- **Response**：`f1`=messages(repeated Message), `f5`=internal_ext(string), `f9`=need_ack(bool)。
- **Message**：`f1`=method(string), `f2`=payload(bytes)。
- **ChatMessage**：`f2`=user(message), `f3`=content(string)。
- **User**：`f3`=nick_name(string)。（id=f1，通常不要）
- **MemberMessage**：`f2`=user。**SocialMessage**：`f2`=user。**GiftMessage**：`f2`=user + gift 字段（实施时 dump 确认 gift.name/num 字段号）。**RoomUserSeqMessage**：`f3`=total(在线)。
- 手写：`ReadFields(byte[])` 遍历 varint key→(field<<3|wiretype)，wire0=varint / wire2=len-delimited（按字段号取）。gunzip 用 `GZipStream`。延续 DanmuFree B站 `DecodeInteractPb` 的手写风格。

### 2.5 ack（关键，不做则服务端断/不推进 cursor）
收到 PushFrame → 解 Response，若 `need_ack(f9)`：发回 `PushFrame{ f2=log_id(收到的), f7="ack"(string), f8=internal_ext(Response.f5 的 utf8 bytes，不 gzip) }`。
- 探针实测：缺 f7 → 每帧重推状态帧（cursor 不推进）；带 f7 → 推进正常。

### 2.6 心跳
每 10s 发空 `PushFrame{ f8=gzip(空) }`。

### 2.7 重连
`ClientWebSocket` 断开后指数退避重连（参照 B站 `BilibiliDanmuClient`）。**重连需重新签名**（每次连接算一次 signature）。

## 3. MessageType 映射
| 抖音 method | DanmuFree `MessageType` | RichMessage 字段 |
|---|---|---|
| `WebcastChatMessage` | `Danmu` | UserName=User.f3 nick_name, Text=ChatMessage.f3 content |
| `WebcastMemberMessage` | `Interact` | UserName=User.f3, Text="进入直播间"（→ 进场通知窗） |
| `WebcastSocialMessage` | `Interact` | UserName=User.f3, Text="关注了主播" |
| `WebcastGiftMessage` | `Gift` | UserName + 礼物名/数量（dump 确认字段） |
| `WebcastLikeMessage` | 忽略（或加 `Like`，v1 忽略） |
| `WebcastRoomUserSeqMessage` | `OnlineCount` | Extra=total(f3).ToString() |
| 其余（Banner/Rank/Stats 等） | 忽略 |
- `Interact` 复用现有进场/关注路由（`OnMessageReceived` 按 Text 门控 ShowEntry/ShowFollow）。

## 4. 文件清单
**Core 新增**（`src/DanmuFree.Core/`）：
- `Protocol/DouyinProto.cs` — 手写 protobuf（varint + PushFrame/Response/Message/Chat/User 解码 + BuildAck）。搬探针 `RawProtobuf.cs`+`DouyinDecoder.cs` 合并。
- `Protocol/DouyinRoomResolver.cs` — enter 接口取 room_id+ttwid。搬探针 `DouyinRoom.cs`。
- `Client/DouyinDanmuClient.cs` — WS 连接/心跳/ack/收帧解码→`RichMessage`/重连。搬探针 `DouyinWs.cs`，产出 `RichMessage` 而非 Console。

**Core 修改**：
- `Models/MessageType.cs` — 若加 `Like`（可选）。

**App 新增**（`src/DanmuFree.App/`）：
- `Services/DouyinSigner.cs` — 算 md5 + subprocess `node sign_runner.js <md5>` 拿 X-Bogus。搬探针 `DouyinSignature.cs`。
- `sign/` 目录 — `sign.js` + `sign_runner.js` + `package.json` + `node_modules/jsdom`（从 `probe/probe_node/` 复制），`CopyToOutputDirectory` 随 exe 分发。

**App 修改**：
- `Services/AppSettings.cs` — 加 `Platform`(Bilibili/Douyin), `DouyinRoomId`, `DouyinEnabled`。
- `ViewModels/DanmuViewModel.cs` — 加 `Platform`/`DouyinRoomId` observable；`ConnectAsync` 按 Platform 选 client。
- `Views/ControlWindow.xaml` — 连接区加平台单选（B站/抖音）+ 抖音房间号输入框（平台=抖音时显示）。
- `App.xaml.cs` — 启动检测 `node --version`（无则提示，不崩）。

## 5. node sidecar 设计
- 签名**只在 WS 连接（含重连）时算一次**（连接稳定期间不需要重签）。
- **v1（先做）**：每次连接 `Process.Start("node", "sign/sign_runner.js <md5>")`，读 stdout。~2s 延迟（node 启动+jsdom 加载），连接低频可接受。
- **v2（若重连频繁再升级）**：常驻 node 进程，stdin 喂 md5 / stdout 读 X-Bogus（避免每次启动开销）。
- `sign_runner.js`：`(function(){ <sign.js>; return getSign({X-MS-STUB:md5}); })()` 取 `X-Bogus` 写 stdout（探针验证可跑）。
- `sign.js` = saermart webmssdk 1.0.0.53 + jsdom 补环境（**Jint 跑不动**，反调试 >32000 层递归，必须 node+jsdom）。

## 6. 设置持久化
`AppSettings`：`Platform`(默认 Bilibili), `DouyinRoomId`(string), `DouyinEnabled`(bool)。`SaveSettings`/构造 Load 相应。几何持久化不动。

## 7. 分阶段实施
- **Phase 1 — Core 协议层**：`DouyinProto` + `DouyinRoomResolver`，Core 单测（用探针 dump 的真实 PushFrame 字节 + FakeHttpHandler 模拟 enter 接口）。`dotnet test` 绿。
- **Phase 2 — Client + 签名**：`DouyinDanmuClient` + `DouyinSigner` + `sign/` 目录。控制台冒烟（复用探针风格连真实房，dump RichMessage）。
- **Phase 3 — VM + UI**：`DanmuViewModel` 平台选择 + `ControlWindow` 平台切换/抖音房间输入。
- **Phase 4 — 设置持久化 + 冒烟**：`AppSettings` + 热门房 256438100956 真机冒烟（弹幕/进场/关注/统计都来）。
- 每阶段：`dotnet build` 0 错误 0 警告 + Core 单测绿。在 `feat/douyin` 分支做，完成 ff-merge 进 master + 删分支。

## 8. 测试策略
- **Core TDD**（Tests 不能引 App）：
  - `DouyinProto`：探针 dump 的真实 PushFrame 字节做回归（解出 method=WebcastChatMessage + content/nick）。
  - `DouyinRoomResolver`：`FakeHttpHandler.When(enter url, json)` 模拟，断言 room_id。
  - `DouyinSigner` 的 md5 算法：纯 C#，单测 param 拼接 + MD5。
- **App**：0/0 + 用户冒烟（不单测 UI/VM）。
- **冒烟房**：`256438100956`（有真人弹幕）。**别用 `32445368053`**（挂机房，0 弹幕，会误判）。

## 9. 风险与权衡
- **node 依赖**：违背 DanmuFree「纯单 exe」。用户机已有 node v22（自用接受）。分发需带 `sign/node_modules/jsdom`（~10MB，39 包）。`App.xaml.cs` 启动检测 node，无则友好提示。
- **抖音 WS 不稳**：可能频繁断重连 → 每次重连 2s 签名延迟。若明显，升级 §5 v2 常驻 sidecar。
- **签名算法变更**：抖音会换（_signature→X-Bogus→a_bogus→__ac_signature）。sign.js 跟 saermart/DouyinLiveWebFetcher 更新（GitHub 被墙，走 gitee `iuact` 镜像）。
- **webmssdk 反调试升级**：node+jsdom 现在过，未来可能需补环境。这是用 node（而非纯 C#）换来的稳定性红利——算法变了不用重新逆向，换 sign.js 即可。
- **风控**：高频/多房可能被限。单房自用低风险。

## 10. 探针参考代码（实施时直接搬）
- `probe/DouyinProbe/RawProtobuf.cs` + `DouyinDecoder.cs` → 合并为 Core `DouyinProto.cs`
- `probe/DouyinProbe/DouyinRoom.cs` → Core `DouyinRoomResolver.cs`
- `probe/DouyinProbe/DouyinSignature.cs` → App `DouyinSigner.cs`（C# md5 部分）
- `probe/DouyinProbe/DouyinWs.cs` → Core `DouyinDanmuClient.cs`（Console 输出改 RichMessage + 事件）
- `probe/probe_node/sign.js` + `sign_runner.js` + `node_modules/` → App `sign/`
- 参考实现：saermart/DouyinLiveWebFetcher（gitee `iuact` 镜像，`liveMan.py` + `protobuf/douyin.py`）

## 11. 环境注意（搬代码时）
- rebuild 前先 `taskkill //F //IM DanmuFree.exe`（运行中锁 dll → MSB3021）。
- repo 命令需在仓库根目录执行。
- GitHub raw 被墙；更新 sign.js 走 `https://gitee.com/iuact/DouyinLiveWebFetcher/raw/main/...`。
- 探针搬完后删 `probe/`（一次性，使命完成）。
