# FireflyMC Launcher 执行状态与后续工作

- **日期**：2026-06-27
- **状态**：进行中（C 方案：暂时只支持离线账号）
- **关联**：[2026-06-26 初始设计](2026-06-26-fireflymc-launcher-design.md)、[2026-06-27 下载源修订](2026-06-27-mod-download-sources-and-config-revisions.md)

---

## 1. 当前关键决策

### 认证：C 方案 —— 暂时只支持离线账号

**原因**：原定 client_id `00000000402b5328`（Minecraft 官方 launcher 公开 ID）实测**不可用**，Microsoft 登录整条链跑不通。2026-06-27 实测三个端点全失败：

| 端点 | 结果 |
|---|---|
| v2 `consumers` | `unauthorized_client` (AADSTS70016: 应用不存在) |
| v2 `common`/`organizations` | `no tenant-identifying info` (AADSTS50059) |
| 旧 `login.live.com/oauth20_device_code.ep` | `404 Not Found`（端点已弃用） |

该 ID 是旧 Xbox Live 时代的，Microsoft 已不认。**这是基于过时信息的错误推荐。**

**C 方案落地**：账号页只保留离线登录入口，Microsoft 登录代码保留（未来恢复用），UI 暂不暴露。

---

## 2. 本次修复的启动崩溃（NullReferenceException）

**现象**：启动崩在 `LauncherConfiguration.cs:99` `LauncherUserAgent.Create`。

**根因**：`Create` 第 99-101 行原逻辑
```csharp
var configured = string.IsNullOrWhiteSpace(configuration.UserAgent)
    ? configuration.CurseForge.UserAgent   // ← CurseForge 为 null 时崩
    : configuration.UserAgent;
```
`configuration.CurseForge` 在反序列化后为 null。最可能原因：`JsonContext` 未显式注册 `CurseForgeOptions`（及 `MicrosoftAuthOptions`/`SelfUpdateOptions`/`GameOptions`/`MirrorOptions`/`UpdateOptions`/`FireflyApiOptions`）子类型，System.Text.Json source generator 反序列化时该属性被置 null，覆盖了 `= new()` 默认值。

**已修复**：加 null 条件访问 `configuration.CurseForge?.UserAgent`，任一来源为空则走 fallback。止崩。

**建议的根因修复（待做）**：在 `JsonContext` 显式注册各 Options 类型，避免其他属性（SelfUpdate/Game/Mirrors 等）也潜在为 null 导致后续别的 NullRef：
```csharp
[JsonSerializable(typeof(LauncherConfiguration))]
[JsonSerializable(typeof(MicrosoftAuthOptions))]
[JsonSerializable(typeof(CurseForgeOptions))]
[JsonSerializable(typeof(SelfUpdateOptions))]
[JsonSerializable(typeof(GameOptions))]
[JsonSerializable(typeof(MirrorOptions))]
[JsonSerializable(typeof(UpdateOptions))]
[JsonSerializable(typeof(FireflyApiOptions))]
// ...其余不变
```

**验证**：修复后须 `dotnet build` + 实际运行启动器确认不再崩。

---

## 3. C 方案落地清单

| 项 | 状态 | 说明 |
|---|---|---|
| 修 UA NullRef 崩溃 | ✅ 已完成 | `CurseForge?.UserAgent` null 防御 |
| JsonContext 注册 Options 类型 + Load 默认值归并 | ✅ 已完成 | 根因修复（2026-06-27 落地） |
| 账号页隐藏"添加 Microsoft 账号" | ✅ 已完成 | C 方案 UI |
| ClientId 默认改空 | ✅ 已完成 | 不再用已验证不可用的 `00000000402b5328` |
| Firefly API 契约修复（`PackModsResponse`） | ✅ 已完成 | `/api/pack/mods` 实际是 `{importedAt,count,mods}`，正确读 156 mod |
| **`gm.rainplay.cn` online-mode** | ✅ 已确认离线 | 协议探测：Login Start 后无 Encryption Request（收到 Set Compression），指向 `online-mode=false`，不阻挡离线。端到端时需实际连入最终确认 |
| 端到端：离线账号→安装→启动→连服 | ⏳ 待做 | **唯一剩余硬阻塞**，发布前必须跑通 |

---

## 4. 整体发布就绪状态

### ✅ 已完成且验证
- 代码 build 0 警告 0 错误，单元测试 16/16
- 配置占位已填：UA、Ed25519 公钥、MCIM 镜像端点
- 移除 CF ApiKey 与 Java SHA-256 校验
- mod 下载降级链（Modrinth→Modrinth MCIM→CurseForge MCIM，免 key）实测可行
- Ed25519 验签（纯 C#）向量测试通过
- 打包脚本 `tools/package_release.py` 端到端验证（产出 90MB zip + sig）
- MCIM UA 白名单已提交；私钥移至仓库外
- **JsonContext 注册 Options + Load 默认值归并**（UA 崩溃根因修复，实际运行无 crash）
- **Firefly API 契约修复**（`PackModsResponse` 正确读 156 mod 清单）
- C 方案 UI（账号页隐藏 MS 登录）；ClientId 默认改空
- `gm.rainplay.cn` 协议探测确认离线路径（不阻挡离线账号）

### ⏳ 阻塞发布（仅剩端到端验证）
1. **端到端 checklist**（spec §8.4）从未在真实环境完整跑过：
   - 完整安装（Java→MC→NeoForge→mods→Firefly mod）
   - 启动 MC 进游戏
   - 自动连服（协议探测已指向离线，需实际连入最终确认）
   - 自更新真实链路（下载→Updater→替换→健康确认→回滚）
   - 断网离线启动、日志脱敏

### ⚠️ 已知风险
- **NeoForge installer** 流程复杂（install_profile.json + processors + maven），实现正确性未端到端验证
- **MCIM 缓存延迟**：`sync_at` header 实测常缺失，陈旧检查可能失效（拿到旧 mod 版本）
- **测试覆盖偏少**（16 个）：spec §8.2 最高风险的事务崩溃恢复、启动参数 `--accessToken` 脱敏、认证全链未覆盖

---

## 5. Microsoft 登录恢复方向（后续，非本次）

C 方案是权宜。要恢复正版登录，未来可选：
- **A. 个人账号注册 Entra app**（最干净）：若"租户注销"仅指组织租户，个人 Microsoft 账号（@outlook/@hotmail）在 [portal.azure.com](https://portal.azure.com) 可注册 Public client，拿自有 client_id。
- **B. 社区启动器公开的 client_id**：如 PrismLauncher 等在源码公开的 consumers app ID（合规风险同前述）。
- **C.（当前）只离线**。

---

## 6. 下一步（截至 2026-06-27）

1–4 已完成（UA 修复 / JsonContext 根因 / C 方案 UI / online-mode 确认）。
5. **端到端跑一次**（唯一剩余硬阻塞）：离线账号 → 一键安装（Java→MC→NeoForge→156 mod→Firefly mod）→ 启动 → 实际连入 `gm.rainplay.cn:32772` 确认能进。
6. 端到端通过后即可首次发布（玩家手动下载 zip）。
7. 后续：补 spec §8.2 高风险测试（事务崩溃恢复 / 启动参数脱敏）、整理 git 历史、考虑 MS 登录恢复（§5）。
