// N.E.K.O-Juno-bridge — 遥测采集
//
// 职责分工（和 KSP 那边保持一致）：
//   模组负责   —— 从游戏里把数字抠出来，输出**原始 JSON**
//   插件负责   —— 把数字压成可以直接说的人话（summary）
//
// 所以这个文件里没有中文摘要，只有干净的数值和少量判断
// （例如 is_stable_orbit / situation）。
//
// 线程模型：
//   主线程   Capture() 把每个接口的 JSON 全部渲染成字符串存进字段
//   HTTP线程 只读这些字符串，**完全不碰任何 Unity / 游戏对象**
//
// 所有取数都包在 try/catch 里：游戏在加载中、主菜单、或者某个字段
// 恰好在某个阶段为空时，只会让那一个字段变成 null，不会连累整份快照。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ModApi.Craft;
using ModApi.Craft.Parts;
using ModApi.Flight;
using ModApi.Flight.Sim;
using ModApi.Flight.UI;
using ModApi.Planet;
using UnityEngine;

namespace NekoJunoBridge
{
    public static class Telemetry
    {
        private static readonly object Gate = new object();

        private static string offlineReason = "尚未连接游戏";
        private static string stateJson = "{\"ok\":false,\"error\":\"尚未连接游戏\"}";
        private static string vesselJson = "{}";
        private static string orbitJson = "{}";
        private static string flightJson = "{}";
        private static string resourcesJson = "{}";
        private static string targetJson = "{}";
        private static string bodiesJson = "[]";
        private static string vesselsJson = "[]";
        private static string sceneJson = "{}";

        private static bool lastInFlight;

        public static string StateJson { get { lock (Gate) { return stateJson; } } }
        public static string VesselJson { get { lock (Gate) { return vesselJson; } } }
        public static string OrbitJson { get { lock (Gate) { return orbitJson; } } }
        public static string FlightJson { get { lock (Gate) { return flightJson; } } }
        public static string ResourcesJson { get { lock (Gate) { return resourcesJson; } } }
        public static string TargetJson { get { lock (Gate) { return targetJson; } } }
        public static string BodiesJson { get { lock (Gate) { return bodiesJson; } } }
        public static string VesselsJson { get { lock (Gate) { return vesselsJson; } } }
        public static string SceneJson { get { lock (Gate) { return sceneJson; } } }
        public static bool InFlight { get { lock (Gate) { return lastInFlight; } } }
        public static string OfflineReason { get { lock (Gate) { return offlineReason; } } }

