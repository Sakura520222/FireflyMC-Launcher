# FireflyMC Launcher 下载源与配置修订

- **日期**：2026-06-27
- **状态**：补充设计（基于现实约束变更，已验证可行）
- **关联**：[2026-06-26-fireflymc-launcher-design.md](2026-06-26-fireflymc-launcher-design.md)
- **性质**：对原 spec 决策 #4（client_id）、#7（mod 下载源）、Java 校验条款的修订；本文为后续代码落地的依据。

---

## 1. 背景：三个现实约束变化

原 spec 基于三项假设，现均不成立，需修订：

| # | 原 spec 假设 | 现实 | 影响 |
|---|---|---|---|
| 1 | FireflyMC 自注册 Entra Public client，拿 `client_id` | 租户长时间未活跃被注销，**无法注册** | Microsoft 登录无 client_id |
| 2 | 内置 CurseForge Core API key | **无法注册** CF key | 27 个 CF mod 无法经官方 API 解析 |
| 3 | Java 下载需 SHA-256 校验 | **不需要**校验 | 移除校验逻辑 |

---

## 2. 决策变更（对原 spec 的影响）

| 原 spec 条款 | 原决策 | **新决策** |
|---|---|---|
| 决策 #4（MS 应用） | 自注册 Entra Public client，不复用官方 ID | **改用 Minecraft 官方 launcher 公开 client_id `00000000402b5328`**（社区广泛使用，规避未成年家庭限制）；仍走配置不硬编码于认证代码 |
| 决策 #7（mod 下载源） | Modrinth 官方（免 key）+ CurseForge 走 BMCLAPI 镜像（内置 CF key） | **四级降级链 + MCIM 镜像**：Modrinth 官方 → Modrinth 镜像 → CurseForge 镜像（MCIM，免 key）→ CurseForge 官方（需 key，当前跳过）。**完全不需要 CF key** |
| §6.1 java-spec.json | 含 `sha256` 字段，下载后校验 | **移除 `sha256` 字段及校验逻辑**；仅靠 HTTPS 传输层保护 |
| §6.1 CurseForge.ApiKey | 内置 CF key | **移除**（走 MCIM 免 key） |

---

## 3. mod 下载源四级降级链

对**每个 mod**（无论原 platformId 是 Modrinth 还是 CurseForge）按序尝试，前一源失败/无匹配才降级：

```
┌─────────────────────────────────────────────────────────────────────┐
│ 1. Modrinth 官方     api.modrinth.com      文件: cdn.modrinth.com   │
│         ↓ 失败/无匹配                                                │
│ 2. Modrinth 镜像     mod.mcimirror.top/modrinth  文件: mod.mcimirror.top │
│         ↓ 该 mod 不在 Modrinth（如 FTB 系列）                        │
│ 3. CurseForge 镜像   mod.mcimirror.top/curseforge（免 key）          │
│                      文件: mod.mcimirror.top（替换 forgecdn）        │
│         ↓ 失败（MCIM 缓存缺该 mod，极少）                            │
│ 4. CurseForge 官方   api.curseforge.com（需 key——当前无 key，跳过）  │
└─────────────────────────────────────────────────────────────────────┘
```

> **与用户表述的差异说明**：用户给的顺序是 `Modrinth → Modrinth镜像 → CF → CF镜像`。因无 CF key，CF 官方（第 3）必然失败，实现时把 **CF 镜像提到 CF 官方之前**（省一次无效请求），CF 官方留作"未来有 key 时启用"的占位。语义一致。

### URL 替换规则（MCIM）

| 官方源 | MCIM 镜像 | 用途 |
|---|---|---|
| `api.modrinth.com` | `mod.mcimirror.top/modrinth` | Modrinth API |
| `cdn.modrinth.com` | `mod.mcimirror.top` | Modrinth 文件下载 |
| `api.curseforge.com` | `mod.mcimirror.top/curseforge` | CurseForge API（免 key） |
| `edge.forgecdn.net` / `mediafilez.forgecdn.net` | `mod.mcimirror.top` | CurseForge 文件下载 |

启动器从 API 拿到文件 URL 后，按本表替换主机即走镜像。

---

## 4. MCIM 接入规范

### 4.1 CurseForge 查询策略（关键）

MCIM 的 CF API **不能带 `modLoaderType` 筛选参数**（实测带 `modLoaderType=4` 返回 0 文件，MCIM 对该筛选支持有缺陷）。正确做法：

