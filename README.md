# N.E.K.O-Juno-bridge

> 给《朱诺新起源 Juno: New Origins》做的**只读飞行遥测桥接**。
> 让 AI 副驾驶（猫娘）能读懂你正在飞的飞船，并陪着聊天给建议。

发布名：**Neko Uplink** · 版本 **0.1.0** · 许可证 **GPL-3.0**

---

## 这是什么

飞行时的数据都在游戏里：高度、速度、轨道、燃料、Delta-V。
但它们散在十几个仪表和地图界面，玩家得自己拼起来看 ——
**而他还得同时把飞船开好**。

这个模组把这些数据整理成外部程序能读的形式，让 AI 能**基于真实数字**开口，
而不是瞎猜。

## 定位：只读

**不发控制指令、不动油门、不碰姿态、不写存档。** 操纵杆始终在玩家手里。

这不是妥协，是设计承诺 —— 让玩家能安心挂着，不用担心后台程序替他做了什么。

> 模组只提供 `GET` 接口，代码里不存在任何修改游戏状态的路径。

## 为什么用 BepInEx（和 KSP 那个项目不同）

姊妹项目 `N.E.K.O-KSP-bridge` 是一个纯 KSP 官方模组，**零前置依赖**。
这个项目做不到同样的事，原因很具体：

- 朱诺的官方 mod 必须是 Unity **AssetBundle**（扩展名 `.sr2-mod`），
  AssetBundle 只能由 **Unity 编辑器 + 官方 `SimpleRockets2_ModTools`** 构建；
- 没有任何脱离 Unity 的办法能产出合法的 AssetBundle；
- 官方加载器还要求程序集以 asset 的形式存在于 bundle 里
  （`ModManagerBase.LoadModAssemblies`：*Unable to find assembly asset ...*），
  并且在部分版本上直接禁用 mod 代码执行。

所以这里走社区通用的 **BepInEx 注入**：它**不修改游戏原文件**，
只是在游戏目录里多放几个文件（`winhttp.dll` + `BepInEx\`），
卸载就是把这些删掉。

**前置依赖：BepInEx 5.4.23.5 (win x64)** —— 安装脚本会自动装好。

---

## 安装

```powershell
# 1) 装 BepInEx + 本模组（会自动下载/展开 BepInEx 并编译插件）
.\install.ps1

# 指定游戏目录
.\install.ps1 -GameDir "D:\Games\SimpleRockets2"
```

脚本做了什么：

| 动作 | 位置 |
| --- | --- |
| 放 BepInEx 加载器 | `<游戏>\winhttp.dll`、`doorstop_config.ini`、`.doorstop_version` |
| 放 BepInEx 运行时 | `<游戏>\BepInEx\` |
| 编译并安装插件 | `<游戏>\BepInEx\plugins\N.E.K.O-Juno-bridge\JunoBridge.dll` |

**卸载**：删掉上面三个文件与 `BepInEx` 文件夹即可。游戏本体文件一个都没动过。

启动游戏后验证：

```powershell
curl http://127.0.0.1:21577/ping
```

默认端口 **21577**，**只监听 `127.0.0.1`**，不对局域网开放。

---

## 接口（12 个，全部 GET）

| 接口 | 内容 |
| --- | --- |
| `/ping` | 存活检查 + 版本 + 是否在飞行 |
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

## 实测数据

在真实飞行中采集（游戏 1.4.105，飞船停在 **Droo** 的 Juno Village Pad）：

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

因为是只读，HTTP 线程可以独立应答，不需要回主线程排队。

> ⚠️ **将来如果要加控制接口**（分级 / 油门 / 姿态），必须改成
> 「HTTP 线程入队 → 主线程 `Update` 里执行」。
> 在后台线程调用 Unity API 会直接让游戏崩 —— 姊妹项目踩过这个坑。

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

## 配置

`<游戏>\BepInEx\plugins\N.E.K.O-Juno-bridge\neko-juno-bridge.ini`
（不存在会自动生成）

```ini
port=21577              # HTTP 端口，只监听 127.0.0.1
poll_interval_ms=200    # 遥测采集间隔，50-60000
log_to_file=true        # 写 juno-bridge.log（超 2 MB 自动截断）
```

> 改端口要**同时**改 N.E.K.O. 插件配置里的 `bridge_url`，只改一处两边就连不上。

---

## 已知限制

1. **只读** —— 不能控制飞船。这是**有意设计**，不是缺陷。
2. **依赖 BepInEx** —— 见上文「为什么用 BepInEx」。
3. **游戏更新后可能需要重新适配** —— 遥测依赖游戏内部结构
   （`ModApi` 接口与 `FlightSceneScript`）。
4. **质量单位未标定** —— `mass` 是游戏内部数值，与界面显示的量级对不上，
   需要进一步确认。
5. **未做建造场景（设计器）的数据读取** —— 只读飞行场景。
6. **不做 Delta-V 规划** —— 只报游戏当前计算出的本级 Delta-V。

---

## 目录结构

```
juno-bridge/
├── src/
│   ├── Mod.cs            BepInEx 入口、配置
│   ├── GameAccess.cs     飞行场景获取（名称查找 + 缓存）
│   ├── Telemetry.cs      遥测采集（主线程）与快照渲染
│   ├── BridgeServer.cs   HTTP 服务（手写 TcpListener）
│   ├── BridgeJson.cs     JSON 拼装（NaN/Inf -> null）
│   ├── BridgeLog.cs      双写日志
│   └── DebugProbe.cs     ★ 调试探针，仅在 -Debug 构建中存在，正式包不含
├── docs/
│   ├── samples/          实机抓取的真实响应样本（供测试用）
│   └── interface.md      接口字段详解
├── tools/                反射探测、字符串提取、构建辅助（见 tools/README.md）
├── build.ps1             编译（可 -Install / -Debug）
├── install.ps1           一键装 BepInEx + 本模组
└── dist/                 JunoBridge.dll
```

### 关于 `DebugProbe.cs`

开发期为了让**无人值守也能自动跑进飞行场景做验证**而加：
它通过「HTTP 线程入队 → 主线程执行」的方式驱动游戏 UI，
并用条件编译 `NEKO_DEBUG` 隔离。

**正式构建（`build.ps1` 不带 `-Debug`）不包含它的任何代码** ——
可以用字符串扫描确认产物里不含 `debug/tap`、`TapByName` 等符号。
正式包**只有只读接口**，不存在任何能改变游戏状态的端点。

---

*星河拓航Studio (Galaxy Exploration Studio) · 猫娘计划*
*<https://xhth.top/> · GPL-3.0*
