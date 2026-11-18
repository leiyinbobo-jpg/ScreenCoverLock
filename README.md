# ScreenCoverLock（Windows 遮屏锁）

[中文](./README.md) | [English](./README_EN.md)

---

## 项目简介

**ScreenCoverLock** 是一款面向 Windows 的全屏桌面遮屏与异常取证工具，专为**“后台任务持续运行 + 公共场所防人触碰 + 远程/手机端 AI 安全接管”**场景设计：

- **公共场所离开防误触 / 防窥屏**：
  在宿舍、图书馆、实验室或办公室跑下载、编译、模型训练等后台任务时，保持屏幕常亮不断网，同时用全屏遮罩挡住桌面画面并锁定本地操作。
- **支持手机端让 AI（如 Codex 等可操作电脑/看屏的 AI）临时接管**：
  当你在外通过手机向电脑上的 AI 下达指令时，AI 可通过本机授权控制接口**临时暂停遮屏**，完成截图查看与桌面操作后再自动恢复遮屏。
- **物理触碰瞬时反制 + 可选摄像头取证**：
  在 AI 临时操作期间（或正常锁屏期间），一旦检测到任何**非 AI 注入的本地物理键鼠触碰**，系统会立刻拦截该次输入并**瞬间弹回全屏遮屏锁**；若已开启异常录像，还会同步触发摄像头录制现场画面。

---

## 快捷键机制：哪些会屏蔽，哪些不会屏蔽？

为了兼顾防误触与系统故障兜底，**严格模式**和**日常模式**对快捷键的拦截策略不同；同时受 Windows 系统安全架构限制，部分系统级热键**不会也不可能**被本软件屏蔽：

| 快捷键 / 组合键 | 严格模式 (`ScreenCover-Strict`) | 日常模式 (`ScreenCover-Casual`) | 说明 |
| :--- | :--- | :--- | :--- |
| **`Win` 键**（含 `Win+D` / `Win+Tab` / `Win+E` 等） | **屏蔽** | **屏蔽** | 通过底层键盘钩子拦截左右 `Win` 键 |
| **`Alt+F4`** | **屏蔽** | **屏蔽** | 拦截窗体关闭快捷键，防止直接关掉遮屏 |
| **`Ctrl+Shift+Esc`**（任务管理器） | **屏蔽** | **不屏蔽** | 严格模式拦截任务管理器热键；日常模式放行 |
| **`Alt+Tab` / `Alt+Esc`**（切换窗口） | **不拦截按键，但强力抢回置顶** | **不屏蔽** | 严格模式通过 `TopMost` + 每 `200ms` 焦点巡检把遮屏拉回最前；日常模式允许切到其他窗口 |
| **`Ctrl+Alt+Del`**（安全桌面） | **不会屏蔽** | **不会屏蔽** | Windows 内核级安全注意力序列（SAS），普通桌面程序无法也不应拦截，保留系统级故障恢复通道 |
| **`Win+L`**（Windows 系统锁屏） | **不会屏蔽** | **不会屏蔽** | Windows 系统原生锁屏热键由 Winlogon 优先接管，本工具不是也不替代系统锁屏 |
| **`Ctrl+Shift+Q`**（本软件应急退出） | **软件自身响应** | **软件自身响应** | 全局热键，无需密码直接退出遮屏（包括 30 秒错误冷却期间） |
| **`Ctrl+Shift+L`**（本软件手动复锁） | **软件自身响应** | **软件自身响应** | 仅在启用 `--allow-automation` 时注册，随时手动立即恢复遮屏 |

> **重要提示**：本软件属于**桌面应用层遮屏工具**，**不能替代 Windows 系统锁屏（`Win+L`）**。知晓 `Ctrl+Shift+Q` 或通过 `Ctrl+Alt+Del` 打开系统安全界面/任务管理器的人仍可退出程序。

---

## 详细操作规范

### 一、首次使用准备（只需做一次）

1. **打开设置入口**：
   运行 `.\out\ScreenCover-Strict.exe --settings`（或双击安装后的“设置与记录”快捷方式）。
2. **设置 6 字符机主密码**：
   首次进入设置时，连续输入两次自定义的 **6 位机主密码**（区分大小写）。密码通过当前 Windows 用户的 DPAPI 加密保护，不保存明文。
