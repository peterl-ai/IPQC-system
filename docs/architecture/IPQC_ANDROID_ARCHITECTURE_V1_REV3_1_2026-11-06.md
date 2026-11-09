# JAX Power IPQC Android Architecture v1 Rev3.1

日期：2026-11-06，America/New_York。

修订范围：依据 GPT Independent Review 仅补充 F1（§8.1）、F2（§9.1）、F3（§11.5）及其验收（§15.1）。Rev3 的业务政策、技术选择、72 小时简化、五年自动删除和首版范围不变；等待三项独立复核与 Peter 最终签核。

状态：Staff Engineer 已根据 Peter 最新授权收敛的架构基线，供 ChatGPT 独立复核和 Peter 最终核定。业务决定按本版执行解释；尚未授权代码、迁移、分支、PR 或部署。

本版是完整架构稿。它取代 Rev2 中的五年到期人工审批、V1 业务 hold 管理、复杂离线可信时钟要求，以及已经由本版选定的技术开放项。Rev2 其余已确认业务规则完整保留。仓库当前实现仍是 Web/API v1，不能将本文内容当成已经实现。

## 1 本次收敛决定

| 事项 | Rev3 最终选择 |
| --- | --- |
| 72 小时 | 保留最后成功在线验证后最多 72 小时的普通离线会话；允许同一用户正常杀进程和重启后恢复。采用常规时间检查，不建设硬件可信时间、反 root 或强防篡改系统。异常回拨要求联网重验，工作保留。 |
| 五年到期 | 每次正式 Submit 起算五个日历年。服务器每小时自动检查并删除符合条件的照片文件，无人工候选审批或删除批次签核。停机后恢复自动补清理。 |
| 删除审计 | 保留最小必要的自动删除记录、照片元数据和原提交引用，历史显示照片已按保留政策删除。无业务审批流程。 |
| 时间口径 | SubmitAtUtc.AddYears(5)，UTC 保存与比较；2 月 29 日按对应第五年的 2 月 28 日处理。界面继续按厂区时区显示。 |
| 业务例外 | V1 不建设法律／客户 hold 申请、审批、冻结或期限配置工作流。若业务邮件提出必须保留例外，作为明确的新需求评估，不能默默加入 V1。 |
| 样本特殊状态 | 无数值 NG 和 Removed 使用独立 disposition；不会生成假测量样本，也不会清零已经形成的复检样本要求。 |
| Reference Images | 有任何任务／提交历史依赖的参考图版本持续保留；不套用证据照片五年清理规则。 |
| 设备认证 | Keystore 密钥对、Admin 注册和产线绑定、mTLS 设备身份、个人 JWT。使用独立 Native TLS 入口连接同一后端，保留 Web same-origin。 |
| 开发方式 | 继续现有 monorepo；扩展现有领域服务和历史，不重写整个 Web/API，不增加 MES、通知和首版自动更新。 |

Peter 已将上述工程选择交由 Staff Engineer 决定，不再作为需要产品负责人逐项选择的技术问卷。

## 2 产品与现有系统基线

目标是两条生产线各一台 Samsung Galaxy Tab A7 SM-T500、Android 11 平板，Kotlin、English-only。个人 IPQA 在线登录后，可以接续同一 PAD／产线上的工作。Home 包含 Patrol 与不可执行的 Material Verification 预留入口。

服务器按 America/New_York 每条线每日 07:00 和 19:00 生成任务，分别冻结 Day／Night；约 28 个项目不是硬编码限制。任务冻结 Standard 定义，之后修改 Standard 不改变已有任务。

只支持已经下载的任务离线输入、保存和照片。新任务仍由服务器创建，未下载任务等联网后取得。草稿和照片自动同步；每次正式 Submit 必须是 IPQA 明确的在线操作，并经服务器确认。

只读核验基线为 main 86f4d817ac68dfafebaaef341f7f2a5bbaedb73b。本地与 GitHub main 在 2026-11-06 评审中一致。当前代码仍是个人指派、N/A、全部提交 PendingApproval、无完整附件和 Android 的实现。新能力必须按本版实施和测试。

## 3 技术栈与组件

| 层 | 选择 |
| --- | --- |
| Android UI | Kotlin、Jetpack Compose、Material 3、Navigation 3、screen ViewModel、StateFlow；English 资源文件统一管理 |
| Android 数据 | Repository、Room、Coroutines；先用单 app module 和清晰包边界，不建立大量 Gradle modules |
| Android 网络 | Retrofit、OkHttp；个人 JWT 与设备 mTLS；十进制值使用 BigDecimal／精确序列化 |
| 图片 | CameraX；系统相册选择器和 Android 11 回退；Coil 显示、缩放 |
| 持久后台工作 | WorkManager；只同步草稿、参考缓存和照片，不执行正式 Submit |
| 偏好与凭证 | DataStore 保存非敏感设置；Keystore 保护密钥及 refresh token 密文；不保存密码 |
| API | 现有 ASP.NET Core 10、领域服务、服务器权限和排程 |
| 数据库 | 生产专用 PostgreSQL；SQLite 用于开发／适合的自动化测试；关键并发另测 PostgreSQL |
| Web | 保留 Vue 3／TypeScript／Ant Design Vue／vue-i18n，English 与简体中文 |
| 文件 | 同一 IPQC 服务器的独立可扩容照片数据卷，初始规划 1 TB，通过本地文件 storage adapter 访问 |

Android minSdk 30；compileSdk／targetSdk 在工程建立时固定为兼容的稳定版本。所有库固定兼容的稳定版本，不使用动态版本或为了追新采用 alpha。

UI 支持横屏双栏项目列表与详情，竖屏保持完整功能，不锁定方向。Patrol Items 保留 Basic Information 和始终可达的 Save Draft／Submit 按钮，状态不能仅靠颜色表达。

## 4 设备身份与用户会话

### 4.1 设备注册