        /// <summary>**只能在主线程调用。**</summary>
        public static void Capture()
        {
            try
            {
                GameAccess.Tick();
                IFlightScene scene = GameAccess.Scene;
                if (scene == null)
                {
                    SetOffline("当前不在飞行场景（主菜单、设计器或加载中）");
                    return;
                }

                ICraftNode craft = null;
                IFlightState flightState = null;
                ITimeManager timeManager = null;
                try { craft = scene.CraftNode; }
                catch { }
                try { flightState = scene.FlightState; }
                catch { }
                try { timeManager = scene.TimeManager; }
                catch { }

                if (craft == null)
                {
                    SetOffline("飞行场景已就绪，但还没有可读取的飞船");
                    return;
                }

                ICraftScript script = null;
                ICraftFlightData flight = null;
                ICraftOrbitData craftOrbit = null;
                ICraftPerformanceData performance = null;
                try { script = craft.CraftScript; } catch { }
                try { if (script != null) { flight = script.FlightData; } } catch { }
                try { if (flight != null) { craftOrbit = flight.Orbit; } } catch { }
                try { if (flight != null) { performance = flight.Performance; } } catch { }

                IOrbit orbit = null;
                try { orbit = craft.Orbit; } catch { }

                IPlanetNode bodyNode = null;
                try { bodyNode = craft.Parent; } catch { }
                IPlanetData body = null;
                try { if (bodyNode != null) { body = bodyNode.PlanetData; } } catch { }

                double? atmosphereHeight = null;
                try
                {
                    if (body != null && body.AtmosphereData != null)
                    {
                        atmosphereHeight = body.AtmosphereData.Height;
                    }
                }
                catch { }

                string situation = InferSituation(flight, craftOrbit, body, atmosphereHeight);

                double gameTime = 0;
                try { if (flightState != null) { gameTime = flightState.Time; } } catch { }

                bool paused = false;
                string warpMode = null;
                double warpMultiplier = 1;
                int warpIndex = 0;
                try
                {
                    if (timeManager != null)
                    {
                        paused = timeManager.Paused;
                        warpIndex = timeManager.ModeIndex;
                        ITimeMultiplierMode mode = timeManager.CurrentMode;
                        if (mode != null)
                        {
                            warpMode = mode.Name;
                            warpMultiplier = mode.TimeMultiplier;
                        }
                    }
                }
                catch { }

                string sceneBlock = BuildScene(craft, body, situation, paused, warpMode, warpMultiplier, warpIndex, gameTime);
                string vesselBlock = BuildVessel(craft, flight, body, situation, atmosphereHeight);
                string orbitBlock = BuildOrbit(craftOrbit, orbit, craft, body, atmosphereHeight);
                string flightBlock = BuildFlight(flight);
                string resourcesBlock = BuildResources(flight, performance);
                string targetBlock = BuildTarget(flight);
                string bodiesBlock = BuildBodies(flightState, body);
                string vesselsBlock = BuildVessels(flightState, craft);

                BridgeJson.Writer state = new BridgeJson.Writer();
                state.StartObject()
                    .Key("ok").Boolean(true)
                    .Key("ts").Number(Now())
                    .Key("in_flight").Boolean(true)
                    .Key("bridge_version").Text(JunoBridgePlugin.PluginVersion)
                    .Key("scene").Value(sceneBlock)
                    .Key("vessel").Value(vesselBlock)
                    .Key("orbit").Value(orbitBlock)
                    .Key("flight").Value(flightBlock)
                    .Key("resources").Value(resourcesBlock)
                    .Key("target").Value(targetBlock)
                    .EndObject();

                lock (Gate)
                {
                    sceneJson = sceneBlock;
                    vesselJson = vesselBlock;
                    orbitJson = orbitBlock;
                    flightJson = flightBlock;
                    resourcesJson = resourcesBlock;
                    targetJson = targetBlock;
                    bodiesJson = bodiesBlock;
                    vesselsJson = vesselsBlock;
                    stateJson = state.ToString();
                    offlineReason = null;
                    lastInFlight = true;
                }
            }
            catch (Exception ex)
            {
                SetOffline("采集遥测时出错: " + BridgeLog.Safe(ex));
            }
        }

        private static void SetOffline(string reason)
        {
            string json = "{\"ok\":false,\"in_flight\":false,\"error\":" + BridgeJson.Str(reason)
                + ",\"bridge_version\":" + BridgeJson.Str(JunoBridgePlugin.PluginVersion) + "}";
            lock (Gate)
            {
                offlineReason = reason;
                stateJson = json;
                sceneJson = json;
                vesselJson = "{\"ok\":false,\"error\":" + BridgeJson.Str(reason) + "}";
                orbitJson = vesselJson;
                flightJson = vesselJson;
                resourcesJson = vesselJson;
                targetJson = vesselJson;
                bodiesJson = "[]";
                vesselsJson = "[]";
                lastInFlight = false;
            }
        }

        // ------------------------------------------------------------------
        // 各接口的构造
        // ------------------------------------------------------------------

