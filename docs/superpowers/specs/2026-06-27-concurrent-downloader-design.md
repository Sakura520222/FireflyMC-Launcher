# 多线程下载（文件间并发）设计

- **日期**：2026-06-27
- **状态**：已确认（实施方案 1）
- **关联**：[初始设计](2026-06-26-fireflymc-launcher-design.md)、[执行状态](2026-06-27-execution-status.md)

---

## 1. 背景与目标

当前 [HttpDownloader](../../src/FireflyMC.Launcher/Infrastructure/Download/HttpDownloader.cs) 是单连接下载（含断点续传/重试，**保留不变**），而调用方对**多文件**采用 `foreach` 逐个串行：

- [ModPackUpdateService.SyncAsync](../../src/FireflyMC.Launcher/Services/Update/ModPackUpdateService.cs)：156 个 mod 串行
- [McVersionInstaller](../../src/FireflyMC.Launcher/Infrastructure/Minecraft/McVersionInstaller.cs)：assets（常上千小文件）+ libraries 串行

**目标**：把这些多文件场景改为**文件间并发**（最多 8 路同时下载），消除串行等待。**单文件分段（HTTP Range 多连接）不在本次范围**——大文件（Java zip/client.jar）保持单连接续传。

**非目标（YAGNI）**：Firefly mod（单个文件）、自更新发布包（单文件）不做并发。

---

## 2. 方案：共享并发下载原语 `ConcurrentDownloader`

新建 [Infrastructure/Download/ConcurrentDownloader.cs](../../src/FireflyMC.Launcher/Infrastructure/Download/ConcurrentDownloader.cs)，封装"并发下载 + 字节级进度聚合 + fast-fail + 可选 SHA 校验"，两个调用方复用。

### 2.1 数据契约

```csharp
public sealed record ConcurrentDownloadJob(
    Uri Uri,
    string DestinationPath,
    string Label,            // 进度显示与结果索引（mod=RelativePath，assets=hash/文件名）
    bool Required,           // true：失败取消其他并中止；false：失败跳过
    string? ExpectedSha1,    // 非 null：下载后校验，不匹配按 Required 处理
    bool RecordActualSha1);  // true：下载成功后 ComputeSha1Async 记录（mod 事务需要）

public sealed record DownloadedFile(string Path, string? ActualSha1);
public sealed record ConcurrentDownloadResult(
    IReadOnlyDictionary<string, DownloadedFile> Files,  // 成功的 label -> 文件
    int SkippedCount);                                   // 可选失败/校验失败跳过数
```

### 2.2 行为约束（不变量）

| 约束 | 实现 |
|---|---|
| 并发上限 | `SemaphoreSlim(maxConcurrency)`，默认 `UpdateOptions.MaxConcurrentDownloads=8` |
| 每文件续传/重试 | 复用现有 `HttpDownloader.DownloadAsync`（resume=true）—— 单文件行为零变化 |
| **required fast-fail** | required 下载/校验失败 → `linkedCts.Cancel()` 取消在途任务 → 抛出（spec §5.4 中止+回滚语义） |
| 可选失败 | `SkippedCount++`，事务继续（spec §5.4 可选文件跳过） |
| 外部取消 | `LauncherOperationCoordinator` 的 `cancellationToken` 传播到所有任务 |
| 脱敏 | 进度 `CurrentItem` 只用 Label（相对路径/文件名），不含 token/URL 凭据 |

### 2.3 进度聚合（`ProgressAggregator`，per-call inner class）

并发后单文件名无意义，改为：

- `CurrentItem` = `下载中 {已完成数}/{总数}`
- `StagePercent` = 字节聚合 `Σcompleted / Σtotal`（已知 Content-Length 的 job 求和），total 未知时回退 `已完成文件数/总数`
- `OverallPercent` = `overallBase + overallSpan × StagePercent/100`（mod 场景 7+70；assets/libraries 场景传 null，不算 overall）
- `CompletedBytes`/`TotalBytes` = 各 job 求和；`BytesPerSecond` = 聚合字节增量/时间增量
- 节流 200ms（用 lock 保护 per-call 状态，不用 `Progress<T>` 避免 SynchronizationContext 陷阱）