```
GET https://mod.mcimirror.top/curseforge/v1/mods/{projectId}/files?gameVersion=1.21.1
（不带 modLoaderType）
```

返回的每个文件含 `gameVersions` 数组（如 `['1.21.1', 'NeoForge']`），**客户端按以下条件过滤**：
- `gameVersions` 含 `'1.21.1'`
- `gameVersions` 含 `'NeoForge'`
- `fileName` 或版本号匹配整合包要求（动态解析：优先 version 字符串精确，无则取最新）

### 4.2 缓存新鲜度（sync_at）

MCIM 是缓存服务，**可能滞后**（实测 Modrinth 端点返回了旧版本号）。每个 MCIM 响应在 Header `sync_at`（格式 `YYYY-MM-DDTHH:MM:SSZ`）标注缓存时间。

策略：
- 解析 `sync_at`，若**超过阈值（建议 7 天）视为陈旧**，降级到下一源或官方源。
- 若 `sync_at` 缺失（实测部分响应无此 header），不阻塞，但记录日志、优先尝试官方源校验。

### 4.3 User-Agent（必须合规）

MCIM 与 Modrinth 都要求 UA **唯一标识启动器**（不能仅是 HTTP 库名），否则可能被拒。MCIM 还要求**提交 UA 到白名单**。

**完整 UA 字符串**：
```
FireflyMC-Launcher/1.0.0 (https://github.com/Sakura520222/FireflyMC-Launcher)
```