1. App 在 Android Keystore 生成不可导出私钥，并发起待注册请求，证明持有对应私钥。
2. Admin 在 Web 核对该 PAD 的配对请求，选择稳定 LineId 后注册。配对凭证短期、一次性使用，不能授予业务执行权限。
3. 已注册公钥取得受信的客户端证书。签发密钥由部署秘密／IT 管理，不进 Git 或照片目录。
4. Native 请求经 mTLS 验证该设备密钥，再结合个人账号完成授权。私人证书和 JWT 都不能单独授予跨线执行权限。
5. Admin 可撤销设备或更换绑定；DeviceRegistry 保存状态、证书／公钥指纹、BindingVersion 和审计记录。

Native 使用同一物理服务器的独立 TLS 主机名或端口入口，避免在 Web 同一 TLS 连接中按路径临时要求证书。Web 页面与其 API 继续同源。注册引导入口只处理配对，不开放未注册设备的任务执行。

反向代理如果终止 mTLS，必须验证证书、剥离客户端伪造的证书 header，并只向受信后端转发设备身份。应用每次仍检查设备注册和绑定状态，TLS 连接复用不能绕过撤销。

不依赖 IP、Android 显示名称或客户端自报 DeviceId 作为设备认证。硬件 backing 能力由实机验证，但不把 StrongBox 或完整设备 attestation 作为首版硬性依赖。

### 4.2 个人认证

复用现有本地账号、密码政策、锁定、Native login／refresh／change-password。首次／重置密码必须在线改密码并重新登录。Access token 只保存在内存；refresh token 使用 Keystore 金钥加密，排除系统备份；不保存密码。

增加稳定 NativeSessionId，绑定 UserId、DeviceId、AuthVersion、session 状态与绝对期限。在线业务请求检查账号、会话、设备、BindingVersion 和产线。退出会话在服务器撤销；离线退出先停止本机会话，保留业务工作，联网后按可验证状态处理服务器撤销。

Refresh 同时只允许一次在途请求。网络失败不当作账号失效；401／403 不无限重试。Refresh 已轮换但回包丢失时允许重新在线登录，不能因此清除草稿或照片。

### 4.3 72 小时的务实实现

服务器完整验证个人、会话和 PAD 绑定后，签发绑定 UserId／DeviceId／LineId／BindingVersion 的 OfflineGrant，包含 VerifiedAtUtc 和 ExpiresAtUtc。最大有效期为 72 小时，并不超过适用会话绝对期限。

同次开机使用服务器时间锚点与 elapsedRealtime 计算经过时间；重启后使用保存的验证锚点与 UTC 墙上时钟检查剩余时间。时间不因重启、打开页面、Save 或失败网络请求而重置。记录最近的有效时间进度；检测明显回拨／损坏时要求联网重新验证。

允许原用户在正常重启后恢复同线缓存工作。72 小时过期仅锁定访问和编辑，保留全部未同步资料，原用户或之后在线获准的同线用户可恢复。新登录、换用户、未知任务和 Submit 均要求在线。

这是公司常规非 root PAD 上的业务超时控制，不承诺对恶意改系统、恢复旧系统状态、改时钟再长期断电提供强防篡改保证。首版不因此要求 MDM 或安全时钟硬件。PAD 离线时无法即时收到服务器撤销；联网后在继续同步前重新验证。Peter 已接受以日常巡检场景为准的工程取舍。

## 5 排程与执行授权

复用现有 Daily selected times、DST、catch-up、禁止预生成未来任务和 Plan／Occurrence 唯一键。新 Plan 使用稳定 LineId 与 LineShared 模式；不通过假 IPQA 账号满足旧个人 assignee 限制。

新策略每条线有一个有效日常计划，07:00／19:00 的轮次配置包含 Shift。启用或修改时防止同线同轮次计划重叠；既有每 Plan occurrence 唯一键继续作为生成去重基础。任务生成时冻结 LineId、轮次、Shift、策略版本和 Standard 快照。

不同 IPQA 都可在正确注册 PAD 执行该线工作。执行请求的授权组合为有效 IPQA＋有效 Native session＋有效设备＋匹配产线。Web PQE／Admin 保持其审查权限；不能把设备限制粗暴加到所有 Web 请求。

## 6 表单与检查规则

Task Basic Information 包含可选 Customer、Production、Product MO，三者在 V1 为有长度限制的文本；Production 的业务重命名／结构化留给 V2。新 UI 不显示 Machine Code／Series／Mold，但历史值保留。

定性 Sample 使用 OK／NG；普通定量 Sample 包含可选 Barcode 和必填 Inspection Value。Barcode 允许重复，支持手工和外接 keyboard-wedge scanner，不做相机扫码。定量使用冻结上下限由服务器判定；客户端仅做输入帮助与预览。

NG Type、普通 NG Reason 和 Remarks 可选；无数值 NG 的 NG Reason 必填。PIC 可选多选，Admin 维护独立 active／inactive roster，含稳定 ID、姓名和部门；不要求登录账号。提交冻结历史姓名／部门，停用 roster 不破坏旧记录。首版只记录／显示，不发送通知。

### 6.1 样本与复检

首次普通检查至少 frozen Standard.SampleCount，空值默认 1；最多 50。每个 targeted reinspection item 的整个样本组都是新 attempt，最低数为 max(冻结最低数，上次适用正式实测数量／已延续要求)，最多 50。

复检必须录入新样本记录，不复制目标项目旧样本作为已完成。真实重新测量可以得到相同值，不能以数值不同作为判定新检查的条件。目标项目之外的 OK 结果保留来源归属并可沿用。

全部目标项目完成本次处理后才允许正式重提；未完成部分只保存草稿。完成本次处理包括正常检查、符合规则的无数值 NG，或经业务确认的 Removed。再次 NG 可以提交并生成下一版，不要求等待 PQE。

### 6.2 无数值 NG

项目存在但暂时无法测量时，IPQA 显式选择 UnableToMeasureNG，填写 NG Reason 并增加当前 attempt 新照片。此状态不制造 0 或假 Sample；普通定量仍必须有真实数值。

无数值 NG 和 Removed 不把已形成的复检样本最低数归零。恢复正常测量后继续执行较高的适用要求。例如最低 3、上次实测 5，之后一版无数值 NG，再次测量仍至少 5。

### 6.3 Removed

Production／EQ 确认项目确实不存在或不适用后，IPQA 逐项标记 Removed；原因可选，保留 actor、动作和定义，不物理删除。V1 没有批量全部移除动作。

