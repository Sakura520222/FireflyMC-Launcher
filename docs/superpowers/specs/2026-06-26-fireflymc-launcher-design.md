# FireflyMC Launcher 设计文档

- **日期**：2026-06-26
- **状态**：已确认（待实现规划）
- **作者**：Sakura520222
- **范围**：FireflyMC 生态专用 Minecraft 启动器，第一版（MVP）

---

## 1. 背景与目标

为 FireflyMC 整合包生态开发一个 Windows 桌面 Minecraft 启动器，覆盖账号、安装、更新、启动、启动器自更新五大模块，并对接 FireflyMC 官方 API 与第三方 mod 平台。

**核心目标：**
- 一键安装/启动 FireflyMC 整合包（Minecraft 1.21.1 + NeoForge 21.1.219）。
- 自动下载/更新整合包 mod（156 个，来源 Modrinth + CurseForge）。
- 自动下载/更新 Firefly mod（来源 GitHub Releases）。
- Microsoft 正版登录 + 本地离线账号。
- 启动器自身可热更新。
- 默认自动连入 FireflyMC 官服 `gm.rainplay.cn:32772`。

**非目标（YAGNI，第一版不做）：**
- 跨平台（仅 Windows x64）。
- 多实例管理（单一固定整合包）。
- 托盘功能、自定义标题栏（延后第二阶段）。
- 离线账号自定义皮肤。
- Mod 启用/禁用管理、截图管理。

---

## 2. 关键决策汇总

| # | 决策点 | 选定 |
|---|---|---|
| 1 | 技术栈 | C# + WPF，.NET 10（`net10.0-windows`，LTS 至 2028-11） |
| 2 | 目标平台 | 仅 Windows x64 |
| 3 | MS 登录 | 自实现 Device Code Flow 全链（不引入 MSAL） |
| 4 | MS 应用 | FireflyMC 自注册 Entra Public client（**不复用**官方 Launcher client_id），`consumers` tenant，`client_id` 走配置 |
| 5 | 离线账号 | 纯离线 UUID（`OfflinePlayer:<名>` 确定性哈希） |
| 6 | 整合包更新 | 方案 C：启动器回源 Modrinth + CurseForge |
| 7 | mod 下载源 | Modrinth 官方 API（免 key）+ CurseForge 走 BMCLAPI 镜像（内置 CF API key） |
| 8 | MC/NeoForge/Java 源 | 官方源优先 + BMCLAPI 回退；Java 21 经内置 `java-spec.json` 固定的 Adoptium JRE 捆绑下载（不取 latest） |
| 9 | 安装目录 | 启动器自带隔离 `.minecraft`（`ILauncherPaths` 注入） |
| 10 | 自动连服 | 默认连 `gm.rainplay.cn:32772`，设置可关；地址来自配置/Manifest 不写死 |
| 11 | 启动器自更新 | 独立 `Updater.exe` 替换整个发布目录 ZIP，Ed25519 验签，新版健康确认，失败回滚 |
| 12 | UI 范围 | 精简实用型五页：主页/账号/整合包管理/设置/关于 |
| 13 | 代码组织 | 经典分层（Models/Contracts/Services/Infrastructure/ViewModels/Views）+ MVVM（CommunityToolkit.Mvvm） |

---

## 3. 项目结构与分层（§1）