格式依据：`<应用名>/<版本> (<联系/标识>)`，符合 [Modrinth API UA 规范](https://docs.modrinth.com/api/) 与 MCIM 接入要求。
- 版本号 `1.0.0` 随发布动态更新（从程序集版本拼接，见 §7 代码变更）。
- 括号内为 GitHub 仓库 URL，提供唯一标识 + 联系途径。

---

## 5. CurseForge 独占 mod 验证（免 key 可行性）

实测 2026-06-27，CF 独占 mod 经 MCIM CF 镜像可正常解析：

| mod | projectId | MCIM CF API 结果 |
|---|---|---|
| FTB Ultimine | 386134 | ✅ 16 个 1.21.1 文件，含 `ftb-ultimine-neoforge-2101.1.15.jar` |
| Biomes O' Plenty | 220318 | ✅ 含 `BiomesOPlenty-neoforge-1.21.1-21.1.0.14.jar` |

文件 URL 形如 `https://edge.forgecdn.net/files/8231/400/ftb-ultimine-neoforge-2101.1.15.jar`，替换为 `mod.mcimirror.top` 即国内镜像下载。

**结论**：FTB 系列（Ultimine/Library/Ranks）等 CF 独占 mod **无需 override 硬编码**，由 MCIM CF 镜像动态解析。原讨论的 `cf-mods-override.json` 方案不再需要。

---

## 6. 配置变更

### 6.1 `appsettings.json`（diff）

```diff
 {
   "MicrosoftAuth": {
-    "ClientId": "",
+    "ClientId": "00000000402b5328",
     "Tenant": "consumers",
     "Scopes": [ "XboxLive.signin", "offline_access" ]
   },
-  "CurseForge": { "ApiKey": "", "UserAgent": "FireflyMC-Launcher" },
+  "CurseForge": { "UserAgent": "FireflyMC-Launcher/1.0.0 (https://github.com/Sakura520222/FireflyMC-Launcher)" },
   "SelfUpdate": {
     "ReleasesApi": "https://api.github.com/repos/Sakura520222/FireflyMC-Launcher/releases",
     "Channel": "stable",
     "PublicKey": ""
   },
   ...
   "Mirrors": {
+    "ModrinthApiPrimary": "https://api.modrinth.com",
+    "ModrinthApiMirror": "https://mod.mcimirror.top/modrinth",
+    "ModrinthCdnPrimary": "https://cdn.modrinth.com",
+    "ModrinthCdnMirror": "https://mod.mcimirror.top",
+    "CurseForgeApiMirror": "https://mod.mcimirror.top/curseforge",
+    "CurseForgeFileCdn": "https://edge.forgecdn.net",
+    "CurseForgeFileMirror": "https://mod.mcimirror.top",
     "MinecraftPrimary": "https://piston-meta.mojang.com",
     ...
   },
   "Update": {
+    "McimStaleThresholdDays": 7,
     ...
   }
 }
```

说明：
- `CurseForge.ApiKey` 移除（走 MCIM 免 key）。
- `CurseForge.UserAgent` 字段可保留作 CF 官方未来启用时的 UA，或并入全局 UA。
- 全局 UA 建议提到根级（如 `"UserAgent": "FireflyMC-Launcher/1.0.0 (...)"`），供所有 HTTP 客户端（Modrinth/CurseForge/GitHub/FireflyApi）统一使用。

### 6.2 `java-spec.json`

```diff
 {
   "vendor": "eclipse",
   "major": 21,
   "runtimeVersion": "21.0.8+9",
   "architecture": "x64",
   "imageType": "jre",
-  "sha256": "",
   "url": "https://github.com/adoptium/temurin21-binaries/releases/download/jdk-21.0.8%2B9/OpenJDK21U-jre_x64_windows_hotspot_21.0.8_9.zip"
 }
```

移除 `sha256`。`AdoptiumJavaRuntimeInstaller` 不再校验，仅下载 + 解压。

---

## 7. 代码变更清单

| 文件 | 变更 |
|---|---|
| `Infrastructure/Platforms/ModrinthClient.cs` | 增加官方→MCIM 镜像回退：API 端 `api.modrinth.com`↔`mod.mcimirror.top/modrinth`；文件 URL `cdn.modrinth.com`→`mod.mcimirror.top` 替换；检查 `sync_at` 新鲜度 |
| `Infrastructure/Platforms/CurseForgeClient.cs` | **改为走 MCIM CF API（免 key）**：端点 `mod.mcimirror.top/curseforge/v1/...`；查询**不带 `modLoaderType`**，客户端按 `gameVersions` 过滤 `1.21.1`+`NeoForge`；文件 URL `edge.forgecdn.net`→`mod.mcimirror.top` |
| `Infrastructure/Platforms/IModPlatformClient.cs` | 接口不变（已抽象），增加镜像回退由实现内部处理 |
| `Infrastructure/Download/MirrorRouter.cs` | 增加 Modrinth/CF 的 URL 主机替换规则表（§3 表） |
| `Infrastructure/Minecraft/AdoptiumJavaRuntimeInstaller.cs` | 移除 SHA-256 校验调用（`IHashVerifier` 不再对 Java 调用） |
| `App.xaml.cs` / DI 装配 | 配置全局 `UserAgent`（从程序集版本 + 仓库 URL 拼接），注入所有类型化 `HttpClient` |
| `Configuration/LauncherConfiguration.cs` | 增加 `ModrinthApiMirror`/`CurseForgeApiMirror`/`McimStaleThresholdDays` 等绑定字段 |
| `appsettings.json` / `java-spec.json` | 按 §6 更新 |

**client_id 无需改代码**：`MicrosoftOAuthClient` 本就从 `MicrosoftAuthOptions.ClientId` 读取，只需配置填值。

---

## 8. User-Agent（提交 MCIM 白名单用）

**提交给 MCIM 的完整 UA**：
```
FireflyMC-Launcher/1.0.0 (https://github.com/Sakura520222/FireflyMC-Launcher)
```

- 提交入口：[mcim-api 接入说明](https://github.com/mcmod-info-mirror/mcim-api)（UA 白名单）。
- 未加白名单前，MCIM 可能随时拒绝请求——**发布前必须完成提交**。
- 版本号会随发布变化，建议提交时说明"版本号随发布更新，应用名 `FireflyMC-Launcher` 固定"，或按 MCIM 要求提交当前版本。

---

## 9. 待办

| 项 | 责任 | 阻塞 |
|---|---|---|
| 向 MCIM 提交 UA 白名单 | 用户 | 发布前（否则 MCIM 镜像不可用，降级链第 2/3 层失效） |
| 落地 §7 代码变更 | 开发 | 打包前 |
| 同步更新原 spec（决策 #4/#7、Java 校验条款） | 开发 | 可选（本文已记录修订，spec 可附本文引用） |
| client_id `00000000402b5328` 的合规确认 | 用户 | 发布前（此为 Mojang/微软公开 ID，社区广泛使用，但非自有应用身份） |

---

## 10. 参考资源

- MCIM 镜像：https://github.com/mcmod-info-mirror/mcim-api
- MCIM 文档/试用：https://www.mcimirror.top/
- Modrinth API（UA 规范）：https://docs.modrinth.com/api/
- CurseForge API（官方，需 key）：https://docs.curseforge.com/rest-api/
- BMCLAPI：https://bmclapidoc.bangbang93.com/
- 原设计文档：[2026-06-26-fireflymc-launcher-design.md](2026-06-26-fireflymc-launcher-design.md)