移除最后一个正常项目时显示警告，但允许该动作及后续在线 Submit。所有项目 Removed 的正式提交使用 NotPerformed outcome，绝不能因为没有 NG 就计算为 Qualified。原本没有任何项目快照的损坏任务不属于此正常全 Removed 场景。

## 7 状态机与任务分组

| 事件 | Task 状态 | Android tab／显示 |
| --- | --- | --- |
| 新任务／首次保存 | PendingInspection／InProgress | Pending |
| 正式提交存在 NG | ReinspectionRequired | Re-inspection，NG |
| 复检再次正式提交仍 NG | ReinspectionRequired，新增 revision | Re-inspection |
| 有正常项目且全部通过后 Submit | PendingApproval | Completed，Pending Approval |
| PQE 批准所见 revision | Completed | Completed，Approved |
| PQE 原因退回并指定项目 | Rejected | Re-inspection，Rejected by PQE |
| 全项目 Removed 后正式 Submit | PendingNotPerformedConfirmation | Completed，Pending PQE Not-Performed Confirmation |
| PQE 特殊确认 | NotPerformedConfirmed | Completed，Not Performed - PQE Confirmed |
| PQE 拒绝特殊确认并指定恢复项目 | Rejected | Re-inspection |

草稿改变结果不替代正式 Submit；未提交的 NG→OK 仍留复检组。部分 Removed、其余正常项目全部 OK 进入普通 PendingApproval，PQE 可见移除明细。

NG 自动路由不是 PQE Reject，不生成假 reviewer。PQE 可立即只读查看已提交 NG revision／照片。普通批准不能确认全 Removed；特殊确认使用独立 decision，终态不可继续编辑。

每次提交完整冻结项目 disposition、样本、Task header、PIC、证据集合、贡献来源、提交者与服务器 Submit 时间。Inspector 使用对应最终正式提交者。业务检查时间使用各次正式 Submit 时间，不新增人工设备测量时间输入。

PQE Approve／Reject／特殊确认／batch 必须绑定所见 SubmissionId 或 expected revision。Reject 必须有原因和非空、有效的项目集合，可以选原 OK 或 Removed 项目。所选项目进入新 attempt；未选 OK 结果保持。新策略先不建设独立 header-only 退回流程，header 可在获准返工期间修正并随下一次提交冻结。

## 8 本机持久化与多人归属

Room 保存 Task snapshot、draft、样本、PIC cache、reference manifest、操作记录及 outbox；图片字节放 app-private persistent storage，不只保存外部 URI。

共享业务空间键为 Environment＋RegisteredDevice＋LineId＋TaskId。个人 token／OfflineGrant 单独管理，不能继续把同线工作永久锁在第一个 UserId 下。

每笔编辑关联此前服务器签发的个人上下文，并保留原作者、上传者和最终提交者。A 离线编辑、B 在线登录后上传接续时，不把 A 的贡献改记 B。服务器只接受可关联的既有上下文，不能由请求任意指定作者。离线历史作者依赖受控 App 记录，不冒称服务器逐笔在线见证。

保存成功的 UI 必须对应完成本机持久写入。图片写临时文件、完成后原子更名，再提交 metadata；启动时核对未完成文件／metadata。进程退出、重启、授权过期和网络失败不能清空 outbox。

按 task 串行同步。CurrentRevisionNo 是正式版本，DraftVersion 是服务器可变草稿版本，LocalEditVersion 是本机编辑确认进度，三者不混用。同步成功确认到版本 20，不能把后来产生的 21 标成已同步。

草稿请求带 expected DraftVersion 和稳定 OperationId，服务器原子条件更新。冲突保留本地与服务器内容；页面显示双方摘要，由当前在线操作者选择重载服务器或在最新版本上重新应用本地修改。没有静默 last-write-wins，也不建设通用复杂字段合并器。

WorkManager 和前台同步共用同一 coordinator。联网先重新授权，再取得当前 task／draft／attempt 状态，顺序重放尚未确认操作及照片。换人时停止旧 worker 使用旧凭证，由新获准用户处理共享队列，原始作者不变。

### 8.1 F1 离线贡献归属与当前上传授权

本节明确 §4.2–4.3 和 §8 的历史归属与运行授权边界。离线记录不是凭过期 token 请求业务写入；B 的当前有效会话授权传输和本次提交，A 的历史上下文只证明记录来源。

每笔本机操作至少携带稳定 OperationId、TaskId／ItemId／AttemptId、CapturedContributorUserId、CapturingOfflineGrantId／ContributorContextId、DeviceId、LineId、BindingVersion、依赖操作及内容 hash。服务器保存和验证先前签发的设备绑定贡献上下文，从该上下文解析原贡献者，不信任请求任意填写的 UserId。照片 manifest 使用相同来源关系。CapturedContributorUserId、实际认证的 UploadedByUserId 和正式 SubmittedByUserId 分别记录；历史姓名按相应快照显示。

| 上传时情况 | 服务器处理 | App 恢复方式 |
| --- | --- | --- |
| A 的 grant／普通会话已经过期或 A 正常退出，但 A 仍为有效 IPQA，原贡献上下文未因安全原因撤销；设备、产线和绑定匹配 | 可由当前获准的 B 上传，仍记 A 为原贡献者、B 为上传者；不是延长 A 的离线编辑授权 | 接续同线工作，B 可在自己的明确在线 Submit 中提交 |
| A 已停用、失去 IPQA 执行资格，或该来源上下文被明确安全撤销／标记不可接受 | 不将相关未同步操作／照片应用到正式服务器草稿；返回来源冲突明细 | 保留本机记录并隔离相关项目；B 在自身有效上下文重新检查／拍照，生成独立新操作 |
| PAD 已撤销，或采集上下文的 LineId／BindingVersion 与当前授权不符 | 拒绝旧执行请求；不得借 B 的登录跨线同步 | 保留原工作及归属，等待同一原产线的获准恢复；不把旧工作自动迁入新产线 |
| Grant／贡献上下文缺失、未知、签名或内容关系无效 | 不接受声明作者或把作者改成 B；返回 ProvenanceConflict | 隔离并显示可恢复冲突；B 重新完成受影响检查，不能直接“认领”旧结果 |
| B 当前未获准、会话失效或不在匹配 PAD／产线 | 拒绝传输／写入，不依赖 A 的历史上下文补权限 | 保留工作，先完成正常在线认证 |