```
FireflyMC-Launcher/
├── FireflyMC-Launcher.sln
└── src/
    ├── FireflyMC.Launcher/                          （主 WPF 项目，net10.0-windows）
    │   ├── FireflyMC.Launcher.csproj                （OutputType=WinExe）
    │   ├── App.xaml / App.xaml.cs                   （DI 装配、全局异常分级、主题）
    │   ├── appsettings.json                         （MicrosoftAuth.ClientId 等只读基线）
    │   │
    │   ├── Models/                                  （内部领域模型）
    │   │   ├── Accounts/
    │   │   │   ├── AccountProfile.cs                （Id/Type/Username/Uuid/LastUsedAt，可序列化）
    │   │   │   ├── AccountSession.cs                （MS+MC Access Token + 双 ExpiresAt，纯内存）
    │   │   │   ├── MicrosoftCredential.cs           （RefreshToken/AccountId/UpdatedAt，DPAPI 落盘）
    │   │   │   └── AccountType.cs                   （enum: Microsoft / Offline）
    │   │   ├── Remote/                              （RemoteManifest 及子项，见 §6.2）
    │   │   ├── Installed/                           （InstalledManifest/InstalledFile）
    │   │   ├── Transaction.cs                       （TransactionState）
    │   │   ├── ResolvedModFile.cs
    │   │   ├── UpdatePlan.cs                        （FileToDownload/Delete/Keep）
    │   │   ├── LauncherSettings.cs
    │   │   ├── LaunchProfile.cs
    │   │   └── MergedVersionMetadata.cs
    │   │
    │   ├── Contracts/                               （外部 API DTO，隔离）
    │   │   ├── FireflyApi/
    │   │   │   ├── ModEntryResponse.cs              （/api/pack/mods 条目）
    │   │   │   └── VersionInfoResponse.cs           （/api/version 解析）
    │   │   └── Authentication/Microsoft/
    │   │       ├── DeviceCodeResponse.cs
    │   │       ├── MicrosoftTokenResponse.cs
    │   │       ├── XboxLiveTokenResponse.cs
    │   │       ├── XstsTokenResponse.cs
    │   │       ├── MinecraftTokenResponse.cs
    │   │       ├── MinecraftEntitlementsResponse.cs
    │   │       └── MinecraftProfileResponse.cs
    │   │
    │   ├── Services/
    │   │   ├── Accounts/
    │   │   │   ├── IAccountService.cs
    │   │   │   ├── AccountService.cs                （统一入口：登录/登出/刷新/列表）
    │   │   │   ├── Offline/
    │   │   │   │   ├── IOfflineAccountService.cs
    │   │   │   │   ├── OfflineAccountService.cs
    │   │   │   │   └── OfflineUuidProvider.cs       （OfflinePlayer:<名> UUID 计算）
    │   │   │   └── Microsoft/
    │   │   │       ├── IMicrosoftAuthService.cs
    │   │   │       ├── MicrosoftAuthService.cs      （编排 6 步全链）
    │   │   │       ├── IDeviceCodeLoginSession.cs
    │   │   │       └── DeviceCodeLoginSession.cs    （设备码展示 + 轮询 + 取消）
    │   │   ├── Install/    （IInstallService + InstallService）
    │   │   ├── Update/     （IModPackUpdateService + ModPackUpdateService）
    │   │   ├── Launch/     （ILaunchService + LaunchService）
    │   │   ├── SelfUpdate/ （ISelfUpdateService + SelfUpdateService）
    │   │   └── Operations/
    │   │       ├── ILauncherOperationCoordinator.cs （全局状态机仲裁）
    │   │       └── LauncherOperationCoordinator.cs
    │   │
    │   ├── Infrastructure/
    │   │   ├── Authentication/Microsoft/
    │   │   │   ├── IMicrosoftOAuthClient.cs         （device_code + 轮询 + refresh）
    │   │   │   ├── MicrosoftOAuthClient.cs
    │   │   │   ├── IXboxLiveClient.cs               （MS→XBL→XSTS）
    │   │   │   ├── XboxLiveClient.cs
    │   │   │   ├── IMinecraftServicesClient.cs     （XSTS→MC token→entitlements→profile）
    │   │   │   └── MinecraftServicesClient.cs
    │   │   ├── Download/   （IDownloader/HttpDownloader/MirrorRouter）
    │   │   ├── Platforms/  （IModPlatformClient/ModrinthClient/CurseForgeClient）
    │   │   ├── Minecraft/
    │   │   │   ├── McVersionInstaller.cs            （library rules/natives/logging/args/继承合并）
    │   │   │   ├── NeoForge/
    │   │   │   │   ├── InstallProfileReader.cs
    │   │   │   │   ├── ProcessorRunner.cs
    │   │   │   │   ├── MavenArtifactResolver.cs
    │   │   │   │   └── VersionJsonMerger.cs
    │   │   │   └── AdoptiumJavaRuntimeInstaller.cs
    │   │   ├── Storage/
    │   │   │   ├── ILauncherPaths.cs                （路径服务接口，可注入/可替换）
    │   │   │   ├── LauncherPaths.cs
    │   │   │   ├── IAccountStore.cs
    │   │   │   ├── ISettingsStore.cs
    │   │   │   ├── ISecretStore.cs                  （面向 MicrosoftCredential，DPAPI）
    │   │   │   ├── JsonAccountStore.cs
    │   │   │   ├── JsonSettingsStore.cs
    │   │   │   └── WindowsSecretStore.cs            （DPAPI CurrentUser）
    │   │   ├── Crypto/    （HashVerifier：仅 SHA-1/256；SecretRedactor：脱敏）
    │   │   └── Process/   （GameProcess）
    │   │
    │   ├── ViewModels/
    │   │   ├── ShellViewModel.cs                    （导航容器）
    │   │   ├── HomeViewModel.cs
    │   │   ├── AccountViewModel.cs
    │   │   ├── DownloadViewModel.cs
    │   │   ├── SettingsViewModel.cs
    │   │   └── AboutViewModel.cs
    │   └── Views/
    │       ├── MainWindow.xaml                      （原生标题栏 MVP）
    │       ├── Pages/ （HomeView/AccountView/DownloadView/SettingsView/AboutView）
    │       ├── Controls/
    │       ├── GameLogWindow.xaml                   （日志抽屉/独立窗口）
    │       └── DataTemplates.xaml                   （ViewModel→View 映射）
    │
    └── FireflyMC.Updater/                           （独立，net10.0-windows，控制台）
        ├── FireflyMC.Updater.csproj                 （OutputType=Exe → Updater.exe）
        └── Program.cs                               （待退出→Ed25519验签→解压→全文件校验→备份→替换→启动→等健康确认→回滚）
```

