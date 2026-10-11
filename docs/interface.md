# N.E.K.O-Juno-bridge 接口详解

所有接口都是 `GET`，返回 `application/json; charset=utf-8`，
只监听 `127.0.0.1:21577`（端口可配置）。

**约定**

- 拿不到的数字一律是 `null`，不会是 `0`，也不会是 `NaN`/`Infinity` 之类的非法 JSON。
- 不在飞行场景时，除了 `/ping`、`/config`、`/log`、`/bodies`、`/vessels` 之外的
  接口都返回 `{"ok":false,"in_flight":false,"error":"..."}` —— 这是**正常状态**。
- 单位见 README 的「单位约定」一节。

---

## `/ping`

存活检查。游戏没开时请求会直接连接失败。

```json
{
  "ok": true,
  "bridge": "N.E.K.O-Juno-bridge",
  "version": "0.2.0",
  "mode": "read-write",
  "control": true,
  "in_flight": true,
  "port": 21577,
  "game": "1.4.105"
}
```

## `/state`

一次性拿到全部数据，是消费者最常用的接口。

```json
{
  "ok": true,
  "ts": 227.165,
  "in_flight": true,
  "bridge_version": "0.2.0",
  "scene":  { ... },     // 同 /scene
  "vessel": { ... },     // 同 /vessel
  "orbit":  { ... },     // 同 /orbit
  "flight": { ... },     // 同 /flight
  "resources": { ... },  // 同 /resources
  "target": { ... }      // 同 /target
}
```

`ts` 是游戏进程启动以来的秒数（`Time.realtimeSinceStartup`），**不是**游戏内时间。

## `/scene`

```json
{
  "ok": true,
  "situation": "LANDED",
  "body": "Droo",
  "craft_name": "New",
  "paused": false,
  "warp_mode": "1x",
  "warp_multiplier": 1,
  "warp_index": 2,
  "game_time": 24.05
}
```

`situation` 取值：`LANDED` / `SPLASHED` / `FLYING` / `SUB_ORBITAL` / `ORBITING` / `ESCAPING`。

## `/vessel`

```json
{
  "ok": true,
  "name": "New",
  "situation": "LANDED",
  "body": "Droo",
  "node_id": 2,
  "is_player": true,
  "altitude": 2779.194,
  "altitude_agl": 7.4445,
  "altitude_terrain": 7.4445,
  "sea_level_altitude": 2779.194,
  "mass": 0.0418,
  "current_mass": 0.0418,
  "part_count": 1,
  "has_command_pod": true,
  "grounded": true,
  "in_water": false,
  "in_contact_with_planet": true,
  "latitude": -2.8699,
  "longitude": 0.4468,
  "heading": 248.628,
  "pitch": 89.980,
  "bank": -0.0065,
  "angle_of_attack": 3.376,
  "side_slip": 6.879,
  "atmosphere_height": 78146.448,
  "command_pod_count": 1,
  "crew_count": 1,
  "current_stage": 0,
  "stage_count": 1
}
```

- `altitude` 相对海平面；`altitude_agl` / `altitude_terrain` 相对地面。
- `mass` 与 `current_mass` 是**游戏内部单位，未标定**。
- `crew_count` 是「有人的载具单元数」（Juno 没有 KSP 那样的独立乘员名单）。

## `/orbit`

```json
{
  "ok": true,
  "body": "Droo",
  "body_radius": 1274200,
  "atmosphere_height": 78146.448,
  "apoapsis_altitude": 0,
  "periapsis_altitude": 0,
  "apoapsis_distance": 1276979.194,
  "periapsis_distance": 1052.739,
  "eccentricity": 0,
  "inclination": 0,
  "period": 0,
  "semi_major_axis": 639015.967,
  "semi_minor_axis": 36665.056,
  "is_valid": true,
  "orbit_type": "Elliptical",
  "is_stable_orbit": false,
  "time_to_apoapsis": null,
  "time_to_periapsis": null,
  "true_anomaly": 3.14159,
  "mean_anomaly": 3.14159,
  "primary_mass": 2.3836e+23
}
```

- `*_altitude` 是**相对地表**的高度；`*_distance` 是**相对天体中心**的距离。
- **`is_stable_orbit`**：近点是否高过该天体真实的大气层顶
  （且离心率 < 1、近点 > 0）。这是最该被消费者利用的一个字段。
- 双曲轨道时 `eccentricity >= 1`，`orbit_type` 为 `Hyperbolic`。
- 天体没有大气时 `atmosphere_height` 为 `0`。

## `/flight`

```json
{
  "ok": true,
  "surface_speed": 7.9e-07,
  "orbital_speed": 143.258,
  "vertical_speed": -7.8e-07,
  "lateral_speed": 1.05e-07,
  "altitude_agl": 7.4445,
  "altitude_terrain": 7.4445,
  "gravity": 9.7554,
  "drag_acceleration": 0,
  "acceleration": 9.59e-06,
  "mach": 2.34e-09,
  "air_density": 0.8648,
  "air_pressure": 71905.10,
  "temperature": 279.648,
  "speed_of_sound": 341.153,
  "thrust": 0,
  "max_thrust": 0,
  "fuel_mass": 0
}
```

- `surface_speed` 相对地面；`orbital_speed` 相对天体（惯性系）。
- `air_pressure` 单位是 **Pa**；`temperature` 单位是 **K**；
  `air_density` 是 **kg/m³**。

## `/resources`