上述接受／隔离规则针对尚未被服务器接受的操作及其未验证依赖；不会追溯改写已经接受的草稿归属或不可变 submission。之后 A 停用，不会自动否定此前已接受、可合法沿用的历史结果，也不会把它们改记为 B。

正常 logout／时间到期与来源上下文的安全撤销分开：前者终止当前使用权，不自动删除历史来源；后者不能仅因换成 B 上传就被绕过。自动同步先提交来源／附件 manifest 供校验，未获准的照片字节不继续上传；设备级授权失败直接停止整个该设备队列。

冲突批次按事务原子处理，不返回部分已应用的模糊成功。App 保留原批次和回执，标记受影响项目及依赖它们的后续操作；不允许把隔离的 A 数据混入 B 的全量草稿来绕过校验。未受影响项目可按当前版本拆成新批次同步；B 重新完成的检查有新 OperationId／Attempt 记录，原隔离记录和照片保留至明确恢复或清理，不静默丢弃、不改写 A 为 B。

采集时限由已批准的普通离线 App 会话机制执行。本机时间是审计线索，不是防篡改证明；过期的历史上下文既不证明精确采集时间，也不授予新的离线操作或在线提交权限。

## 9 提交与审批幂等

Submit 是明确的前台动作：检查必填／目标范围、同步最新草稿、确认所需附件 Ready、请求服务器正式提交。服务器在一个数据库事务中检查策略、权限、DraftVersion、样本／证据，创建不可变 revision、证据保留引用、receipt 和新状态。

SubmissionAttemptId 标识一次稳定内容的用户提交；相同 ID 与相同内容重试返回原 receipt，不生成第二版。相同 ID 不同内容返回冲突。权限检查仍执行，receipt 查询不能泄露其他产线任务。

超时或回包丢失显示结果确认中，通过相同 attempt 查询／重试解决。先查已有 receipt，再决定是否是新操作，不能因 NG 已恢复可编辑而把重试当复检提交。自动同步不补交用户曾点击但尚未确认／未正式接受的 submission；需要前台用户重试并保持同一 attempt。

Review 使用 exact SubmissionId、expected revision 和稳定 review operation ID；并发只允许一个有效 decision。旧页面提交过期版本返回冲突，不审核新 revision。

### 9.1 F2 固定提交意图与自动同步屏障

一个 task 同时只能有一个活动的提交意图；前台与 WorkManager 共用 task coordinator。Submit 使用 Preparing → Sealed → InFlight → Accepted／Conflict／OutcomeUnknown 状态；这些是本机请求状态，不是新的业务 Task 状态。

1. IPQA 在线明确点击 Submit 时，持久记录 SubmissionAttemptId、请求操作者 UserId／NativeSessionId、DeviceId／BindingVersion、BaseRevisionNo 和当前 LocalEditVersion，冻结这一版本的业务输入与照片文件 manifest，进入 Preparing。立即暂停该 task 的新编辑、账号接续写入及超过该本机版本的后台重放，屏障状态持久化。
2. Coordinator 等待既有在途请求得到确定结果，按顺序同步到冻结版本；只准备该版本的照片，并等待所选每张照片取得匹配 hash 的 AttachmentId／Ready 和持久 draft 关联。此时不发送正式 Submit，也不把业务输入替换成后续下载内容。发现服务器变化／来源冲突时保留意图并返回冲突。
3. 准备完成后一次性写入 Sealed intent：精确 ExpectedDraftVersion、BaseRevisionNo、完整 header／item disposition／sample／来源／PIC 内容、证据 manifest（ItemId、InspectionAttemptId、ClientPhotoId、AttachmentId、实际字节 SHA-256）、CanonicalizationVersion、CanonicalContentHash 和同一 SubmissionAttemptId。Sealed 后全部字段不可变；不能在重试前重新读取“最新草稿”重建 payload。
4. 只有当前仍有效的原请求操作者、在其连续的前台 Submit 动作中，才能把 Sealed intent 发送为正式提交。准备期间断网／退出／进程终止，返回后只能自动恢复状态、同步准备材料或查询回执；自动 worker 不发送正式 Submit。用户在线明确重试时才继续。
5. Sealed／InFlight／OutcomeUnknown 时，本 task 保持只读，避免并发 Save／照片／Remove 改变本次内容。后来到达的照片 Ready 回执只更新相应文件传输状态，不自动加入冻结 manifest；后续编辑只能在旧意图已完成／明确冲突或安全取消后成为新的本机版本。已经发出但结果未知的意图不能被简单取消／替换为新 attempt。

CanonicalContentHash 由版本化的统一规范化规则产生：固定字段、项目／样本／照片顺序、精确十进制文本、null／空值规则，覆盖操作者绑定、TaskId、BaseRevisionNo、ExpectedDraftVersion 和全部业务／证据内容；服务器按同一规则重算，不能只信客户端给出的 hash。业务输入在 Preparing 即冻结；远端版本和附件 ID 仅在准备确认后一次性封入 Sealed intent，不能留下随重试变化的字段。

服务器在主库事务中执行以下合同：

- 唯一键为 (TaskId, SubmissionAttemptId)。当前访问授权始终先检查；已提交的同一身份绑定／hash 请求返回原 receipt，不因当前任务已经进入下一状态而再建 revision。相同 ID 携带不同 hash／操作者绑定返回 409 IntentMismatch。
- 首次接受时原子校验 ExpectedDraftVersion、BaseRevisionNo、当前工作流状态与 item attempt、新样本、来源和所需 Ready 证据；创建完整 immutable submission、证据引用、receipt 并切换状态，一起 commit 或 rollback。
- 每次成功 Submit 增加 CurrentRevisionNo，并推进服务器写入版本；Review／其他改变编辑资格或返工范围的动作也推进相应写入版本。旧 intent 即使在 NG 后任务仍可编辑，也不能再作为下一版提交。409 DraftVersionConflict 不修改原 payload；需要新操作时创建新 intent。
- Receipt 返回精确 SubmissionId／RevisionNo、AcceptedAtUtc、ContentHash、SubmittedByUserId 和结果。自动 Save／Upload 无权创建这些记录。