**分层约束：**
- 接口先行，单向依赖：Views → ViewModels → Services(接口) → Infrastructure，Infrastructure 不反向引用上层。
- `Contracts/` 隔离外部 API DTO，`Models/` 只含内部领域模型。
- `ILauncherPaths` 可注入，便于测试与便携模式。
- `FireflyMC.Updater` 无 WPF 依赖、无业务逻辑，只做发布目录替换。

---

## 4. 端到端数据流（§2）

### 流程 A · 账号登录（Microsoft 正版）

```
用户点"添加微软账号"
  → AccountService.LoginMicrosoftAsync()
    → MicrosoftAuthService.StartDeviceCodeLoginAsync()
      → MicrosoftOAuthClient.RequestDeviceCodeAsync()   POST .../consumers/oauth2/v2.0/devicecode
        ← DeviceCodeResponse { user_code, device_code, verification_uri, expires_in, interval }
      → 返回 DeviceCodeLoginSession（持有 CancellationTokenSource）给 UI
    → UI 弹设备码对话框（网址/码/复制/打开浏览器/倒计时/状态文案/取消；关闭即取消轮询）
    → MicrosoftAuthService.PollForLoginAsync(session, ct)
      → MicrosoftOAuthClient.PollTokenAsync(device_code, interval, ct)
        ← MicrosoftTokenResponse { access_token, refresh_token, expires_in }
        （处理 authorization_pending / slow_down[增大 interval] / authorization_declined
           / bad_verification_code / expired_token / 用户取消）
    → XboxLiveClient.RequestUserTokenAsync(msAccessToken)   → {Token, uhs}
    → XboxLiveClient.RequestXstsTokenAsync(userToken)       → {Token, uhs}
    → MinecraftServicesClient.LoginWithXboxAsync(uhs, xsts) → MC access_token
    → MinecraftServicesClient.GetEntitlementsAsync(mcToken) → 校验已购游戏，否则明确报错
    → MinecraftServicesClient.GetProfileAsync(mcToken)      → {id, name}
    → 组装：AccountProfile + AccountSession + MicrosoftCredential
  → 账号落盘事务（任一失败清理已写部分）：
      SecretStore.SaveMicrosoftCredentialAsync(credential)   DPAPI 落盘
      → AccountStore.SaveAsync(profile)                      accounts.json
  → 返回 profile
```

**凭据与并发规则：**
- `AccountSession` 分别记录 MS Token 与 MC Token 的 `ExpiresAt`，启动前在过期前几分钟主动刷新。
- 每账号 `SemaphoreSlim`（`ConcurrentDictionary<AccountId, SemaphoreSlim>`）防止并发刷新互相覆盖。
- Refresh Token 原子替换：拿完整新令牌 → 验证后续 XBL/XSTS/MC 链成功 → 写 tmp → DPAPI 加密 → 原子替换正式凭据 → 删旧。不长期持久化短期 Access Token。
- Device Code 过期不自动无限重试，显示"设备代码已过期 [重新获取代码]"。

**离线登录：** `OfflineAccountService` → `OfflineUuidProvider.GetUuid(username)` → 直接生成 `AccountProfile`，无 token，写 `accounts.json`。

**登出：** 同时删除 内存 `AccountSession` + `accounts.json` 中 `AccountProfile` + DPAPI 中 `MicrosoftCredential`。

### 流程 B · 首次安装

```
InstallService.InstallAsync(progress, ct)
  阶段0 拉清单: 拉 /api/pack/mods + /api/version → 组装 RemoteManifest
                 （注：Java 规格来自启动器内置 java-spec.json，后端不提供）
  阶段1 Java:   AdoptiumJavaRuntimeInstaller（按 java-spec.json 固定的 JRE：vendor/runtimeVersion/sha256/url）
                  → 下载 + SHA 校验 → 解压到 <root>/runtime/java-21
  阶段2 MC:     McVersionInstaller（1.21.1 version.json → 官方/BMCLAPI 回退）
                  → client.jar + libraries(rules 过滤) + native classifiers + natives 解压
                  + assets index/objects + logging 配置 + arguments(jvm/game) + 版本继承合并 + OS/架构规则
  阶段3 NeoForge: 走 installer.jar 全流程（21.1.219）
                  InstallProfileReader → MavenArtifactResolver 下载 libraries
                  → ProcessorRunner 执行 processors + data/placeholders → VersionJsonMerger 生成版本 JSON
  阶段4 mods:   ModPackUpdateService.SyncAsync()（复用流程 D 事务）
  阶段5 Firefly mod: 下载 VersionInfo.modUrl（GitHub）→ SHA 校验 → 放 mods/
```

### 流程 C · 启动 Minecraft