每个 job 的 `HttpDownloader` 进度回调经一个轻量 `ActionProgress<StageProgress>` 更新自己在聚合器里的 `(completed, total)`；job 成功后 `IncrementCompleted()`。

---

## 3. 调用方改造

### 3.1 `ModPackUpdateService.SyncAsync`（mod）

1. 先处理 `about:blank`（解析失败的占位）：required 的直接 throw（保持现状），可选的过滤掉——**不进并发队列**。
2. 剩余 download 构造 `ConcurrentDownloadJob`（`ExpectedSha1`=非空 sha1、`RecordActualSha1=true`、`Required`=mod.Required）。
3. `ConcurrentDownloader.DownloadAllAsync(jobs, maxConcurrency, OperationStage.Stage, overallBase:7, overallSpan:70, ...)`。
4. 用结果填充 `stagedRelativePaths`/`stagedSha1`（跳过的不进字典）。
5. Firefly mod **保持单文件**下载（不变）。
6. 事务 Commit 逻辑不变（`augmentedDownloads` 按 `stagedRelativePaths` 过滤）。

### 3.2 `McVersionInstaller`

- **assets**：先收集所有 `(hash, targetPath)` → 构造 job（`Required=true`、`ExpectedSha1=null`、`RecordActualSha1=false`）→ 并发下载。
- **libraries**：先遍历 `libraries` 数组收集允许当前 OS 的 artifact 与 native 的 `(url, targetPath)` → 并发下载 → native 路径收集后统一解压（解压保持串行，量小且快）。
- 这两处的进度 `overallBase/Span` 传 null（McVersionInstaller 不计 overall）。

---

## 4. 配置

`UpdateOptions` 增 `MaxConcurrentDownloads`（默认 8），写入 `appsettings.json` 的 `Update` 段。与 `MaxRetries`/`PerFileTimeoutSeconds` 同处，运行时可调。

---

## 5. DI

`App.xaml.cs` 注册 `ConcurrentDownloader` 为单例（注入 `IDownloader`/`IHashVerifier`/`IDiagnosticLogger`）。`ModPackUpdateService` 与 `McVersionInstaller` 各自注入 `ConcurrentDownloader`。

---

## 6. 错误处理 / 边界

- **某 job 抛 required 异常时，其他 job 正在 `semaphore.WaitAsync` 或读流**：`linkedCts.Cancel()` 让等待者抛 `OperationCanceledException`、读流者中断；`Task.WhenAll` 汇总，调用方拿到第一个 required 异常。
- **外部取消**（用户点取消）：`cancellationToken` 触发，所有 job 中断，`Task.WhenAll` 抛 `OperationCanceledException` 冒泡到 `Coordinator`。
- **可选 job 全失败**：`SkippedCount` 高但事务继续；仅当 required 失败才中止。
- **断点续传不变**：每个 job 独立 `.part` 文件，并发不互相干扰；中断后下次 `RecoverAsync` 清 staging 重下。

---

## 7. 测试

- 复用 [UpdateTransactionTests](../../test/FireflyMC.Launcher.Tests/Update/UpdateTransactionTests.cs) 风格，用 `NullDiagnosticLogger`。
- 新增 `ConcurrentDownloaderTests`：
  - 多文件全部成功 → `Files` 含全部 label
  - 一个 required 失败 → 抛异常、其他被取消（用 fake `IDownloader` 注入受控失败）
  - 一个可选失败 → `SkippedCount=1`、其他成功
  - 并发度限制：fake `IDownloader` 记录最大并发调用数 ≤ maxConcurrency
  - 外部取消 → `OperationCanceledException`
- 现有 16 个测试保持通过（构造函数新增 `ConcurrentDownloader` 参数需同步用 `NullDiagnosticLogger` 风格补齐）。

---

## 8. 验证

- `dotnet build` 0 警告 0 错误
- `dotnet test` 全绿
- 手动（可选）：实际跑一次整合包更新，对比并发前后耗时；观察进度条平滑推进、`CurrentItem` 显示「下载中 N/M」
