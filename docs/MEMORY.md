# CS国服去和谐程序（项目笔记）

## 是什么
CS2 国服血液效果切换工具。玩前点「去和谐」= 游戏内红血，玩完点「恢复」= 官方原状。
原理：仅重命名 `game\csgo_lv\pak01_dir.vpk` ↔ `pak01_dir.vpk.bak`（该文件是国服低暴力素材包，
移开后引擎回落默认红血素材）。不注入、不改内容、不联网。

## 交付物（工作区根目录）
- `CS国服去和谐程序.exe` — 单文件、免安装、约 61 KB
- `使用说明.txt` — UTF-8 **带 BOM** + 全 CRLF（记事本兼容），随 exe 分发
- `程序截图.png` — 每次改版后重新截图验证
- `src/` — Program.cs / CSBloodToggle.csproj / app.ico

## 构建（必读）
**Desktop 路径下 MSBuild 枚举源文件异常（CS5001/CS1555），必须走暂存目录：**
```
mkdir %TEMP%\cs2stage
copy src\Program.cs src\CSBloodToggle.csproj src\app.ico %TEMP%\cs2stage\
cd %TEMP%\cs2stage && %LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe build -c Release -v q
copy bin\Release\net48\CS国服去和谐程序.exe  ← 拷回工作区
```
dotnet SDK 8.0.426 装在 `%LOCALAPPDATA%\Microsoft\dotnet`（用户级，不碰系统）。
net48 / WinExe / ApplicationIcon / GenerateSupportedRuntime=false（保持单文件，无 .exe.config）。

## 关键约束
- **只碰 pak01_dir.vpk 一个文件**；操作前校验 `game\bin\win64\cs2.exe` 与 `game\csgo` 存在
- 状态机四态：`harmonized` / `deharmonized` / `stale`(原文件与 .bak 并存，Steam 重下过) / `unknown`
- `stale` 处理原则：Deharmonize 先删孤儿 .bak 再移；Restore 保留 Steam 重下的官方件、删孤儿
- 单实例：`Mutex("Local\\CSBloodToggle_SingleInstance_9F2A7C")`，第二次启动激活已有窗口
- 窗口：`FixedSingle` + `MinimizeBox=true` + `MaximizeBox=false`（可最小化到任务栏，不可最大化/拉伸）；
  最小化时 `Resize` 事件停掉 blobTimer，还原时重启并重绘一次
- `Main` 里只有 find/state/deharm/restore 走命令行模式，**其余任何参数都启动 GUI**
  （否则把文件拖到 exe 上会静默退出）

## 审美定调（用户已验收，勿擅改）
CLEAN WHITE 液态玻璃：**零反光、零描边、零阴影**；玻璃卡片 alpha 98、按钮 158/120；
按钮窄条居中（x=74, w=260）；窗口高透明；光斑缓慢漂移（有焦点 120ms / 失焦 480ms）。
标题 `CS国服去和谐程序 · made by xixi❤️`，标题后有小圆圈 i（悬停/点击看免责声明）。

## 踩过的坑（重复踩会浪费时间）
1. **WinForms z 序**：后 `Controls.Add` 的控件在**底层**。与已有 Label 重叠的自绘控件必须 `BringToFront()`。
2. Panel 设 `BackColor = Transparent` 前必须 `SetStyle(SupportsTransparentBackColor, true)`，否则启动即崩。
3. 截图脚本：必须 `SetWindowPos(HWND_TOPMOST)` 否则截到被遮挡的其他窗口；
   加了单实例锁后**先 taskkill 再等 2 秒**，否则新实例被锁拦住、拍到的其实是正在退出的旧窗口。
4. 测量 CPU 要先预热 6 秒再连测多次——启动阶段 JIT 会让数值虚高 5~10 倍（曾据此误判"16% CPU"）。
5. 读回自己写的 CRLF 文件要用二进制模式，Python 文本模式会把 CRLF 规范化成 LF。
6. 改文案别用 `...`（Windows"会弹窗"惯例），用户看不懂，以为文字被截断。

## 验证状态

**项目已完成验证，用户确认可用（2026-10-22）。**

- 本机自动化验证：状态机回归 9 项、拖放/多参数启动、单实例拦截与窗口激活、i 图标点击弹窗、
  最小化/还原、启动 114ms、关闭 61ms 退出码 0、无残留进程、无内存/句柄泄漏
- 真机验证：用户已在装了国服 CS2 的机器上跑通全流程，确认「去和谐 → 游戏内红血 → 恢复」可用
  （开发机上的 `Counter-Strike Global Offensive` 只是 0 字节卸载残留，当时无法自测）
- `game\csgo_lv\pak01_dir.vpk` 这个路径假设**成立**，未被完美世界改动
- 已知局限（用户明确说不做）：无 DPI 感知声明（125%/150% 高分屏会模糊）、无忘恢复提醒、无数字签名