```
LaunchService.LaunchAsync(account, ct)
  → LauncherOperationCoordinator.AcquireLaunchLock()   （实例互斥，禁止安装/更新/修复并发）
  → 校验 Java 版本 + 实例安装状态
  → 按策略决定是否更新；更新服务器不可用且非强制更新 → 允许离线启动
  → 取/刷新 AccountSession（流程 A 刷新路径，保证 MC token 有效）
  → 读 LauncherSettings
  → 合并版本元数据：原版 1.21.1 version.json + NeoForge 输出 → MergedVersionMetadata
  → 生成 LaunchProfile：
      rules 过滤 → ${} 展开 → classpath 构造 → natives 目录 → logging 参数
      → 主类 → NeoForge 参数 → 账号参数 → (连服开关 ON 时) --server/--port
  → GameProcess.Start(args)  stdout/stderr 经脱敏后入日志，注册 OnExit 回调
```

**约束：** 服务器地址来自配置/Manifest（`gm.rainplay.cn:32772`），不写死；启动命令行日志必须对 `--accessToken` 等脱敏。

### 流程 D · 整合包更新（事务式 + 动态解析）

```
ModPackUpdateService.SyncAsync(progress, ct)
  Resolve:  拉 /api/pack/mods + /api/version → 组装 RemoteManifest（客户端自算 ManifestSha256）
  Plan:     逐条解析 ResolvedModFile：
              有 version → platformId + version字符串 + [1.21.1] + [neoforge] 精确定位
              无 version → 去中文前缀文件名匹配；匹配不到取该 project 在 1.21.1+neoforge 下最新
              platformId 字母数字→Modrinth，纯数字→CurseForge(BMCLAPI 镜像 + 内置 key)
              解析失败计入阈值（>10% 中止）
            比对本地 InstalledManifest.ManagedFiles → 生成 UpdatePlan
              缺失/SHA-1 不一致 → Downloads（含 required 标记）
              在 ManagedFiles 且新清单不含 → Deletes
              一致 → Keeps
              本地有但不在 ManagedFiles（用户自加）→ 保留 + 标"未受管理"
  Stage:    下载全部 Downloads 到 staging/（HttpDownloader，断点续传/重试/取消）
  Verify:   HashVerifier 对 staging 全数 SHA-1 校验（required:false 失败可跳过，required:true 失败→中止+回滚）
  Commit:   backup（待替换 copy、待删除 move 到 backup-<tx>/）→ 替换 → 原子写 InstalledManifest
  Cleanup:  成功 backup 转 retained（保留 1 份），删 staging + 过期 retained
```

**职责分离：** `IDownloader`（下载/续传/进度/重试/取消）/ `IHashVerifier`（SHA-1/256）/ `IUpdateTransaction`（备份/替换/删除/回滚）。API JSON 请求走类型化 `HttpClient`。

**解析策略说明：** 现有 API 不提供 versionId/fileId，采用动态解析（不追求跨时间完全固定）；解析得到的 sha1 用于事务校验，保证每次安装完整可验证。

### 流程 D' · 版本对账

不依赖 `importedAt`。快速检查用 `manifestId` / ETag；真正判定比 `RemoteManifestSha256` 与本地 `InstalledManifest.RemoteManifestSha256`。**仅事务 Commit 成功后**才写 `LastInstalledPackVersion`/`RemoteManifestSha256`/`InstalledAt`/`ManagedFiles`。

### 流程 E · 启动器自更新

```
SelfUpdateService.CheckAsync()  → GitHub Releases（区分 stable/beta 渠道，不盲用 latest）
  有新版 → UI 提示
用户点"立即更新"：
  1. 下载完整发布包 ZIP + 独立签名 到 <root>/update/
  2. SelfUpdateService 拉起 Updater.exe（传 新包路径/旧目录/nonce），自己退出
  3. Updater.exe：
     Pending(等 Launcher 退出，轮询+超时) → Verified(Ed25519 验签 + package-manifest.json 全文件校验)
     → BackingUp(备份旧发布目录) → Replacing(替换整目录) → Launching(启新 Launcher, 等 success-<nonce>)
     → Confirmed(删 backup)
  4. Launching 超时未确认或检测新进程崩溃 → 自动回滚（backup 恢复旧目录）→ 启动旧版 + 报错
```

**发布形态：** 完整目录 ZIP（`FireflyMC.Launcher.exe` + `appsettings.json` + 依赖 + `package-manifest.json`），不假设单 EXE。SHA-256 只检测传输损坏，正式自更新用 Ed25519 离线签名（私钥 CI/离线，公钥编译进 Launcher/Updater）。

### 流程 F · 恢复/修复（启动时）

启动器启动时检测：`.installing` 标记 / `transaction.json` / 残留 `.part` / `staging/` / `.bak` / `InstalledManifest` 与实际文件不一致。按事务状态恢复（详见 §6）。修复复用更新逻辑：`ResolveManifestAsync` / `BuildUpdatePlanAsync(forceVerify:true)` / `ExecuteTransactionAsync`。

---

## 5. UpdateTransaction 状态机、崩溃恢复、备份、错误分类（§3）

### 5.1 整合包更新事务状态机

事务生命周期由 `<root>/update/transaction.json` 持久化。