超时进入 OutcomeUnknown，先查同一 attempt 的权威回执；同一操作者显式重试仍发送完全相同的 Sealed intent。一次 NotFound 不能证明旧请求从未在途，不能仅凭它显示失败或创建下一版。Task version／BaseRevision 的原子条件保证两个竞争请求不能同时接受同一基础状态。

原操作者 A 再次在线登录后，可以由同一 UserId、同一 DeviceId／BindingVersion 的新有效会话传输原 Sealed intent，前提是当前授权及来源校验通过；原 NativeSessionId 保留为请求来源审计，不要求重用已过期 token，也不因此改变 intent／hash。此规则不允许 B 冒充 A。

A／B 交接期间，B 可以在正常同线权限下查询 A 的 receipt，结果仍显示真实提交者 A。B 不能用自己的 token 重发为 A 的 Submit。若需要由 B 接续正式提交，必须由 B 再次明确点击、形成 B 的新 intent，并核对当前草稿／基础 revision；原 A 的迟到请求与 B 的新请求受同一版本条件互斥，冲突方重新查询实际结果。不得把 A 的旧 intent 改名成 B，也不得自动生成 B 的下一轮 NG 复检提交。

Preparing 尚未发出正式请求时可明确取消并保留草稿；版本冲突后的新业务修改创建新 attempt。Accepted 前没有 Submitted／Approved 成功提示；OutcomeUnknown 在重启后继续保持并优先核对，不因 UI 恢复而清空请求身份。

## 10 照片与参考图

### 10.1 本机与上传

CameraX 和 Album 均在当前 item 内调用，自动关联 Task／Item／Attempt。Album 内容必须成功复制实际字节，才算保存在本机；云端照片在离线不可读时明确失败，不假报成功。

照片可以多张，初始技术配置为每 item／attempt 最多 5 张、每张上传最多 5 MiB。客户端默认长边 1920 px、JPEG quality 85、目标约 2 MiB；这些是可调起始配置，生产启用压缩必须由 QA 用真实缺陷／标签样张验证。不满足清晰度时调整配置或关闭压缩，不需要重做架构。APK 不硬编码最终业务限额，服务器配置具有最终约束。

原图在上传 Ready 且 draft 附件关联得到持久确认前保留。上传失败／不明结果保留文件；不会因为 request 发出而删除唯一副本。上传后的压缩成品是服务器正式证据，记录其 hash／尺寸，不声称服务器保留了未上传的原图。

ClientPhotoId 在设备工作空间内稳定；重试查询或上传返回相同逻辑 AttachmentId。相同 ID、不同内容是冲突。服务器验证实际类型、内容和限制，持久保存到独立卷，metadata 写 PostgreSQL。Staging／Ready 与失败重试明确区分，只有 Ready 文件可进入正式提交。

### 10.2 必拍和新照片

普通 NG 至少一张当前 attempt 证据。OK 按冻结 PhotoRequirement；NG→OK 和仍 NG 的复检至少一张当前 attempt 新照片。PQE 指定的原 OK／Removed 项目必须重新完成检查，其照片义务按本次 NG、NG→OK 或冻结 OK PhotoRequirement 判断；不会因为被 PQE 指定就额外把所有 Optional 项目变成必拍。移除项目不强制拍照。

需要新照片时不能直接沿用旧 AttachmentId；同一 item 已提交旧照片的完全相同字节重传不能满足新证据要求。来源 Camera／Album 和 attempt 关联保留；不以 EXIF 当可信实际拍摄时间，不建设图像真实性鉴定／AI 审图。相册证据是否真实反映本次检查由巡检操作和 PQE 复核负责。

冻结 PhotoRequirement 改用有明确语义的 Required／Optional 配置。旧自由文本保留原值，只有显式确认转换后的新 Standard／Plan 才生成新策略任务，不把未知旧文本默认成 Optional。

### 10.3 Reference Images

PQE 在 Web 上传独立 reference image version。Task snapshot 固定版本／hash，之后编辑 Standard 不覆盖旧文件。App 缓存 reference 字节，支持查看和缩放，并显示任务的离线资源准备状态。

仍被 Standard、Task snapshot 或 submission 历史引用的参考图持续保留。Reference 与 inspection evidence 的 ID、用途、访问授权和清理范围分开；未引用的参考图才进入普通孤立文件清理。

## 11 同服务器文件储存与五年自动删除

### 11.1 储存

Web／API／PostgreSQL 和独立照片卷部署在同一 IPQC 主机。初始 1 TB 可扩容，文件目录不在 wwwroot，不开放 FTP／SMB 给客户端。DB 保存 StorageKey／metadata，API 通过受控根目录和服务账号读取。只接受授权 HTTPS，防止任意路径访问。

约 307 GB 是每日 4 Tasks、每 Task 28 张、每张 1.5 MB、五年的基准估算，不包含复检增长、参考图和备份。容量监控与扩容由 IT 执行；磁盘不足时明确失败并保留本机待上传数据。

### 11.2 自动到期

每个正式 submission 的 evidence reference 保存 SubmittedAtUtc 与 RetainUntilUtc。保留期限是 SubmittedAtUtc.AddYears(5)，UTC 比较，闰日落到第五年 2 月 28 日。服务器每小时检查、启动时补检查；服务停机后补清理，不提供秒级删除承诺。

到期自动执行，不产生审批候选、审批按钮、删除授权批次或业务 hold 工作流。Peter 会邮件确认五年自动删除的业务政策；邮件不是每次清理的运行依赖。

同一图片如果被后续 submission 沿用，每次引用分别计算五年；物理文件受较晚引用保护。有效 draft／reference 依赖同样不能被孤立文件清理误删。这是文件引用完整性，不是人工延期审批。

只有全部正式保留引用已到期、且无有效工作／参考依赖时才能删除物理文件。删除执行前和新增引用事务协调，防止清理与 Submit 竞争；删除中的对象不能被新提交直接引用。

删除采用可重试状态 Available／DeletePending／Deleted／DeleteFailed。先记录自动删除意图，再删除字节，最后记录结果。失败自动重试／告警，不伪装成已经删除。