        private static string BuildScene(ICraftNode craft, IPlanetData body, string situation,
            bool paused, string warpMode, double warpMultiplier, int warpIndex, double gameTime)
        {
            BridgeJson.Writer w = new BridgeJson.Writer();
            w.StartObject()
                .Key("ok").Boolean(true)
                .Key("situation").Text(situation)
                .Key("body").Text(TryS(() => body.Name))
                .Key("craft_name").Text(TryS(() => craft.Name))
                .Key("paused").Boolean(paused)
                .Key("warp_mode").Text(warpMode)
                .Key("warp_multiplier").Number(warpMultiplier)
                .Key("warp_index").Integer(warpIndex)
                .Key("game_time").Number(gameTime)
                .EndObject();
            return w.ToString();
        }

        private static string BuildVessel(ICraftNode craft, ICraftFlightData flight, IPlanetData body,
            string situation, double? atmosphereHeight)
        {
            BridgeJson.Writer w = new BridgeJson.Writer();
            w.StartObject()
                .Key("ok").Boolean(true)
                .Key("name").Text(TryS(() => craft.Name))
                .Key("situation").Text(situation)
                .Key("body").Text(TryS(() => body.Name))
                .Key("node_id").Integer(TryI(() => craft.NodeId) ?? 0)
                .Key("is_player").Boolean(TryB(() => craft.IsPlayer) ?? false)
                .Key("altitude").Number(TryD(() => craft.Altitude))
                .Key("altitude_agl").Number(TryD(() => craft.AltitudeAgl))
                .Key("altitude_terrain").Number(TryD(() => craft.AltitudeAboveTerrain))
                .Key("sea_level_altitude").Number(TryD(() => flight.AltitudeAboveSeaLevel))
                .Key("mass").Number(TryD(() => craft.CraftMass))
                .Key("current_mass").Number(TryD(() => flight.CurrentMass))
                .Key("part_count").Integer(TryI(() => craft.CraftPartCount) ?? 0)
                .Key("has_command_pod").Boolean(TryB(() => craft.HasCommandPod) ?? false)
                .Key("grounded").Boolean(TryB(() => flight.Grounded) ?? false)
                .Key("in_water").Boolean(TryB(() => flight.InWater) ?? false)
                .Key("in_contact_with_planet").Boolean(TryB(() => craft.InContactWithPlanet) ?? false)
                .Key("latitude").Number(TryD(() => craft.LatLon.y))
                .Key("longitude").Number(TryD(() => craft.LatLon.x))
                .Key("heading").Number(TryD(() => flight.Heading))
                .Key("pitch").Number(TryD(() => flight.Pitch))
                .Key("bank").Number(TryD(() => flight.BankAngle))
                .Key("angle_of_attack").Number(TryD(() => flight.AngleOfAttack))
                .Key("side_slip").Number(TryD(() => flight.SideSlip))
                .Key("atmosphere_height").Number(atmosphereHeight)
                .Key("command_pod_count").Integer(CommandPodCount(craft))
                .Key("crew_count").Integer(CrewCount(craft))
                .Key("current_stage").Integer(TryI(() => ActiveCommandPod(craft).CurrentStage) ?? -1)
                .Key("stage_count").Integer(TryI(() => ActiveCommandPod(craft).NumStages) ?? -1)
                .EndObject();
            return w.ToString();
        }