```
Idle → Resolving → Planning → Staging → Verifying → Committing → Cleanup → Idle
                  （任何阶段 fatal/取消）                （Committing fatal/取消）
                       ↓                                     ↓
              Staging 前崩溃：删 staging → Idle      用 backup 回滚 → Idle
```

**持久化时机：** 进入每状态更新 `state`；`Committing` 开始写 `backupPath`；`Commit` 成功先写 `InstalledManifest` 再 `Cleanup`；`Cleanup` 完成删 `transaction.json`。

**核心不变量：只有 `Committing` 阶段修改游戏文件**，backup 仅在该阶段创建。

### 5.2 崩溃恢复点

| 崩溃时状态 | 游戏文件被改 | 恢复动作 |
|---|---|---|
| Resolving/Planning | 否 | 删 `transaction.json` → Idle |
| Staging/Verifying | 否 | 删 `staging/` + `transaction.json` → Idle |
| Committing（部分） | 是 | 用 `backupPath` 逐文件还原已替换/已删除 → 删 staging + transaction → Idle |
| Commit 完成、未 Cleanup | 是（已成功） | 安全：删 staging + 过期备份 → Idle |
| Cleanup 中 | 是（已成功） | 安全：Cleanup 幂等重跑 → Idle |

### 5.3 备份保留策略

```
<root>/update/
├── transaction.json
├── staging/
├── backup-<transactionId>/     （本次 Committing 备份，回滚用；成功后转 retained）
└── retained/                   （上一次成功事务快照，保留 1 份，供手动回退）
```

- Committing 内部：待替换 `copy` 旧→backup 再覆盖；待删除 `move` 到 backup（可还原）。
- 当前事务 backup：回滚完成即删。
- 成功事务 backup：重命名 `retained/`，删旧 retained；超容量阈值（500MB）UI 提示清理。

### 5.4 错误分类

| 类别 | 触发 | 处理 |
|---|---|---|
| 瞬时·可重试 | 网络超时/重置/503/429 | 指数退避，MaxRetries=3 |
| 瞬时·必需文件 | required:true 下载失败 | 重试 N 次；仍失败→中止+回滚 |
| 瞬时·可选文件 | required:false 下载/校验失败 | 重试 N 次仍失败→跳过，事务继续 |
| 瞬时·校验失败 | SHA-1 不匹配 | 换镜像重下 N 次 |
| fatal·中止不回滚 | Staging 前/中：磁盘不足、解析失败>10%、Manifest 摘要失败、权限错误 | 中止，删 staging |
| fatal·中止+回滚 | Committing IO 失败；required 校验全失败 | 用 backup 回滚 |
| 用户取消 | 任意阶段 | Staging/Verifying→删 staging；Committing→回滚 |
| 离线容忍 | 更新检查网络不可用 | 不中止启动，降级离线启动（非强制更新） |

**重试参数：** `MaxRetries=3`，退避基准 `2s`，单文件超时 `60s`，集中在 `UpdateOptions`。

### 5.5 自更新状态机变体（Updater 跨进程）

`Pending → Verified → BackingUp → Replacing → Launching(等 success-<nonce>) → Confirmed`。Launching 超时/新进程崩溃 → 自动回滚 + 启动旧版。Updater 自身崩溃 → 下次旧 Launcher 启动检测残留 `update/` 清理 + 提示。

---

## 6. 配置体系与数据契约（§4）

### 6.1 配置分层

优先级：运行时设置 > `appsettings.json` > 内置默认。

**`appsettings.json`（随发布，只读基线）：**
```json
{
  "MicrosoftAuth": {
    "ClientId": "<FireflyMC自注册Entra client_id>",
    "Tenant": "consumers",
    "Scopes": ["XboxLive.signin", "offline_access"]
  },
  "CurseForge": { "ApiKey": "<内置CF key>", "UserAgent": "FireflyMC-Launcher" },
  "SelfUpdate": {
    "ReleasesApi": "https://api.github.com/repos/Sakura520222/FireflyMC-Launcher/releases",
    "Channel": "stable",
    "PublicKey": "<Ed25519公钥 base64>"
  },
  "Game": {
    "MinecraftVersion": "1.21.1",
    "NeoForgeVersion": "21.1.219",
    "Server": { "Host": "gm.rainplay.cn", "Port": 32772 }
  },
  "Mirrors": {
    "MinecraftPrimary": "https://piston-meta.mojang.com",
    "MinecraftFallback": "https://bmclapi2.bangbang93.com",
    "NeoForgePrimary": "https://maven.neoforged.net",
    "NeoForgeFallback": "https://bmclapi2.bangbang93.com/maven",
    "AdoptiumPrimary": "https://api.adoptium.net",
    "AdoptiumFallback": "https://mirrors.tuna.tsinghua.edu.cn/Adoptium"
  },
  "Update": {
    "MaxRetries": 3,
    "RetryBaseDelaySeconds": 2,
    "PerFileTimeoutSeconds": 60,
    "ResolveFailureThresholdPercent": 10
  },
  "FireflyApi": {
    "Version": "https://mc.firefly520.top/api/version",
    "PackMods": "https://mc.firefly520.top/api/pack/mods"
  }
}
```