保留 AttachmentId、hash、原始 metadata、不可变历史引用、自动删除时间／原因／执行者 System。历史页面和报告显示已按五年政策删除；不把经政策删除的照片当作损坏记录，也不删除业务结果或改写 revision。

### 11.3 其他文件生命周期

未同步的本机编辑／照片没有自动 TTL。服务器 staging 无有效上传活动超过 24 小时可以清理；Ready 但没有 draft／submission／reference 关联超过 7 天可清理，客户端仍保留未得到完整确认的副本并能重新上传。

有效未完成 draft 关联的证据保留。已正式终结、完全同步的任务，本机缓存 14 天后清理；服务器正式记录仍按其政策保存。自动清理在全体引用确认后执行，不能为腾空间丢掉唯一未同步工作。

### 11.4 备份与恢复

IT 提供独立于当前服务器故障域的 PostgreSQL＋照片协调备份，覆盖引用清单、hash 和删除状态；同一主机的另一目录不算独立备份。

五年自动删除首先作用于在线照片数据卷，不逐张改写既有备份集。备份集按 IT 的备份轮换淘汰。恢复时，在重新开放服务前重放／核对自动删除记录并运行到期清理，避免旧备份把已到期内容重新长期暴露。

元数据／结果的归档期限和备份轮换属于独立运行政策；本版不因照片五年清理顺带删除数据库检查结果。

### 11.5 F3 附件引用与物理删除的并发合同

本节细化 §10.1 的 Ready 和 §11.2–11.4 的清理／恢复，不增加审批或 hold 流程。V1 一个 AttachmentId 对应一个唯一 StorageKey 和附件行；同一文件被多次 Submit 沿用时复用该 AttachmentId，不另做跨 Attachment 的物理内容去重。若后续增加物理去重，锁必须上移到真正共享的 StoredObject 行，不能只锁不同逻辑引用。

Ready 代表上传字节已验证并持久保存、大小／SHA-256 匹配、metadata 已 commit，且删除状态为 Available。只有这一组合允许新增引用；上传响应和 hash 不满足时不能先返回 Ready。临时文件最终落盘／更名、数据库写入分阶段执行，启动恢复核对中间状态，不假设文件动作可随数据库 rollback。

所有新增、替换和解除 draft／submission／reference 依赖，以及清理抢占，使用同一个附件写入协议：

1. 在 PostgreSQL 事务中按 AttachmentId 固定顺序锁附件行（例如 SELECT FOR UPDATE，或语义等价的原子条件更新），防止不同 worker 使用不同路径绕过。多附件先固定顺序；Task 和附件锁在各相关服务中保持统一顺序。
2. 新增引用方在锁内重新检查 Ready／Available、访问归属、item／attempt 及版本，写入依赖和各次 Submit 的 RetainUntilUtc。Submit 中这些引用和 revision／receipt 同事务提交；有效 draft／reference 依赖也须通过该协议建立。
3. 清理方在相同锁内重新查询全部已提交保留引用、有效 draft／reference 依赖和当前 UTC。仅全部到期且无保护依赖时，以 CAS 将 Available 改为 DeletePending，同时持久写入稳定 DeleteOperationId、原因 RetentionExpired、检查时间和执行状态，然后 commit。
4. 一旦 DeletePending，新的任何引用都拒绝；不能先创建新引用再期待 worker 撤销已发出的文件删除。新增引用先成功则清理看到保护而跳过；清理抢占先成功则引用方得到可恢复 AttachmentUnavailable，不创建指向将删文件的 revision。
5. 引用删除、替换和清理不能直接凭缓存的“引用数为零”判断。引用仍以不可变 submission 事实和当前有效依赖为准；如有聚合期限／计数缓存，锁内校验与更新必须一致。

物理删除在抢占事务 commit 之后执行，不把磁盘 I/O 假装放入数据库原子事务。Worker 使用可过期的执行租约／CAS 领取同一 DeleteOperationId，多个 worker 或进程重启重试同一逻辑删除：

| 崩溃／结果位置 | 恢复合同 |
| --- | --- |
| DeletePending commit 前 | 数据库 rollback，文件没有被 worker 删除，可重新判断资格 |
| DeletePending 已 commit、删文件前 | 保留非可引用状态，恢复后继续同一删除 |
| 字节已删、Deleted 尚未 commit | 依据既有持久删除意图和对象 ID 核对文件缺失，幂等确认 Deleted；不再次创建删除操作 |
| 删除失败且字节可能存在 | 记录 DeleteFailed、错误及重试；继续禁止新增引用，不能未经验证回退 Available |
| Deleted 已 commit | 重试返回原结果；保留 metadata、hash、原引用和审计 |
| 没有匹配删除意图却发现缺字节／hash 错 | 标记 MissingUnexpected／Corrupt 并告警，不补造 RetentionExpired 审计来掩盖故障 |

日志分别保留意图开始、删除尝试和结果确认时间；崩溃恢复后观察到文件缺失，不能伪造一个无法证明的精确物理删除时刻。FileNotFound 只有在匹配对象的已持久化删除意图之后才可作为幂等完成，任意文件缺失不等于政策删除。DeleteFailed 的重试沿用原意图，不延长正式保留期。

附件读取、PQE／Completed viewer、HTML 和 XLSX 使用一致的 availability 投影：Available／DeletePending／DeleteFailed、DeletedByRetentionPolicy、MissingUnexpected／Corrupt。按本版已确认的业务保留政策完成的删除显示明确政策删除原因／时间；意外缺失或损坏显示完整性故障，不能当正常到期。两类情况都不改写原 submission 样本、结果或证据 hash。

恢复备份时先保持业务读取和 Native 同步入口关闭；恢复 DB／文件、核对对象／hash、重放可用的独立备份删除记录，并按当前 UTC 重新计算全部引用期限与依赖。完成 DeletePending／DeleteFailed 恢复和到期文件清理／可见性隔离后，才开放历史图片服务。即使旧备份没有最新 tombstone，也不能只依据旧 Available 状态将当前已到期、无有效依赖的证据重新暴露。到期清理仍是自动规则，不引入人工批次批准。

## 12 历史兼容与报表