3. **（推荐）自定义界面图片**：
   在设置界面选择你喜欢的正常壁纸、暗图、输入进度图标等（详见下方“界面图片自定义说明”），点击“预览”确认效果后保存。
4. **（可选）开启异常录像**：
   - 录像**默认关闭**。
   - 如需开启，请先在“摄像头”栏点 **查找** → 选择设备 → 点击 **手动测试画面和拍摄范围（约3秒）** → 确认弹窗画面正常且构图合适 → 勾选 **开启异常录像** 并点击 **保存设置**。
   - 若更换了摄像头，必须重新完成一次手动测试才能启用。

---

### 二、离开电脑前的标准启动流程

- **场景 A：纯离开锁屏（不需要 AI 在你离开时操作桌面）**
  - 直接运行 `ScreenCover-Strict.exe`（推荐在公共场所使用严格模式）或 `ScreenCover-Casual.exe`。
  - **切勿**传入 `--allow-automation`，此时后台任何脚本都无权暂停遮屏。

- **场景 B：需要用手机让 Codex / AI 远程操控电脑**
  1. 启动时带参数运行：`ScreenCover-Strict.exe --allow-automation`（或使用 `scripts\launch-strict.ps1`）。
  2. 电脑会弹出**“启用自动化临时暂停”**确认框：
     - 必须由机主本人在电脑前亲自点击 **“是”**，本次启动才允许后台控制；
     - 每次重启程序都会重新询问，无人授权绝不会默认放行。
  3. 确认进入全屏遮屏状态后，即可安心离开电脑。

---

### 三、离开期间的 AI 远程控制规范

已获授权后，AI 代理（或后台脚本）通过 `scripts\ScreenCover-Control.ps1` 操作遮屏生命周期：

1. **查询当前状态**：
   ```powershell
   .\scripts\ScreenCover-Control.ps1 -Action Status -Mode Strict
   ```
   检查返回 JSON 中的 `automationAllowed`、`state` 和 `lastRestoreReason`。
2. **临时暂停遮屏（开始桌面操作前）**：
   ```powershell
   .\scripts\ScreenCover-Control.ps1 -Action Start -Mode Strict
   ```
   - 遮屏会隐藏窗口、暂时释放置顶键盘钩子，并自动在后台启动 `KeepAlive` 租约续期进程（每 10 分钟租约自动续期）。
   - 同时挂载 `InputGuard` 真人物理输入防线。
3. **AI 执行操作期间的安全规则**：
   - AI 的自动点击、自动输入在系统底层带有“软件注入”标记，不会触发复锁。
   - **一旦在 AI 暂停期间有陌生人触碰实体鼠标/键盘**：
     `InputGuard` 会立刻拦截该次输入、终止当前控制会话，并在毫秒级内恢复全屏遮屏（同时将 `lastRestoreReason` 标记为 `physical_input`）。
   - **安全铁律**：当检测到 `lastRestoreReason == "physical_input"` 时，说明现场有人碰过电脑，AI **严禁**再次自行调用 `Start` 解开遮屏，必须保持锁定等待机主确认。
4. **操作完成后手动恢复遮屏**：
   ```powershell
   .\scripts\ScreenCover-Control.ps1 -Action Resume -Mode Strict
   ```

---

### 四、机主回来后的日常操作与解锁

- **唤醒与输入**：
  - 无本地操作 10 秒（可调）后屏幕切换至暗图；此时移动鼠标、开始按键或输入正确密码前缀都会保持暗图，降低在公共场合暴露屏幕内容的风险。
  - 只有输入**错误字符**时才会立即恢复正常图并显示提示文字（可选开启错误提示图）。
- **密码录入规则**：
  - 最多 6 位字符，自动禁用中文输入法（IME）与复制粘贴快捷键。
  - 输入 `1～5` 位显示对应进度的指示图标，第 `6` 位显示完成图标（仅表示长度，**绝不提示哪一位对错**）。
  - 退格键删除不算错误；按 `Escape` 清空当前尝试；输入完整 6 位密码后按 `Enter` 解锁。
  - 若 60 秒（可调）内未继续输入，当前尝试自动清空；连续提交错误 5 次进入 30 秒冷却期。