**`java-spec.json`（随发布，只读，固定 Java 规格）：**
```json
{
  "vendor": "eclipse",
  "major": 21,
  "runtimeVersion": "21.0.x+xx",
  "architecture": "x64",
  "imageType": "jre",
  "sha256": "...",
  "url": "https://..."
}
```
固定到具体 JRE 版本（不取 latest），整合包升级 Java 时随启动器新版本发布更新。由 `AdoptiumJavaRuntimeInstaller` 消费。

**`settings.json`（`ISettingsStore`，用户可改）：** 内存分配（自动/手动 MinMb/MaxMb）、JVM 参数补丁、Java 路径覆盖（空=捆绑 JRE）、镜像开关（`UseMirror`）、自动连服开关、窗口分辨率、自动检查更新开关、当前账号 Id。

**`accounts.json`（`IAccountStore`）：** 仅 `AccountProfile[]`。

**DPAPI（`ISecretStore`）：** 按 `accountId` 存 `MicrosoftCredential`。

### 6.2 RemoteManifest（启动器消费的整合包清单）

启动器从 `/api/pack/mods` + `/api/version` 组装（领域模型，非 API 原样 DTO）。**后端零改动**，因此 Java/Server/ForceUpdate 不来自后端，由启动器内置配置补充：

```csharp
public sealed record RemoteManifest(
    int SchemaVersion,              // = 1
    string PackVersion,             // 来自 /api/version
    string ManifestId,              // = ManifestSha256 前 12 位（客户端派生，不依赖 importedAt）
    string ManifestSha256,          // 客户端对规范化后 Mods+PackVersion+FireflyMod 自算
    DateTimeOffset GeneratedAt,     // 组装时刻
    IReadOnlyList<RemoteModEntry> Mods,   // 来自 /api/pack/mods
    FireflyModEntry FireflyMod,     // 来自 /api/version
    JavaRuntimeSpec Java,           // 来自启动器内置 java-spec.json（后端不提供）
    GameServerSpec Server,          // 来自 appsettings.json Game.Server（后端不提供）
    bool ForceUpdate                // 后端暂无此字段，默认 false，预留
);
public sealed record RemoteModEntry(
    string Name, string FileName, long FileSize,
    ModPlatform Platform, string ProjectId, string? VersionLabel, bool Required = true);
public sealed record FireflyModEntry(string Version, string DownloadUrl, string? Sha256);
public sealed record JavaRuntimeSpec(string Vendor, int Major, string RuntimeVersion, string ImageType, string Sha256, string Url);
public sealed record GameServerSpec(string Host, int Port);
```

**字段组装来源：**
- 后端 `/api/version` + `/api/pack/mods`：`PackVersion`、`Mods`、`FireflyMod`。
- 启动器自算：`ManifestSha256`、`ManifestId`、`GeneratedAt`。
- 启动器内置 `java-spec.json`：`Java`（固定 JRE，不取 latest）。
- `appsettings.json`：`Server`。
- 预留：`ForceUpdate`（恒 false）。

### 6.3 InstalledManifest

```csharp
public sealed record InstalledManifest(
    int SchemaVersion, string LastInstalledPackVersion, string RemoteManifestSha256,
    DateTimeOffset InstalledAt, string MinecraftVersion, string NeoForgeVersion, string JavaRuntimeVersion,
    IReadOnlyList<InstalledFile> ManagedFiles, FireflyModEntry FireflyMod);
public sealed record InstalledFile(string RelativePath, long Size, string Sha1);
```

仅事务 `Commit` 成功后原子写（tmp→rename）。

### 6.4 TransactionState

```csharp
public sealed record TransactionState(
    Guid TransactionId, UpdatePhase Phase, string TargetManifestSha256, string? BackupPath,
    DateTimeOffset UpdatedAt, IReadOnlyList<string> StagedFiles,
    IReadOnlyList<string> ReplacedFiles, IReadOnlyList<string> DeletedFiles);
```

### 6.5 UpdatePlan

```csharp
public sealed record UpdatePlan(
    string TargetManifestSha256,
    IReadOnlyList<FileToDownload> Downloads,
    IReadOnlyList<FileToDelete> Deletes,
    IReadOnlyList<FileToKeep> Keeps,
    FireflyModAction FireflyModAction,
    bool JavaRuntimeChanged);
public sealed record FileToDownload(string RelativePath, ResolvedModFile Source, bool Required);
public sealed record FileToDelete(string RelativePath);
public sealed record FileToKeep(string RelativePath, string Sha1);
```

---

## 7. UI 结构与交互（§5）

### 7.1 导航

`ContentControl + DataTemplate`（不用 Frame）。`ShellViewModel` 持 `CurrentPageViewModel` + `NavigationItems` + `NavigateCommand`；`DataTemplates.xaml` 绑定 ViewModel→View。`INavigationService.NavigateTo<TViewModel>()` 底层不依赖 Frame。