```json
{
  "ok": true,
  "fuel_mass": 0,
  "fuel_in_stage": 0,
  "monopropellant": 0,
  "battery": 0.9997,
  "delta_v_stage": 0,
  "isp": 0,
  "thrust_to_weight": 0,
  "burn_time_remaining": 0,
  "fuel_all_stages_percent": 0
}
```

- `battery` 与 `fuel_all_stages_percent` 是 **0–1 归一化**。
- `delta_v_stage` 是**当前级**的 Delta-V；`burn_time_remaining` 单位秒。
- 质量类字段同样是**未标定**的游戏内部单位。

## `/target`

未锁定目标时：

```json
{"ok": true, "locked": false}
```

锁定后：

```json
{
  "ok": true,
  "locked": true,
  "name": "...",
  "kind": "vessel",
  "distance": 12345.6,
  "relative_speed": 123.4,
  "parent": "Droo",
  "has_orbit": true,
  "apoapsis_distance": 1300000,
  "periapsis_distance": 1280000
}
```

`kind` 取值：`vessel` / `body` / `other`。

## `/bodies`

数组，每个元素：

```json
{
  "name": "Droo",
  "radius": 1274200,
  "mass": 2.3836e+23,
  "surface_gravity": 9.798,
  "escape_velocity": 4937.5,
  "has_atmosphere": true,
  "atmosphere_height": 78146.448,
  "has_water": true,
  "parent": "Juno",
  "sphere_of_influence": 0
}
```

- 恒星（如 `Juno`）的 `parent` 是 `null`。
- 没有大气时 `has_atmosphere` 为 `false`、`atmosphere_height` 为 `0`。

## `/vessels`

数组，当前场景里所有飞船：

```json
[{"name":"New","node_id":2,"is_player":true,"altitude":2779.194,
  "mass":0.0418,"part_count":1,"crew_count":1}]
```

## `/config`

```json
{
  "ok": true,
  "port": 21577,
  "poll_interval_ms": 200,
  "log_to_file": true,
  "control_enabled": true,
  "read_only": false,
  "config_path": "...\\neko-juno-bridge.ini",
  "log_path": "...\\juno-bridge.log"
}
```

## `/command`

控制指令入口（`GET`，参数走 query）。**命令只入队**，真正执行在主线程下一帧，
因此返回的是「已入队」状态 + 当前控制快照，不等于「飞船已经动了」。

### 动作（action）

| action | 参数 | 说明 |
| --- | --- | --- |
| `stage` | — | 激活下一级（等效按空格） |
| `hold` | `axis`,`value` | 把某轴设成 setpoint 并**持续保持**（每帧写回），直到 `zero`/`cut` |
| `ag_set` | `group`,`state`(0/1) | 设置激活组开关（group 1–10） |
| `ag_toggle` | `group` | 切换激活组 |
| `trans_mode` | `state`(0/1) | 平移模式开关 |
| `zero` | — | 清零所有 held setpoint + 刹车，交还操纵权 |
| `cut` | — | 油门归零 + `zero`（紧急松手） |

`axis` 取值：`throttle`(0–1) / `pitch` / `yaw` / `roll` / `translate_forward` /
`translate_right` / `translate_up` / `brake`(0–1)，后六个为 -1–1。

### 示例

```
# 点火前把油门推到 80%
/command?action=hold&axis=throttle&value=0.8
# 持续约 10° 仰角（setpoint，每帧保持）
/command?action=hold&axis=pitch&value=0.17
# 分级
/command?action=stage
# 激活组 1 打开
/command?action=ag_set&group=1&state=1
# 松手，交还人工操纵
/command?action=zero
```

### 返回

```json
{"ok":true,"action":"hold","queued":true,"engaged":true,
 "controls":{"ok":true,"engaged":true,"enabled":true,"in_flight":true,
   "is_player_controlled":true,"current_stage":0,"num_stages":3,
   "translation_mode":false,
   "held":{"throttle":0.8,"pitch":0.17,"yaw":0.0,"roll":0.0,
     "translate_forward":0.0,"translate_right":0.0,"translate_up":0.0,"brake":0.0},
   "last_error":null}}
```

`control_enabled=0` 时任何 `/command` 都返回 `{"ok":false,"error":"control disabled ..."}`。

## `/controls`

只读当前控制状态（等价于 `/command` 返回里的 `controls` 字段），用于回显副驾驶此刻
到底握着哪些 setpoint、是否 engaged、激活组是否开启。不修改任何游戏状态。

## `/log?n=100`

```json
{"ok": true, "lines": ["2026-10-10 00:46:52 [INFO ] ...", ...]}
```

`n` 默认 100，上限 2000。

---

## 一个最小消费者示例（PowerShell）

```powershell
$s = (Invoke-WebRequest http://127.0.0.1:21577/state -UseBasicParsing).Content | ConvertFrom-Json
if (-not $s.ok) { Write-Host "读不到：$($s.error)"; return }
$v = $s.vessel; $o = $s.orbit
"{0} 在 {1}，海拔 {2:N0} m，地表速度 {3:N1} m/s" -f $v.name, $v.body, $v.altitude, $s.flight.surface_speed
if ($o.is_stable_orbit) { "已进入稳定轨道" }
elseif ($o.periapsis_altitude -lt $o.atmosphere_height) {
    "近点 {0:N0} m 低于大气层顶 {1:N0} m —— 还会被拖下来" -f $o.periapsis_altitude, $o.atmosphere_height
}
```