保留 WorkflowVersion：现存任务为旧个人模式，新配置生成新 LineShared 策略。旧 N/A、Approved-Unqualified、指派人、Machine／Series／Mold 和旧时间记录不重解释、不补造。旧 endpoints 不允许绕过新任务的规则。

所有 submission 包含完整项目定义对应的结果／disposition，Removed 仍占一条有意义记录。报表不通过省略项目规避现有完整性检查。

普通 Completed 必须有对应 Approved review；NotPerformedConfirmed 必须有对应特殊确认且该 revision 全 Removed。两者形成统一历史投影，但保留明确 outcome／filter。PendingApproval 和 PendingNotPerformedConfirmation 不进入已终结 Web 历史。

Inspector 为对应最终正式提交者，业务检查时间为该 Submit 的服务器时间；批准／特殊确认时间另列。贡献者、前版 NG、Removed／恢复、PIC、Barcode、照片和 reference version 可回看。

Web preview 为 HTML／modal，下载为 XLSX。XLSX 第一版包含结果、样本、Barcode、PIC、移除、提交／审查历史和照片数量／状态／授权查看链接，不批量嵌入全尺寸图片或 base64。完整图片在授权 Web viewer 查看；不增加 PDF、ZIP 或新 dashboard。

## 13 工程边界与复杂度

整体难度中高。主要复杂度是可验证的数据生命周期和跨端一致性，不是平板表单。现有服务器排程、范围判定、快照、不可变 revision、auth、Web i18n 和 Excel 可复用，不需要推翻 Web/API v1。

| 模块 | 难度 | 工作重点 |
| --- | --- | --- |
| 普通 Android 表单／导航 | 中 | 平板布局、最多 50 样本、scanner 焦点、进程恢复 |
| 72 小时普通会话 | 低至中 | 到期／重启／回拨后的常规恢复，不做安全时钟项目 |
| 产线共享与贡献者 | 高 | 授权替换、原作者与上传者分离、旧关系兼容 |
| 草稿／提交可靠性 | 高 | 版本竞争、重复请求、回包丢失、跨用户同步 |
| 复检／全 Removed | 中高 | 新 attempt、最低样本延续、独立确认和状态完整性 |
| 照片／参考图 | 中高 | 字节持久化、Ready、引用、历史和故障恢复 |
| 五年自动清理 | 低至中 | 固定过期计算、引用保护、可重试删除日志；没有审批系统 |
| mTLS 注册／部署 | 中 | 标准设备认证、配对、凭证轮换和 IT TLS 配置 |
| Web 审查／历史／报表 | 中高 | 新 outcome 和旧历史一致，图片／PIC／目标项目 |
| 发布／真机／恢复 | 中 | 签名升级、数据保留、弱网、真实 PostgreSQL 和附件恢复 |

不把项目包装成只有几个 Android 页面。也不因离线时钟、闰日或到期审批扩展出另一套平台。出现新的真实业务矛盾时，Staff Engineer 提供具体场景、实现成本和推荐方案再与 Peter 沟通，不把技术选择当业务问卷。

## 14 实施依赖与 PR 边界

| 顺序 | 范围 | 主要交付 |
| --- | --- | --- |
| P1 | 策略版本、Line、设备注册／撤销与 Native 认证 | 旧新权限隔离、Admin UI、TLS 部署合同 |
| P2 | 草稿版本、操作／作者上下文、Native session／OfflineGrant | 条件更新、去重、恢复协议 |
| P3 | 样本／Barcode、Removed／无数值 NG、Task 字段／PIC | 可拆为小 PR，连同 Standard Web／Excel 合同 |
| P4 | 幂等 Submit、NG／特殊确认状态、目标项目和精确 review | 不可变完整快照、receipt、历史范围 |
| P5 | 文件 adapter、证据／reference、Ready、保留引用 | 上传／读取／重试、服务器证据验证 |
| P6 | Web PQE／特殊确认／历史、HTML／XLSX | 目标选择、照片／PIC、兼容 resolver |
| P7 | Android 基础／认证／缓存／导航 | 配对、online login、password change、离线任务 |
| P8 | Android 表单／共享草稿／照片／自动同步 | crash／reboot、72h、冲突、作者和上传结果 |
| P9 | Android Submit／复检／全 Removed 与综合验收 | 显式提交、回包恢复、真实流程 |
| P10 | 自动删除／本机清理、备份恢复和签名发布验收 | 固定政策、日志、恢复、安装 |

P3／P5 等范围按实际 diff 再拆小，不作为一个巨大 PR。Android 可以在合同稳定后启动，不等所有 Web 页面完成。每个规则在对应 PR 加测试，不把失败恢复全部留到最终 UAT。

## 15 测试与上线条件

| 场景 | 必须通过 |
| --- | --- |
| 普通样本 1／50／51 | 冻结 minimum 生效，50 合法，51 服务端拒绝 |
| 复检最低数 | frozen 3／前实测 5 要求至少 5；无数值 NG／Removed 不降低后续要求 |
| 相同数值重复 NG | 新 attempt 可以同值，必须有新样本和新照片，每次 revision 独立 |
| 无数值 NG | NG Reason／照片必需，无假数值，无普通数值验证绕过 |
| NG→OK | 新改善照片必需，普通 Standard Optional 不能绕过 |
| 最后 Removed | 显示警告但允许移除和 Submit，不生成 Qualified |
| 特殊确认 | Submit→独立等待→确认／指定恢复，普通 Approve 不可代用 |
| PQE stale review | 旧页面不能审新 revision；并发只有一个 decision |
| A 离线、B 登录接续 | 共享工作保留，作者／上传者／提交者分别正确 |
| 自动同步 | 不产生正式 revision，旧回包不确认新编辑 |
| 回包丢失 | 上传／Submit 查询和重试无重复，无虚假成功 |
| 72h／重启 | 正常恢复同一用户，到期锁访问不删工作，异常时间要求联网 |
| 设备撤销／换绑 | 在线立即拒绝旧身份，不跨线同步；离线已知限制可解释 |
| 图片保存 | Album 实字节、进程退出／空间不足不假报；唯一副本保留 |
| reference 修改 | 旧 Task 仍显示旧版本，可离线缩放 |
| 自动五年删除 | UTC／闰日、重复运行、停机补清理和失败重试正确，无人工审批 |
| 多 revision 共享文件 | 较晚引用保护文件；新增提交与删除竞争不破坏证据 |
| 恢复旧备份 | 结果／引用／hash 可用，到期删除策略恢复后继续执行 |
| 旧历史 | N/A、Approved-Unqualified、旧 review／时间／字段和 XLSX 可读 |
| 真 PostgreSQL | migration／条件写／Submit／review／附件引用竞争实测 |
| SM-T500 | Camera／Album、弱网、横竖屏、50样本、keyboard-wedge、性能 |
| 签名升级 | 同 applicationId 和签名，就地升级保留 DB／照片；不靠卸载重装 |

