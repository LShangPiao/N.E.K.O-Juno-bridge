# tools —— 开发辅助

这个目录里的东西**不参与模组运行**，只是开发期用来看清游戏内部结构的工具。
正式构建只需要 `tools/dl/roslyn/` 里的 C# 编译器。

> ⚠️ **所有 `.ps1` 一律保持纯 ASCII 内容。**
> Windows PowerShell 5.1 读取无 BOM 的 UTF-8 文件时按 ANSI 代码页解释，
> 中文注释会直接把脚本解析坏（姊妹项目已经栽过一次）。

---

## 需要准备的两个外部依赖

`tools/dl/` 和 `tools/bepinex-payload/` 都在 `.gitignore` 里 —— 体积大，
而且都属于第三方，仓库里不放。按下面的步骤获取：

### 1. Roslyn 编译器（构建必需）

```powershell
$dl = "tools\dl"
New-Item -ItemType Directory -Force -Path $dl | Out-Null
$url = "https://www.nuget.org/api/v2/package/Microsoft.Net.Compilers.Toolset/4.11.0"
Invoke-WebRequest $url -OutFile "$dl\roslyn.nupkg" -UseBasicParsing
Copy-Item "$dl\roslyn.nupkg" "$dl\roslyn.zip"
Expand-Archive "$dl\roslyn.zip" -DestinationPath "$dl\roslyn" -Force
# 期望得到：tools\dl\roslyn\tasks\net472\csc.exe
```

本机没有 .NET SDK / MSBuild，只有 .NET Framework 自带的老 `csc.exe`（C# 5）。
Roslyn 的 `csc.exe` 能在 .NET Framework 上跑，并且支持现代 C# 语法
（本项目用 `-langversion:7.3`）与 netstandard2.1 引用。

### 2. BepInEx（安装与构建必需）

```powershell
$url = "https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip"
Invoke-WebRequest $url -OutFile "tools\dl\BepInEx_win_x64_5.4.23.5.zip" -UseBasicParsing
```

`install.ps1` 会自动展开它；`build.ps1` 从中取 `BepInEx\core\BepInEx.dll` 作为编译引用。

### 3. 游戏程序集（构建必需）

`build.ps1` 默认从游戏安装目录读取
`SimpleRockets2_Data\Managed\*.dll`。
如果游戏装在别处，用 `.\build.ps1 -GameDir "..."`。

`refs/` 目录里放一份从官方 ModTools 解出来的 `ModApi.xml` /
`SimpleRockets2.xml`（约 13 MB 的 API 文档），供查接口用，同样不进仓库。

---

## 脚本说明

| 脚本 | 用途 |
| --- | --- |
| `reflect.ps1` | 用 .NET Framework 的 ReflectionOnly 模式列出程序集类型与成员。`-Filter` 过滤类型名，`-Members` 列出成员，`-Constants` 读常量值。 |
| `ildump.ps1` | 用 [dnlib](https://github.com/0xd4d/dnlib) 反汇编 IL。`-Strings` 只抽 `ldstr` 常量 —— 官方加载器的字符串常量会把文件格式和错误分支全部交代出来。 |
| `strings.ps1` | 从二进制里抽可打印字符串（latin1 + UTF-16 双路）。`-Pattern` 过滤。 |
| `shot.ps1` | 截取游戏窗口到 PNG（用于人工/多模态核对界面）。 |
| `click.ps1` | 往游戏窗口发鼠标/键盘事件。**注意：这个游戏用 Unity RawInput，`SendInput` / `mouse_event` 合成的事件它收不到**，所以基本只用于诊断，实际驱动 UI 要靠 `DebugProbe.cs`。 |

---

## 排查思路（这个项目是怎么摸清游戏结构的）

1. **先看官方给了什么** —— 游戏目录里的 `ModTools\SimpleRockets2_ModTools.unitypackage`
   解包后含 `ModApi.dll` / `ModApi.xml` / `SimpleRockets2.xml`，
   这些 XML 就是完整的官方 API 文档，比任何逆向都省事。
2. **`strings.ps1` 找文件格式** —— `ModApi.Core.dll` 里直接就有
   `sr2-mod` / `*.sr2-mod-android` 两个扩展名常量。
3. **`ildump.ps1` 看加载流程** —— `Jundroo.ModTools.Core.ModManagerBase`
   的 `ldstr` 常量把「mod 必须有 header」「程序集必须作为 asset 存在」
   「部分版本禁用 mod 代码执行」全部写明白了。
4. **`reflect.ps1 -Members` 摸遥测字段** —— 目标类型是
   `ModApi.Flight.IFlightScene`、`ModApi.Craft.ICraftNode`、
   `ModApi.Craft.ICraftFlightData`、`ModApi.Flight.Sim.IOrbit`、
   `ModApi.Craft.ICraftOrbitData`、`ModApi.Planet.IPlanetData`。