- **误触自愈（防误录）**：
  - 若你自己在解锁时不小心按错一位触发了录像，只要**不退出本次尝试**，用退格键删掉错误字符并最终输入正确密码回车解锁，系统会自动停止本次录像并清理仅包含该次尝试的片段。
- **应急操作**：
  - `Ctrl+Shift+L`：在 AI 暂停期间由人工立刻恢复遮屏。
  - `Ctrl+Shift+Q`：应急退出遮屏（冷却期同样有效）。

---

## 核心实现原理

1. **全屏覆盖与防休眠保活**
   - 调用 `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED)` 在遮屏存活期间阻止系统休眠与关闭显示器（不修改系统电源计划，退出即恢复）。
   - `DisplayCovers` 监听 `SystemEvents.DisplaySettingsChanged`，为所有物理显示器同步创建独立遮罩窗体。
2. **密码安全与逐位前缀检测（`PasswordVault` + `LockState`）**
   - 完整密码使用 `SHA-256` 摘要经 Windows `ProtectedData`（DPAPI `CurrentUser`）加密存入 `legacy-verifier.dat`。
   - 逐位检测使用每次初始化生成的 32 字节随机密钥计算各长度前缀的 `HMAC-SHA256`，同样经 DPAPI 加密存入 `prefix.dat`。
   - 每次输入新字符仅比对当前前缀的 HMAC 值，因此能实现“错一位即触发警示/录像”，同时磁盘上不保留任何明文或可逆信息。
3. **AI 注入操作与真人物理触碰的底层区分（`InputGuard` + `ActivityMonitor`）**
   - Windows 底层键鼠钩子（`WH_KEYBOARD_LL = 13`、`WH_MOUSE_LL = 14`）的回调结构体中带有事件标志位：
     - 鼠标 `LLMHF_INJECTED` / `LLMHF_LOWER_IL_INJECTED`（位掩码 `0x03`）
     - 键盘 `LLKHF_INJECTED` / `LLKHF_LOWER_IL_INJECTED`（位掩码 `0x12`）
   - 自动化工具发送的模拟键鼠事件通常带有上述注入标记；而实体键盘、触摸板、外接鼠标产生的硬件中断事件不包含注入标记。
   - 暂停期间 `InputGuard` 放行带注入标记的操作，一旦遇到**无注入标记**的事件，钩子直接返回 `1` 吞掉该次物理输入并异步投递恢复遮屏任务，避免任何物理操作穿透到桌面。
   - *边界说明*：注入标记由 Windows 内核提供用于区分自动化模拟与物理硬件输入，但不能作为高安全等级的身份凭证；本程序定位为防误触与辅助看护工具。
4. **命名管道与租约防失联机制（`ControlPipe` + `SessionLease`）**
   - 控制管道名称绑定当前用户 SID 与 Windows 会话 ID（`ScreenCover.Control.v1.<SID>.<SessionId>.<mode>`），ACL 仅允许本用户访问，拒绝网络 SID 访问。
   - 每次 `Start` 生成随机 256-bit Bearer Token，并设置单次租约上限（默认 10 分钟）。后台 `KeepAlive` 进程每 30 秒续期一次；若后台 AI 崩溃、断网或进程被结束，最后一份租约到期后遮屏自动恢复，防止电脑长期裸露在解锁状态。
5. **独立异步录像工作进程（`ScreenCover.Recorder.exe`）**
   - 主界面钩子线程绝不直接启动摄像头或等待编码，只向 `%LOCALAPPDATA%\ScreenCoverUpgrade\Queue` 写入事件命令并拉起独立的 `ScreenCover.Recorder.exe`。
   - 工作进程通过 `FFmpeg`（DirectShow `dshow`）录制**无音轨 H.264/Matroska（`.mkv`）**视频，设置约 1 秒集群刷盘降低异常断电损失；确认首个编码帧后开始倒计时（默认末次触发后 15 秒，单段最长 60 秒），闲置 30 秒后自动退出释放摄像头。

---

## 界面图片自定义说明

开源仓库内置的全部默认壁纸、输入图标、警示图标和 `.ico` 程序图标均为**代码生成的无版权极简几何图形**，零版权风险。  
机主可以在设置界面（`--settings`）自由替换为任意本机图片（仅保存路径于本机 `%LOCALAPPDATA%\ScreenCoverUpgrade\settings.json`，**不会进入 Git 仓库**）：

