# N.E.K.O-Juno-bridge

> 给《朱诺新起源 Juno: New Origins》（原名 SimpleRockets 2）做的**飞行遥测桥接**。
> 让 AI 副驾驶（猫娘）能读懂你正在开的火箭，并陪着聊天给建议。

发布名：**Neko Uplink** · 版本 **0.2.0** · 许可证 **GPL-3.0**

配套的 N.E.K.O. 插件：
[`n.e.k.o_plugin_juno_bridge`](https://github.com/LShangPiao/n.e.k.o_plugin_juno_bridge)

---

## 这是什么

飞行时的数据都在游戏里：海拔、速度、姿态、轨道根数、燃料、电量、大气、推力。
但它们散在 HUD 和各个面板上，玩家得自己拼起来看 ——
**而他还得同时把火箭开好**。

这个模组把这些数据整理成外部程序能读的形式，让 AI 能**基于真实数字**开口，
而不是瞎猜。

## 定位：遥测只读，控制需显式开启

**遥测全部是 `GET`，只读。** 控制走单独的 `/command`，且默认可以关掉。

- 只读部分：不发任何指令，不碰飞船
- 控制部分：**要发命令才生效**，松手用 `zero` / `cut` 交还操纵权
- 在 `neko-juno-bridge.ini` 里设 `control_enabled=0` 即**回到纯只读**，
  控制端点直接拒绝

> 这样做是为了让玩家能安心挂着 —— 不主动下命令，模组什么都不会做。

## 现状：已在实机跑通 ✅

**这个模组已经在游戏 1.4.105 上实测通过**，链路是：

```
N.E.K.O. 插件  ←HTTP/JSON(21577)←  Juno-bridge  ←ModApi←  Juno: New Origins
```

实机确认的情况：

- ✅ BepInEx 5（Mono x64）能正常加载本模组，`/ping` 与 `/state` 均返回正常
- ✅ 飞行场景获取、飞船状态、轨道根数、大气数据**全部读到真实值**
- ✅ 22 个天体、单位换算、入轨判断均已核对
- ✅ Release 构建会把 `/probe` 诊断端点整块条件编译掉
  （可用字符串扫描确认产物里不含 `debug/tap`、`TapByName` 等符号）

### 实测数据

在真实飞行中采集（飞船停在 **Droo** 的 Juno Village Pad）：

```
飞船「New」  LANDED  在 Droo
  海拔 2,779.19 m   对地高度 7.44 m   俯仰 89.98°   航向 248.63°
  质量 0.0418（游戏内部单位）
  大气：气压 71,905 Pa   密度 0.865 kg/m³   气温 279.65 K（6.5 ℃）   音速 341.15 m/s
  重力 9.755 m/s²   阻力 0   零件 1
  电量 99.97%
  轨道：离心率 0.0000  周期 0    稳定轨道：否
```

`/bodies` 返回全部 **22 个天体**：

| 天体 | 半径 | 表面重力 | 大气顶 | 母体 |
| --- | --- | --- | --- | --- |
| Juno | 50,000,000 m | 50.0 | — | — |
| Droo | 1,274,200 m | 9.798 | 78.15 km | Juno |
| Vulco | 634,672 m | 6.0 | 21.35 km | Juno |
| Sergeaa | 1,036,351 m | 8.0 | 323.02 km | Juno |
| Oord | 500,000 m | 5.0 | 114.85 km | Sergeaa |
| Brigo | 213,000 m | 1.6 | 无 | Droo |

> **注意**：这不是真实尺度，也不是 KSP 的尺度。
> 入轨看的是**近点是否高过该天体真实的大气层顶**（Droo 是 78.15 km，不是 70 km）。
> 一定要运行时读 `atmosphere_height`，不要凭常识猜。

---

## 安装

### 方式一：解压即用（推荐）

从 [Releases](https://github.com/LShangPiao/N.E.K.O-Juno-bridge/releases) 下载

```
N.E.K.O-Juno-Bridge-v0.2.0-win-x64.zip
```

**把 zip 里的东西全部解压到游戏根目录**（和 `SimpleRockets2.exe` 同级）：

```
E:\Steam\steamapps\common\SimpleRockets2\
```

解压完成后目录应该是这样：

```
SimpleRockets2\
├── SimpleRockets2.exe
├── winhttp.dll              ← 新加的（BepInEx 加载器）
├── doorstop_config.ini      ← 新加的
├── .doorstop_version        ← 新加的
├── BepInEx\                 ← 新加的
│   ├── core\                    BepInEx 运行时
│   └── plugins\
│       └── N.E.K.O-Juno-bridge\
│           └── JunoBridge.dll   ← 本模组
└── SimpleRockets2_Data\
```

> **zip 里已经带了 BepInEx 5.4.23.5（Mono x64）**，不用另外下载。
> 校验和见同目录的 `SHA256SUMS.txt`。

然后**重启游戏**即可。

### 方式二：已经装过 BepInEx

只需把 `BepInEx\plugins\N.E.K.O-Juno-bridge\` 整个目录拷进游戏的 `BepInEx\plugins\`。

### 方式三：从源码构建

```powershell
# 准备 BepInEx（见 tools/README.md）

.\build.ps1 -GameDir "E:\Steam\steamapps\common\SimpleRockets2"   # 只编译
.\install.ps1 -GameDir "E:\Steam\steamapps\common\SimpleRockets2" # 编译 + 安装
.\package.ps1 -Version 0.2.0                                      # 打解压即用的 zip
```

### 验证

启动游戏后：

```powershell
curl http://127.0.0.1:21577/ping
```

默认端口 **21577**，**只监听 `127.0.0.1`**，不对局域网开放。

### 卸载

删掉 `winhttp.dll`、`doorstop_config.ini`、`.doorstop_version` 和 `BepInEx\` 目录即可。
**模组从不修改任何游戏原文件。**

---

## 接口（14 个；遥测全 GET，控制用 /command）

| 接口 | 内容 |
| --- | --- |
| `/ping` | 存活检查 + 版本 + 是否在飞行 + 是否可控制 |
| `/state` | **一次性拿到全部数据** |
| `/vessel` | 飞船基础信息与姿态 |
| `/orbit` | 轨道根数与入轨判断 |
| `/flight` | 速度、大气、受力 |
| `/resources` | 燃料 / 电量 / Delta-V / 推重比 |
| `/target` | 锁定目标 |
| `/bodies` | 星系全部天体 |
| `/vessels` | 本次飞行里的所有飞船 |
| `/scene` | 场景与时间加速状态 |
| `/config` | 当前配置 |
| `/command?action=stage` | **控制**：分级 / 油门 / 姿态 / 平移 / 激活组（发命令才生效） |
| `/controls` | 当前控制状态（engaged / held setpoint / 激活组） |
| `/log?n=100` | 最近日志 |

浏览器直接打开 `http://127.0.0.1:21577/` 有一份索引页。

**这是公用接口** —— 不依赖 N.E.K.O.，任何程序（网页、仪表盘、Discord 机器人）
都能读它。N.E.K.O. 的插件只是第一个消费者。

不在飞行场景时，接口返回：

```json
{"ok":false,"in_flight":false,"error":"当前不在飞行场景（主菜单、设计器或加载中）"}
```

这是**正常状态**，不是错误 —— 消费者应当据此告诉用户「现在读不到」，
而不是编造数字。

---

## 配置

`<游戏>\BepInEx\plugins\N.E.K.O-Juno-bridge\neko-juno-bridge.ini`
（不存在会自动生成，**改完要重启游戏**）

```ini
port=21577              # HTTP 端口，只监听 127.0.0.1
poll_interval_ms=200    # 遥测采集间隔，50-60000
log_to_file=true        # 写 juno-bridge.log（超 2 MB 自动截断）
control_enabled=true    # 是否开放控制接口（/command、/controls）；设 0 回到纯只读
```

> 改端口要**同时**改 N.E.K.O. 插件配置里的 `bridge_url`，只改一处两边就连不上。

---

## 单位约定

| 字段 | 单位 |
| --- | --- |
| 高度 / 距离 / 半径 / `atmosphere_height` | 米 |
| 速度 / 音速 | m/s |
| 角度（航向/俯仰/倾角/攻角等） | 度 |
| 周期 / 剩余时间 / 游戏时间 | 秒 |
| `air_pressure` | **帕斯卡 Pa**（实测 2779 m 处 71905 Pa，对照海平面 ~101325 Pa） |
| `air_density` | **kg/m³** |
| `temperature` | **开尔文 K**（实测 279.65 K = 6.5 ℃） |
| `battery` / `fuel_all_stages_percent` | **0–1 归一化** |
| `mass` / `current_mass` / `fuel_mass` | **游戏内部单位，未标定**（疑为吨，未确认） |

NaN / Infinity 一律输出 `null`，不会有非法 JSON。

---

## 技术要点

### 线程模型（核心）

```
主线程   BridgeRunner.Update() -> Telemetry.Capture() 把快照渲染成 JSON 字符串
后台线程 HTTP 服务只读那些字符串，完全不碰任何 Unity 对象
```

控制加入后，HTTP 线程改为「只入队」，命令在主线程 `Pump` 里出队执行 ——
与既有遥测采集共用同一条主线程循环。

> ✅ **控制接口已实现**：`ControlApi` 严格遵循「HTTP 线程入队 → 主线程 `Update` 执行」，
> 持续型 setpoint 每帧写回 `CraftControls`，因此从后台线程碰 Unity API 的坑被彻底避开。

### 生命周期（这个游戏特有的坑）

BepInEx 挂在它自己 manager 对象上的插件组件，在**启动场景切换时会被销毁** ——
`Start()` / `Update()` 永远不会被派发。实测表现是 `Awake()` 正常执行、
3 秒后 `OnDestroy()` 被调用。

因此这个模组做了两件事：

1. **HTTP 服务由独立后台线程持有**，不随任何 Unity 对象消亡；
2. 遥测采集挂在一个**自己创建、显式 `DontDestroyOnLoad` 的运行器**上。

### 手写 HTTP 与手写 JSON

Unity 的 Mono 对 `System.Net.HttpListener` 支持不稳，而需求极简
（只读、GET、JSON），所以直接用原始 `TcpListener`。
JSON 也手写，输出结构固定、可控。

### 场景获取

游戏把飞行场景实现在 `Assets.Scripts.Flight.FlightSceneScript`（一个
`MonoBehaviour`，同时实现 `ModApi.Flight.IFlightScene`）。
模组按**类型名字符串**去找它，并缓存结果 —— 即使将来官方改名，
也只是找不到场景（返回离线状态），不会影响插件加载。

---

## 已知限制

1. **控制会覆盖玩家同轴的手动输入** —— 松手请用 `zero` / `cut` 交还操纵权；
   设 `control_enabled=0` 可完全关闭控制。
2. **依赖 BepInEx** —— 见下文「为什么用 BepInEx」。
3. **游戏更新后可能需要重新适配** —— 遥测依赖游戏内部结构
   （`ModApi` 接口与 `FlightSceneScript`）。
4. **质量单位未标定** —— `mass` 是游戏内部数值，与界面显示的量级对不上，
   需要进一步确认。
5. **未做建造场景（设计器）的数据读取** —— 只读飞行场景。
6. **不做 Delta-V 规划** —— 只报游戏当前计算出的本级 Delta-V。

---

## 为什么用 BepInEx

Juno: New Origins（原名 SimpleRockets 2）确实有官方 mod 体系，但它有**硬性工具链要求**：

- 官方 `.sr2-mod` 必须用 **Unity 编辑器 + 官方 ModTools** 把资源打成 **AssetBundle**
  才能构建，它不是「丢一个 .dll 就能跑」的形式；
- 也就是说，做官方 mod 要求作者装一整套 Unity + ModTools，而且构建产物和
  Unity 版本强绑定；
- 本项目要的只是一个**本地 HTTP 遥测接口**，不需要往游戏里加零件、贴图或
  Vizzy 积木，用 AssetBundle 那套流程纯粹是负担。

所以这里选择 **BepInEx 5.4.23.5（x64）插件**这条路：

- Juno 是 Unity **Mono** 后端（不是 IL2CPP）→ 普通 C# 插件直接就能用
- BepInEx **不修改任何游戏原文件**，只是在目录里多放几个文件
- 卸载就是删掉那几个文件

⚠️ 必须用 **x64** 版（Juno 是 64 位 Unity 游戏），x86 的 BepInEx 不会生效
（本项目的 release zip 里已经带好了正确的那一份）。

---

## 目录结构

```
juno-bridge/
├── src/
│   ├── Mod.cs            BepInEx 入口、配置
│   ├── GameAccess.cs     飞行场景获取（名称查找 + 缓存）
│   ├── Telemetry.cs      遥测采集（主线程）与快照渲染
│   ├── BridgeServer.cs   HTTP 服务（手写 TcpListener，含 /command /controls）
│   ├── ControlApi.cs     ★ 控制接口（0.2.0 新增）：队列 + 主线程执行
│   ├── BridgeJson.cs     JSON 拼装（NaN/Inf -> null）
│   ├── BridgeLog.cs      双写日志
│   └── DebugProbe.cs     ★ 调试探针，仅在 -Debug 构建中存在，正式包不含
├── docs/
│   ├── samples/          实机抓取的真实响应样本（供测试用）
│   └── interface.md      接口字段详解
├── tools/                反射探测、字符串提取、构建辅助（见 tools/README.md）
├── build.ps1             编译（可 -Install / -Debug）
├── install.ps1           一键装 BepInEx + 本模组
├── package.ps1           打解压即用的 zip
└── dist/                 JunoBridge.dll
```

### 关于 `DebugProbe.cs`

开发期为了让**无人值守也能自动跑进飞行场景做验证**而加：
它通过「HTTP 线程入队 → 主线程执行」的方式驱动游戏 UI，
并用条件编译 `NEKO_DEBUG` 隔离。

**正式构建（`build.ps1` 不带 `-Debug`）不包含它的任何代码**。
正式包包含**只读遥测 + 控制接口**；`control_enabled=0` 时控制端点直接拒绝，
回到纯只读。

---

## 许可证

GNU General Public License v3.0，见 [LICENSE](LICENSE)。

*N.E.K.O-Juno-bridge · 猫娘计划 · GPL-3.0*