独立 Web／API／Android builds 和 CI。保留 TypeScript strict、lint、unit／integration、Release／production builds；Android 增加 instrumented 和 signed release 验证。签名 key、CA private key、JWT secret 和生产连接串不入库。

### 15.1 F1 至 F3 专项验收

以下是实施时必须加入的测试合同，本次只补充文档，没有运行或声称通过。

| ID | 场景 | 必须证明 |
| --- | --- | --- |
| F1-01 | A grant 普通过期／正常退出，A 仍有效；B 同线在线 | 允许符合来源合同的历史同步；CapturedContributor=A、UploadedBy=B，Submitter 取实际提交人 |
| F1-02 | A 已停用、失去资格或来源安全撤销 | 相关记录及依赖隔离，不混入 B 全量草稿；原文件保留，B 重做产生新操作 |
| F1-03 | PAD 撤销／换绑／来源证明缺失或伪造 | 无跨线字节上传和写入，无静默作者重标记；明确可恢复冲突 |
| F2-01 | WorkManager Save 与 Submit 竞争、旧回包迟到 | 提交屏障和冻结 LocalEditVersion 生效，未确认输入不混入／被覆盖 |
| F2-02 | Preparing 后新增照片、Sealed 后收到 Ready | 只接受冻结 manifest；迟到照片不改变 intent 的 hash／版本 |
| F2-03 | stale DraftVersion／相同 attempt 不同内容 | 409；不改变原 intent、不创建错误 revision |
| F2-04 | Submit commit 后响应丢失／杀进程重启 | 同一 intent 查询或显式重试得到唯一 receipt；后台不发 Submit、不假报成功 |
| F2-05 | A 提交在途、B 在线接续 | 不用 B 冒充 A；原请求与 B 明确新请求最多接受同一基础状态一次 |
| F2-06 | NG 已接受后旧请求重试／实际再次 NG | 旧 attempt 返回旧 revision；新 attempt 必须符合新复检工作与版本条件 |
| F3-01 | PostgreSQL Delete 与 Submit／draft 引用竞争 | 同一附件锁下只存在“新引用成功且文件受保护”或“删除抢占成功且新引用失败” |
| F3-02 | 同文件多 Submit 日期及有效 reference／draft | 最新有效期限／依赖受保护，不能按最早引用日期删除 |
| F3-03 | 每个 DB commit／文件删除步骤崩溃，多 worker 重试 | 原 DeleteOperationId 可恢复；无悬空新引用、无未经验证回退 Available |
| F3-04 | 无删除意图的缺文件／hash 损坏 | 独立完整性故障／告警，不标成正常 RetentionExpired |
| F3-05 | 从删除前备份恢复 | 重放／重算／隔离完成前不开图片服务；历史／XLSX 正确区分政策删除与损坏 |

## 16 Peter 需要做的事项

当前没有新的技术选项需要 Peter 再逐项决定。该版本交 ChatGPT 做独立架构 review，之后由 Peter 明确是否开始 scoped implementation。

Peter 按已经表示的计划发一封业务确认邮件，明确五年自动删除的范围：正式提交证据从各次 Submit 起算，在线物理文件在无其他有效引用后自动清理；保留结果、照片元数据、提交历史和删除日志；参考图与未同步资料采用独立生命周期；备份按 IT 的备份集轮换淘汰，不逐张回写。V1 没有到期审批或业务 hold 流程。若业务回复要求所有备份副本同日物理删除，或要求其他证据范围／例外，再作为明确的新变更沟通，不预先建设。

QA 提供 PIC roster、实际 Standard／参考图与照片清晰度样例，并参加巡检 UAT。IT 提供服务器／卷／HTTPS 与 Native TLS 入口／备份位置／PAD 和 scanner／签名保管。它们是输入与上线准备，不是要求 Peter 选择库、时间算法、数据库索引或错误码。

## 17 给独立 GPT Reviewer 的任务

请 review 本 Rev3.1 作为完整架构基线，区分 Peter 最新确认的业务变更、Staff Engineer 已选择的技术设计和现有 main 实现。

重点检查：样本特殊状态与最低数延续、全 Removed 的独立 outcome、mTLS 与个人认证组合、共享草稿作者、DraftVersion／idempotency、离线恢复、证据／reference 冻结、五年自动清理的引用安全、报表历史兼容及测试覆盖。

Peter 已明确简化 72 小时为常规业务会话控制，并将五年清理改为自动执行、取消人工审批和 V1 hold 管理。不要把这些问题再次作为未回答的产品问卷。若认为存在问题，请给具体失败场景、严重度、最小修正与实现成本，不仅写泛化安全建议。

复核结论采用可核定／有具体修正后可核定／不可核定，并说明真正阻碍。代码尚未实现是实施工作，不应单独作为架构不可核定的理由。本文件不授权创建代码、迁移、分支或 PR。

## 18 参考与实施依据

- 现有仓库：main 86f4d817ac68dfafebaaef341f7f2a5bbaedb73b，docs/01–12；本版 TO-BE 不等于现有实现。
- [ASP.NET Core certificate authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/certauth?view=aspnetcore-10.0)：TLS 层证书验证和代理边界。
- [Android Keystore](https://developer.android.com/privacy-and-security/keystore)：密钥保护及硬件能力边界。
- [Android SystemClock](https://developer.android.com/reference/android/os/SystemClock)：单调计时与重启／墙上时钟区分。
- [Android app signing](https://developer.android.com/studio/publish/app-signing)：签名 APK 和升级身份。

本次交付仅生成独立架构文档；没有改动 IPQC-system 仓库文件。