1. **正常背景图（`NormalImage`）**：正常遮屏与错误恢复时显示的全屏壁纸。
2. **暗图（`DarkImage`）**：闲置变暗时显示的壁纸。**若只选了正常背景图而将暗图留空，程序会自动把你的正常背景图压暗至约 28% 亮度生成暗图**。
3. **输入图标目录（`DigitIconFolder`）**：选择本地文件夹，自动按顺序识别 `1.png`～`5.png`（或 `digit-1.png`～`5.png`）作为 1～5 位输入指示图标；放有 `6.png` / `complete.png` 时也会自动识别为第 6 位图标。
4. **单张输入图标（`DigitImage`）**：备选通用图标，未设置目录或目录中缺某一位时使用。
5. **输满 6 位图标（`CompleteImage`）**：输入满 6 位时显示的完成图标。
6. **错误提示图（`ErrorImage` + 开关 `ShowErrorImage`）**：可选开启并在错误警示条上方展示自定义提示图。
7. **输入指示区位置（`InputPosition`）**：支持 `Bottom`（底部居中）、`Center`（屏幕居中）、`Top`（顶部居中），方便适配不同构图的壁纸。
8. **预览与清除**：每项支持直接弹窗预览，点“清除”可随时恢复内置默认无版权图形。

---

## 快速构建与运行

### 环境要求

- Windows 10 / 11（x64）
- Windows 自带 .NET Framework 4.x 编译器（`csc.exe`，无需安装 Visual Studio）
- PowerShell 5.1+

### 编译与测试

```powershell
# 1. 编译 Strict / Casual / Recorder 及单元测试程序到 out/
.\build.ps1

# 2. 运行无头核心逻辑、自定义图片与管道控制测试（不弹出遮屏，不调用摄像头）
.\tests\out\UpgradeTests.exe
.\tests\out\ControlTests.exe

# 3. （可选）如需使用摄像头录像，下载官方 FFmpeg essentials 至 tools/ffmpeg.exe
.\scripts\Download-FFmpeg.ps1

# 4. （可选）打包独立用户级 Setup.exe 安装包到 dist/
.\scripts\Build-Package.ps1 -Version 1.1.0
```

### 运行入口

- 打开设置与自定义图片：`.\out\ScreenCover-Strict.exe --settings`
- 启动严格模式（普通锁屏）：`.\out\ScreenCover-Strict.exe`
- 启动严格模式（允许授权 AI 临时控制）：`.\out\ScreenCover-Strict.exe --allow-automation`
- 启动日常模式：`.\out\ScreenCover-Casual.exe`

---

## 目录结构

```text
ScreenCoverLock/
├─ assets/          # 内置无版权默认几何壁纸、输入图标、警示图标与程序图标
├─ src/             # C# 源码（遮屏主窗体、设置、多屏支持、控制管道、录像管理、安装器）
├─ scripts/         # 打包脚本、自动化控制脚本、启动脚本与 FFmpeg 下载脚本
├─ tests/           # 状态机、自定义图片、控制租约与管道单元测试
├─ tools/           # FFmpeg 官方来源说明、许可证与校验和（不含二进制文件）
├─ build.ps1        # 一键编译脚本
├─ LICENSE          # MIT 开源许可证
├─ README.md        # 中文文档
└─ README_EN.md     # English Documentation
```

---

## 安全与免责声明

- 本工具为**应用层桌面遮屏与看护工具**，不能拦截 `Ctrl+Alt+Del` / `Win+L` 等系统安全桌面操作，**不能替代 Windows 系统锁屏**。
- 个人密码校验材料、配置、事件记录与录像均仅保存在本机 `%LOCALAPPDATA%\ScreenCoverUpgrade`（受当前用户 ACL 保护），**无任何云端上传**；开源仓库不包含任何个人数据。
- 本仓库内置素材均为原创几何图形；用户本机自行选择的自定义图片请遵守相应版权规定。
- 源码基于 [MIT License](./LICENSE) 开源；可选 FFmpeg 组件遵循其上游许可证（见 `tools/FFmpeg-LICENSE.txt`）。