### 7.2 五页职责

| 页 | ViewModel | 标题 | 职责 |
|---|---|---|---|
| 主页 | HomeViewModel | — | 账号选择/实例状态/主按钮（启动入口） |
| 账号 | AccountViewModel | 账号 | 账号列表/添加 MS（设备码对话框）/添加离线/设默认/刷新/登出 |
| 整合包管理 | DownloadViewModel | **整合包管理** | 检查更新/事务进度/156 mod 列表（虚拟化）/未管理文件提示/修复/版本信息 |
| 设置 | SettingsViewModel | 设置 | 性能/Java/网络/游戏/启动器/数据/调试 |
| 关于 | AboutViewModel | 关于 | 版本信息/链接/手动触发自更新 |

日志通过右侧抽屉或 `GameLogWindow`（不新增第六页）。

### 7.3 全局操作协调器

`LauncherOperationCoordinator`（`Services/Operations/`）+ `LauncherOperationState`（Idle/Checking/Installing/Updating/Repairing/PreparingLaunch/Launching/GameRunning/SelfUpdating/Recovering/Failed）。负责：安装/更新/修复/启动互斥、统一取消令牌、广播状态、控制按钮可用性、Commit 阶段禁关闭、防账号刷新与启动刷新重复。各 ViewModel 订阅统一状态源。

### 7.4 主按钮状态机（12 态）

| 状态 | 文案 | 行为 |
|---|---|---|
| Checking | 正在检查… | 禁用 |
| Install | 安装游戏 | 安装 |
| Repair | 修复游戏 | 修复 |
| UpdateAndLaunch | 更新并启动 | 更新后启动 |
| Launch | 启动游戏 | 启动 |
| Preparing | 正在准备… | 可取消 |
| Installing | 正在安装 | 可取消 |
| Updating | 正在更新 42% | 可取消 |
| Launching | 正在启动… | 禁用 |
| Running | 游戏运行中 | 打开日志/激活窗口 |
| Canceling | 取消中… | 禁用 |
| Unavailable | 无法启动 | 展示原因 |

**默认"更新并启动"**；强制更新（`ForceUpdate`）不可跳过；普通更新右侧下拉提供"仅更新 / 启动当前版本"（后者需实例完整+非强制+无进行中事务）。进度用独立取消按钮，**Commit 阶段取消禁用**。

### 7.5 进度模型

```csharp
public sealed record StageProgress(
    OperationStage Stage, double? StagePercent, double? OverallPercent,
    string? CurrentItem, long CompletedBytes, long? TotalBytes,
    double? BytesPerSecond, TimeSpan? EstimatedRemaining, bool CanCancel);
```

百分比可 null（不确定进度，如"正在解析 Modrinth 文件"）。总进度加权：Resolve 5% / Plan 2% / Stage 75% / Verify 10% / Commit 7% / Cleanup 1%。下载速率节流 200–500ms 更新。

### 7.6 Mod 列表

虚拟化（`IsVirtualizing=True` + `VirtualizationMode=Recycling` + `IsDeferredScrollingEnabled=True`）。状态含文本（已是最新/等待下载/文件缺失/校验失败/可选文件/未受管理），色觉友好，不依赖符号/颜色。

### 7.7 设备码对话框

含：登录网址/用户代码/复制代码/打开浏览器/剩余时间/当前状态（等待登录→验证账号→验证 Xbox→检查所有权→读取档案→完成）/取消。过期不无限重试，显示"重新获取代码"。关闭即取消轮询。

### 7.8 单实例与窗口

单实例 `Mutex` + 命名管道/窗口消息激活已有窗口（不只检测 Mutex 后退出）。游戏启动后默认最小化主窗口；游戏退出后按设置决定恢复。**托盘延后二阶段**。**标题栏 MVP 用原生**（后续 `WindowChrome`）。

### 7.9 日志脱敏（永远开启，不可关闭）

