using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

namespace EasyDeliveryVR.Rendering
{
    /// <summary>
    /// Performance numbers in the BepInEx log: every 5 s with Debug/LogPerformance, or once with the
    /// "perf" debug command (vr_cmd.txt), which also logs the render setup (eye textures, URP asset, XR cameras).
    /// </summary>
    internal class VrPerf : MonoBehaviour
    {
        private static VrPerf _instance;
        private void Awake() => _instance = this;

        private float _sum, _max, _windowStart;
        private int _frames;
        private int _droppedAtStart = -1;
        private float _gpuSum;
        private int _gpuSamples;
        private readonly FrameTiming[] _timings = new FrameTiming[1];
        private double _cpuSum, _gpuFtSum;
        private int _ftSamples;
        private bool _logNext;

        public static void LogNow(bool withSetup)
        {
            if (_instance == null) return;
            if (withSetup) LogSetup();
            _instance._logNext = true;
        }

        private static XRDisplaySubsystem Display()
        {
            var list = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(list);
            foreach (var d in list) if (d.running) return d;
            return null;
        }

        private XRDisplaySubsystem _display;

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _sum += dt;
            _max = Mathf.Max(_max, dt);
            _frames++;

            if (_display == null && Time.frameCount % 120 == 0) _display = Display();
            if (_display != null && _display.TryGetAppGPUTimeLastFrame(out float gpu) && gpu > 0f) { _gpuSum += gpu; _gpuSamples++; }
            if (FrameTimingManager.IsFeatureEnabled())
            {
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, _timings) > 0)
                {
                    _cpuSum += _timings[0].cpuMainThreadFrameTime;
                    _gpuFtSum += _timings[0].gpuFrameTime;
                    _ftSamples++;
                }
            }

            bool periodic = VrConfig.LogPerformance.Value && Time.unscaledTime - _windowStart >= 5f;
            if (!periodic && !(_logNext && _frames >= 30)) return;
            _logNext = false;

            int dropped = -1;
            if (_display != null && _display.TryGetDroppedFrameCount(out int d)) dropped = d;
            var sb = new StringBuilder("VR perf: ");
            sb.Append($"{_frames / Mathf.Max(_sum, 1e-4f):F1} fps, frame avg {_sum / _frames * 1000f:F1} ms max {_max * 1000f:F1} ms");
            if (_gpuSamples > 0) sb.Append($", XR app GPU avg {_gpuSum / _gpuSamples * 1000f:F2} ms");
            if (_ftSamples > 0) sb.Append($", CPU main {_cpuSum / _ftSamples:F2} ms, GPU {_gpuFtSum / _ftSamples:F2} ms");
            if (dropped >= 0 && _droppedAtStart >= 0) sb.Append($", dropped {dropped - _droppedAtStart}");
            sb.Append($", mode {VrCameraManager.Mode}");
            Plugin.Log.LogInfo(sb.ToString());

            _sum = _max = _gpuSum = 0f;
            _cpuSum = _gpuFtSum = 0;
            _frames = _gpuSamples = _ftSamples = 0;
            _windowStart = Time.unscaledTime;
            _droppedAtStart = dropped;
        }

        private static void LogSetup()
        {
            Plugin.Log.LogInfo($"VR perf setup: eye texture {XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight} " +
                               $"(scale {XRSettings.eyeTextureResolutionScale:F2}), render mode {XRSettings.stereoRenderingMode}, " +
                               $"refresh {(Display() is XRDisplaySubsystem d && d.TryGetDisplayRefreshRate(out float hz) ? hz : 0f):F0} Hz, " +
                               $"frame timing stats {(FrameTimingManager.IsFeatureEnabled() ? "on" : "off")}");
            var urp = UniversalRenderPipeline.asset;
            if (urp != null)
                Plugin.Log.LogInfo($"VR perf setup: URP '{urp.name}' msaa {urp.msaaSampleCount}x, render scale {urp.renderScale:F2}, HDR {urp.supportsHDR}, " +
                                   $"main light shadows {urp.supportsMainLightShadows} {urp.mainLightShadowmapResolution}px, " +
                                   $"shadow distance {urp.shadowDistance:F0} m, cascades {urp.shadowCascadeCount}, " +
                                   $"additional lights {urp.additionalLightsRenderingMode}, quality level {QualitySettings.GetQualityLevel()}");
            foreach (var c in Camera.allCameras)
            {
                var data = c.GetComponent<UniversalAdditionalCameraData>();
                Plugin.Log.LogInfo($"VR perf setup: camera '{c.name}' target {(c.targetTexture ? $"{c.targetTexture.name} {c.targetTexture.width}x{c.targetTexture.height}" : "screen")} " +
                                   $"xr {(data != null ? data.allowXRRendering.ToString() : "n/a")} type {(data != null ? data.renderType.ToString() : "n/a")} " +
                                   $"depth {c.depth} clear {c.clearFlags} mask 0x{c.cullingMask:X}");
            }
        }
    }
}