        private static string BuildOrbit(ICraftOrbitData craftOrbit, IOrbit orbit, ICraftNode craft,
            IPlanetData body, double? atmosphereHeight)
        {
            if (craftOrbit == null && orbit == null)
            {
                return "{\"ok\":false,\"error\":\"当前没有轨道数据（可能还在地面或正在加载）\"}";
            }

            double? apoAlt = TryD(() => craftOrbit.ApoapsisAltitude);
            double? peAlt = TryD(() => craftOrbit.PeriapsisAltitude);
            double? eccentricity = TryD(() => craftOrbit.Eccentricity);
            double? inclination = TryD(() => craftOrbit.Inclination);
            double? period = TryD(() => craftOrbit.Period);

            double? orbitTime = TryD(() => orbit.Time);
            double? apoTime = TryD(() => craftOrbit.ApoapsisTime);
            double? peTime = TryD(() => craftOrbit.PeriapsisTime);

            double? timeToApo = Remaining(apoTime, orbitTime);
            double? timeToPe = Remaining(peTime, orbitTime);

            bool stable = IsStableOrbit(peAlt, eccentricity, atmosphereHeight);

            BridgeJson.Writer w = new BridgeJson.Writer();
            w.StartObject()
                .Key("ok").Boolean(true)
                .Key("body").Text(TryS(() => body.Name))
                .Key("body_radius").Number(TryD(() => body.Radius))
                .Key("atmosphere_height").Number(atmosphereHeight)
                .Key("apoapsis_altitude").Number(apoAlt)
                .Key("periapsis_altitude").Number(peAlt)
                .Key("apoapsis_distance").Number(TryD(() => orbit.ApoapsisDistance))
                .Key("periapsis_distance").Number(TryD(() => orbit.PeriapsisDistance))
                .Key("eccentricity").Number(eccentricity)
                .Key("inclination").Number(inclination)
                .Key("period").Number(period)
                .Key("semi_major_axis").Number(TryD(() => orbit.SemiMajorAxis))
                .Key("semi_minor_axis").Number(TryD(() => orbit.SemiMinorAxis))
                .Key("is_valid").Boolean(TryB(() => orbit.IsValid) ?? false)
                .Key("orbit_type").Text(TryS(() => orbit.OrbitType.ToString()))
                .Key("is_stable_orbit").Boolean(stable)
                .Key("time_to_apoapsis").Number(timeToApo)
                .Key("time_to_periapsis").Number(timeToPe)
                .Key("true_anomaly").Number(TryD(() => orbit.TrueAnomaly))
                .Key("mean_anomaly").Number(TryD(() => orbit.MeanAnomaly))
                .Key("primary_mass").Number(TryD(() => orbit.PrimaryMass))
                .EndObject();
            return w.ToString();
        }

        private static string BuildFlight(ICraftFlightData flight)
        {
            if (flight == null)
            {
                return "{\"ok\":false,\"error\":\"当前没有飞行数据\"}";
            }

            BridgeJson.Writer w = new BridgeJson.Writer();
            w.StartObject()
                .Key("ok").Boolean(true)
                .Key("surface_speed").Number(TryD(() => flight.SurfaceVelocityMagnitude))
                .Key("orbital_speed").Number(TryD(() => flight.VelocityMagnitude))
                .Key("vertical_speed").Number(TryD(() => flight.VerticalSurfaceVelocity))
                .Key("lateral_speed").Number(TryD(() => flight.LateralSurfaceVelocity))
                .Key("altitude_agl").Number(TryD(() => flight.AltitudeAboveGroundLevel))
                .Key("altitude_terrain").Number(TryD(() => flight.AltitudeAboveTerrain))
                .Key("gravity").Number(TryD(() => flight.GravityMagnitude))
                .Key("drag_acceleration").Number(TryD(() => flight.DragAccelerationMagnitude))
                .Key("acceleration").Number(TryD(() => flight.AccelerationMagnitude))
                .Key("mach").Number(TryD(() => flight.MachNumber))
                .Key("air_density").Number(TryD(() => flight.AtmosphereSample.AirDensity))
                .Key("air_pressure").Number(TryD(() => flight.AtmosphereSample.AirPressure))
                .Key("temperature").Number(TryD(() => flight.AtmosphereSample.Temperature))
                .Key("speed_of_sound").Number(TryD(() => flight.AtmosphereSample.SpeedOfSound))
                .Key("thrust").Number(TryD(() => flight.CurrentEngineThrust))
                .Key("max_thrust").Number(TryD(() => flight.MaxActiveEngineThrust))
                .Key("fuel_mass").Number(TryD(() => flight.FuelMass))
                .EndObject();
            return w.ToString();
        }