必须脱敏：`access_token`/`refresh_token`/`device_code`/`Authorization` 头/XBL Token/XSTS Token/MC access token/`--accessToken`/DPAPI 密文/**IP 地址**。任何日志级别（含 Trace）都脱敏。仅"记录网络诊断信息"和"显示用户名/UUID"可开关。

### 7.10 全局异常分级

| 入口 | 处理 |
|---|---|
| `DispatcherUnhandledException` | 已知可恢复 UI 异常→记录提示继续；未知异常→存诊断后安全退出 |
| `AppDomain.UnhandledException` | 记录最后错误，通常不可恢复 |
| `TaskScheduler.UnobservedTaskException` | 记录错误，从源头确保所有 Task 被 await |

Commit/凭据写入/自更新替换/InstalledManifest 写入异常 → 立即停后续 + 进恢复流程。异步约束：网络/文件 I/O 用真 async API，CPU 密集才 `Task.Run`，UI 改动回 Dispatcher，不机械 `Task.Run`。

---

## 8. 测试策略（§6）

### 8.1 分层目标

| 层 | 范围 | 形态 |
|---|---|---|
| 单元 | Infrastructure + Services 纯逻辑 | xUnit + FluentAssertions |
| 契约/集成 | 平台客户端/下载器对真实端点录像 | WireMock.Net 回放 |
| UI | 关键交互流 | 手动 checklist |

WPF 视图不自动化；ViewModel 全可测（不依赖 `Application`/Dispatcher 静态）。

### 8.2 单元测试重点（按风险）

1. **事务状态机与崩溃恢复**：各状态进入/退出；§5.2 各崩溃点中断→恢复正确；Committing 部分提交→backup 回滚；回滚幂等；InstalledManifest 仅 Commit 成功后写。
2. **认证全链**：6 步编排顺序；各段失败（401/XErr 2148916233 未注册 Xbox / 2148916238 未成年 / entitlements 空）；轮询 pending/slow_down/expired/取消；并发刷新 SemaphoreSlim 串行；Refresh Token 原子覆盖；凭据落盘事务。
3. **启动参数生成**：rules 过滤、`${}` 展开、classpath、natives、连服参数条件；脱敏（`--accessToken` 被遮蔽）。
4. **Mod 解析**：有/无 version 路由；platformId 格式路由；解析失败阈值。
5. **删除规则**：ManagedFiles 中且新清单不含→删；用户自加→保留；空集合边界。
6. **离线 UUID**：`OfflinePlayer:<name>` 与 Java `UUID.nameUUIDFromBytes` 一致（已知向量）。
7. **HashVerifier/脱敏器**：SHA 向量；脱敏器对所有敏感字段（含 IP）。
8. **操作协调器**：互斥、Commit 阶段拒新操作、取消级联。

### 8.3 契约测试（WireMock.Net 录像回放）

Modrinth/CurseForge(BMCLAPI)/`/api/pack/mods`/`/api/version`/version_manifest/NeoForge install_profile.json/Adoptium 响应形状；主源故障→镜像回退。

### 8.4 端到端手动 checklist（发布前）

- [ ] 全新机器一键安装→启动→连入官服
- [ ] MS 登录→重启静默刷新
- [ ] 离线账号启动
- [ ] 整合包更新（有/无/强制）
- [ ] 更新中途强杀→重启恢复（各崩溃点）
- [ ] 用户自加 mod 更新后仍在
- [ ] 启动器自更新（含新版崩溃回滚）
- [ ] 断网离线启动（非强制）
- [ ] 日志检查：任何级别 token/IP 脱敏
- [ ] 单实例二次启动激活窗口

### 8.5 测试项目结构

```
test/FireflyMC.Launcher.Tests/
├── Authentication/   Update/   Launch/
├── Infrastructure/   Operations/   Contracts/
```

`ILauncherPaths`/`ISecretStore`/`IDownloader` 接口化 → 测试用 in-memory/临时目录假实现，无真实网络与系统依赖。

---

## 9. 外部依赖与前置条件

实现前/发布前需准备的资源（不阻塞设计，但阻塞发布）：

| 项 | 说明 | 阻塞阶段 |
|---|---|---|
| **Entra 应用注册** | FireflyMC 自注册 Public client（Personal accounts、device code flow、无 secret），申请 Minecraft API 权限（`api.minecraftservices.com`），拿 `client_id` 填入 `appsettings.json` | 发布前 |
| **CurseForge API key** | 注册 CurseForge Core API key（低权限读公开 mod），内置 | 发布前 |
| **Ed25519 密钥对** | 离线生成私钥（存 CI Secret/离线），公钥编译进 Launcher/Updater；CI 用私钥签名发布包 | 发布前 |
| **GitHub Releases** | 启动器自身发布仓库 `Sakura520222/FireflyMC-Launcher`，按 stable/beta 渠道发布 ZIP + 签名 | 发布前 |
| **后端数据** | 现有 `/api/version`、`/api/pack/mods` 已满足；动态解析接受 90 个空 `version`（取最新兼容版） | 无 |
| **.NET 10 SDK** | 构建环境需 net10.0 | 开发期 |

---

## 10. 参考资源

- Minecraft Microsoft 认证全链：https://minecraft.wiki/w/Microsoft_authentication
- OAuth 2.0 Device Authorization Grant：https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-device-code
- Refresh Token 替换：https://learn.microsoft.com/en-us/entra/identity-platform/refresh-tokens
- NeoForge 客户端安装：https://docs.neoforged.net/user/docs/client/
- Modrinth API（稳定 ID 建议）：https://docs.modrinth.com/api/
- CurseForge API：https://docs.curseforge.com/rest-api/
- Adoptium API：https://github.com/adoptium/api.adoptium.net/blob/main/docs/cookbook.adoc
- .NET 单文件发布：https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
- GitHub Releases API：https://docs.github.com/en/rest/releases/releases
- BMCLAPI：https://bmclapi2.bangbang93.com
