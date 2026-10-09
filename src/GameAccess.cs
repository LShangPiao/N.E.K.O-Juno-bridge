// N.E.K.O-Juno-bridge — 场景访问层
//
// 在 BepInEx 插件里我们没有官方 mod 的 GameModBase/IFlightUpdate 回调，
// 所以自己去找飞行场景：游戏把它实现在 Assets.Scripts.Flight.FlightSceneScript
// （一个 MonoBehaviour）上，同时实现了 ModApi.Flight.IFlightScene。
//
// 查找用类型名字符串而不是 typeof(...)，这样即使将来官方改名，
// 也只是这里找不到场景（返回 null），不会影响整个插件加载。
//
// 线程模型：这些方法**只能在主线程调用**（Unity API 限制）。

using System;
using ModApi.Flight;
using UnityEngine;

namespace NekoJunoBridge
{
    public static class GameAccess
    {
        private const string SceneScriptTypeName = "Assets.Scripts.Flight.FlightSceneScript";

        private static IFlightScene scene;
        private static Type sceneScriptType;
        private static bool typeResolved;
        private static float nextProbe;

        /// <summary>当前飞行场景；不在飞行时为 null。</summary>
        public static IFlightScene Scene
        {
            get { return scene; }
        }

        /// <summary>
        /// 每帧调用。有缓存就直接用；缓存失效（退出飞行）才重新查找，
        /// 且最多每 0.5 秒找一次 —— FindObjectOfType 不便宜。
        /// </summary>
        public static void Tick()
        {
            try
            {
                UnityEngine.Object alive = scene as UnityEngine.Object;
                if (alive != null)
                {
                    return;
                }
                scene = null;

                float now = Time.realtimeSinceStartup;
                if (now < nextProbe)
                {
                    return;
                }
                nextProbe = now + 0.5f;
                scene = FindScene();
            }
            catch
            {
                scene = null;
            }
        }

        /// <summary>强制丢弃缓存，让下一次 Tick 重新查找。</summary>
        public static void Invalidate()
        {
            scene = null;
            nextProbe = 0f;
        }

        private static IFlightScene FindScene()
        {
            try
            {
                Type type = ResolveSceneType();
                if (type == null)
                {
                    return null;
                }
#pragma warning disable 618
                UnityEngine.Object found = UnityEngine.Object.FindObjectOfType(type);
#pragma warning restore 618
                return found as IFlightScene;
            }
            catch
            {
                return null;
            }
        }

        private static Type ResolveSceneType()
        {
            if (typeResolved)
            {
                return sceneScriptType;
            }
            typeResolved = true;
            try
            {
                sceneScriptType = Type.GetType(SceneScriptTypeName + ", SimpleRockets2");
                if (sceneScriptType != null)
                {
                    return sceneScriptType;
                }
                System.Reflection.Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    Type candidate = assemblies[i].GetType(SceneScriptTypeName, false);
                    if (candidate != null)
                    {
                        sceneScriptType = candidate;
                        break;
                    }
                }
            }
            catch
            {
            }
            return sceneScriptType;
        }
    }
}