        private static string BuildResources(ICraftFlightData flight, ICraftPerformanceData performance)
        {
            if (flight == null)
            {
                return "{\"ok\":false,\"error\":\"当前没有资源数据\"}";
            }

            BridgeJson.Writer w = new BridgeJson.Writer();
            w.StartObject()
                .Key("ok").Boolean(true)
                .Key("fuel_mass").Number(TryD(() => flight.FuelMass))
                .Key("fuel_in_stage").Number(TryD(() => flight.RemainingFuelInStage))
                .Key("monopropellant").Number(TryD(() => flight.RemainingMonopropellant))
                .Key("battery").Number(TryD(() => flight.RemainingBattery))
                .Key("delta_v_stage").Number(TryD(() => performance.DeltaVStage))
                .Key("isp").Number(TryD(() => performance.CurrentIsp))
                .Key("thrust_to_weight").Number(TryD(() => performance.ThrustToWeightRatio))
                .Key("burn_time_remaining").Number(TryD(() => performance.RemainingBurnTime))
                .Key("fuel_all_stages_percent").Number(TryD(() => performance.FuelAllStagesPercentage))
                .EndObject();
            return w.ToString();
        }

        private static string BuildTarget(ICraftFlightData flight)
        {
            INavSphereTarget target = null;
            try { if (flight != null) { target = flight.NavSphereTarget; } }
            catch { }

            if (target == null)
            {
                return "{\"ok\":true,\"locked\":false}";
            }

            Vector3d position = default(Vector3d);
            Vector3d velocity = default(Vector3d);
            try { position = target.Position; } catch { }
            try { velocity = target.Velocity; } catch { }

            BridgeJson.Writer w = new BridgeJson.Writer();
            w.StartObject()
                .Key("ok").Boolean(true)
                .Key("locked").Boolean(true)
                .Key("name").Text(TryS(() => target.Name))
                .Key("kind").Text(TargetKind(target))
                .Key("distance").Number(TryD(() => position.magnitude))
                .Key("relative_speed").Number(TryD(() => velocity.magnitude))
                .Key("parent").Text(TryS(() => target.Parent.PlanetData.Name))
                .Key("has_orbit").Boolean(target.OrbitNode != null)
                .Key("apoapsis_distance").Number(OrbitMember(target, o => o.Apoapsis.Position.magnitude))
                .Key("periapsis_distance").Number(OrbitMember(target, o => o.Periapsis.Position.magnitude))
                .EndObject();
            return w.ToString();
        }

        private static string BuildBodies(IFlightState flightState, IPlanetData currentBody)
        {
            List<IPlanetData> planets = new List<IPlanetData>();
            try
            {
                if (flightState != null && flightState.SolarSystemData != null)
                {
                    IReadOnlyList<IPlanetData> all = flightState.SolarSystemData.Planets;
                    if (all != null)
                    {
                        for (int i = 0; i < all.Count; i++)
                        {
                            planets.Add(all[i]);
                        }
                    }
                }
            }
            catch { }

            if (planets.Count == 0 && currentBody != null)
            {
                planets.Add(currentBody);
            }

            StringBuilder sb = new StringBuilder("[");
            for (int i = 0; i < planets.Count; i++)
            {
                IPlanetData planet = planets[i];
                if (planet == null)
                {
                    continue;
                }
                if (sb.Length > 1)
                {
                    sb.Append(',');
                }
                BridgeJson.Writer w = new BridgeJson.Writer();
                double? atmo = null;
                try
                {
                    if (planet.AtmosphereData != null)
                    {
                        atmo = planet.AtmosphereData.Height;
                    }
                }
                catch { }
                w.StartObject()
                    .Key("name").Text(TryS(() => planet.Name))
                    .Key("radius").Number(TryD(() => planet.Radius))
                    .Key("mass").Number(TryD(() => planet.Mass))
                    .Key("surface_gravity").Number(TryD(() => planet.SurfaceGravity))
                    .Key("escape_velocity").Number(TryD(() => planet.EscapeVelocity))
                    .Key("has_atmosphere").Boolean(atmo.HasValue && atmo.Value > 0)
                    .Key("atmosphere_height").Number(atmo)
                    .Key("has_water").Boolean(TryB(() => planet.HasWater) ?? false)
                    .Key("parent").Text(TryS(() => planet.Parent.Name))
                    .Key("sphere_of_influence").Number(TryDN(() => planet.SphereOfInfluence))
                    .EndObject();
                sb.Append(w.ToString());
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string BuildVessels(IFlightState flightState, ICraftNode current)
        {
            List<ICraftNode> nodes = new List<ICraftNode>();
            try
            {
                if (flightState != null && flightState.CraftNodes != null)
                {
                    IReadOnlyList<ICraftNode> all = flightState.CraftNodes;
                    for (int i = 0; i < all.Count; i++)
                    {
                        nodes.Add(all[i]);
                    }
                }
            }
            catch { }

            if (nodes.Count == 0 && current != null)
            {
                nodes.Add(current);
            }

            StringBuilder sb = new StringBuilder("[");
            for (int i = 0; i < nodes.Count; i++)
            {
                ICraftNode node = nodes[i];
                if (node == null)
                {
                    continue;
                }
                if (sb.Length > 1)
                {
                    sb.Append(',');
                }
                BridgeJson.Writer w = new BridgeJson.Writer();
                w.StartObject()
                    .Key("name").Text(TryS(() => node.Name))
                    .Key("node_id").Integer(TryI(() => node.NodeId) ?? 0)
                    .Key("is_player").Boolean(TryB(() => node.IsPlayer) ?? false)
                    .Key("altitude").Number(TryD(() => node.Altitude))
                    .Key("mass").Number(TryD(() => node.CraftMass))
                    .Key("part_count").Integer(TryI(() => node.CraftPartCount) ?? 0)
                    .Key("crew_count").Integer(CrewCount(node))
                    .EndObject();
                sb.Append(w.ToString());
            }
            sb.Append(']');
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // 纯逻辑（不碰游戏对象，便于复刻到测试里）
        // ------------------------------------------------------------------

        /// <summary>
        /// 判断飞行阶段。Juno 没有 KSP 那种现成的 situation 枚举，
        /// 所以这里用「是否落地 / 是否在水里 / 近点相对大气层顶的位置」推。
        /// </summary>
        public static string InferSituation(ICraftFlightData flight, ICraftOrbitData orbit,
            IPlanetData body, double? atmosphereHeight)
        {
            bool grounded = false;
            bool inWater = false;
            try { if (flight != null) { grounded = flight.Grounded; inWater = flight.InWater; } }
            catch { }

            if (inWater)
            {
                return "SPLASHED";
            }
            if (grounded)
            {
                return "LANDED";
            }

            double? eccentricity = null;
            double? peAlt = null;
            try
            {
                if (orbit != null)
                {
                    eccentricity = orbit.Eccentricity;
                    peAlt = orbit.PeriapsisAltitude;
                }
            }
            catch { }

            if (eccentricity.HasValue && eccentricity.Value >= 1.0)
            {
                return "ESCAPING";
            }
            if (!peAlt.HasValue || !eccentricity.HasValue)
            {
                return "FLYING";
            }
            if (peAlt.Value < 0)
            {
                return "SUB_ORBITAL";
            }
            if (atmosphereHeight.HasValue && atmosphereHeight.Value > 0
                && peAlt.Value < atmosphereHeight.Value)
            {
                return "SUB_ORBITAL";
            }
            return "ORBITING";
        }

        /// <summary>近点是否高过大气层顶 —— 新手最需要知道的一件事。</summary>
        public static bool IsStableOrbit(double? periapsisAltitude, double? eccentricity, double? atmosphereHeight)
        {
            if (!periapsisAltitude.HasValue)
            {
                return false;
            }
            if (eccentricity.HasValue && eccentricity.Value >= 1.0)
            {
                return false;
            }
            if (periapsisAltitude.Value < 0)
            {
                return false;
            }
            if (atmosphereHeight.HasValue && atmosphereHeight.Value > 0
                && periapsisAltitude.Value < atmosphereHeight.Value)
            {
                return false;
            }
            return true;
        }

        /// <summary>远点/近点剩余时间：负数（已经过了）一律当成未知。</summary>
        public static double? Remaining(double? eventTime, double? now)
        {
            if (!eventTime.HasValue || !now.HasValue)
            {
                return null;
            }
            double delta = eventTime.Value - now.Value;
            if (double.IsNaN(delta) || double.IsInfinity(delta) || delta <= 0)
            {
                return null;
            }
            return delta;
        }

        private static double? OrbitMember(INavSphereTarget target, Func<IOrbitNode, double> getter)
        {
            try
            {
                if (target == null || target.OrbitNode == null)
                {
                    return null;
                }
                double v = getter(target.OrbitNode);
                if (double.IsNaN(v) || double.IsInfinity(v))
                {
                    return null;
                }
                return v;
            }
            catch
            {
                return null;
            }
        }

        private static string TargetKind(INavSphereTarget target)
        {
            try
            {
                if (target.OrbitNode != null)
                {
                    return "vessel";
                }
                if (target.Parent != null)
                {
                    return "body";
                }
            }
            catch
            {
            }
            return "other";
        }

        private static ICommandPod ActiveCommandPod(ICraftNode craft)
        {
            try
            {
                if (craft != null && craft.CraftScript != null)
                {
                    return craft.CraftScript.ActiveCommandPod;
                }
            }
            catch
            {
            }
            return null;
        }

        private static int CommandPodCount(ICraftNode craft)
        {
            try
            {
                if (craft == null || craft.CraftScript == null)
                {
                    return 0;
                }
                int count = 0;
                foreach (ICommandPod pod in craft.CraftScript.CommandPods)
                {
                    if (pod != null)
                    {
                        count++;
                    }
                }
                return count;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Juno 没有 KSP 那种独立的乘员名单，人能坐的地方就是指令舱 / EVA。
        /// 这里报的是「有人的载具单元数」，不是精确的航天员人数。
        /// </summary>
        private static int CrewCount(ICraftNode craft)
        {
            try
            {
                if (craft == null || craft.CraftScript == null)
                {
                    return 0;
                }
                int count = 0;
                foreach (ICommandPod pod in craft.CraftScript.CommandPods)
                {
                    if (pod != null)
                    {
                        count++;
                    }
                }
                return count;
            }
            catch
            {
                return 0;
            }
        }

        // ------------------------------------------------------------------
        // 取数兜底：任何异常 / NaN / Inf 一律变成 null
        // ------------------------------------------------------------------

        private static double? TryD(Func<double> getter)
        {
            try
            {
                double v = getter();
                if (double.IsNaN(v) || double.IsInfinity(v))
                {
                    return null;
                }
                return v;
            }
            catch
            {
                return null;
            }
        }

        private static double? TryD(Func<float> getter)
        {
            try
            {
                float v = getter();
                if (float.IsNaN(v) || float.IsInfinity(v))
                {
                    return null;
                }
                return v;
            }
            catch
            {
                return null;
            }
        }

        private static int? TryI(Func<int> getter)
        {
            try
            {
                return getter();
            }
            catch
            {
                return null;
            }
        }

        private static bool? TryB(Func<bool> getter)
        {
            try
            {
                return getter();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>取值本身就是可空 double 时用这个（例如天体的引力球范围）。</summary>
        private static double? TryDN(Func<double?> getter)
        {
            try
            {
                double? v = getter();
                if (!v.HasValue)
                {
                    return null;
                }
                if (double.IsNaN(v.Value) || double.IsInfinity(v.Value))
                {
                    return null;
                }
                return v;
            }
            catch
            {
                return null;
            }
        }

        private static string TryS(Func<string> getter)
        {
            try
            {
                return getter();
            }
            catch
            {
                return null;
            }
        }

        private static double Now()
        {
            try
            {
                return Time.realtimeSinceStartup;
            }
            catch
            {
                return 0;
            }
        }
    }
}
