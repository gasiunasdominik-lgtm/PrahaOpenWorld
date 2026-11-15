using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

// Cinemachine 3 rig: an orbit camera for on-foot / driving, plus a slow title orbit.
// Mouse drives the orbit axes directly (no input-actions asset needed).
public class CameraRig : MonoBehaviour
{
    public CinemachineCamera playCam, titleCam;
    public CinemachineOrbitalFollow orbit;
    public CinemachineImpulseSource impulse;
    public Camera mainCam, minimapCam;
    public RenderTexture minimapRT;
    public float mouseSens = 0.12f;
    public Vector3 titleCenter = new(-250, 0, 180); // between Charles Bridge and the castle (real map)
    public float titleRadius = 650f, titleHeight = 200f, titleDegPerSec = 3f;

    Transform follow;
    float targetRadius = 5.5f, idle, titleAngle = 200f, baseFov = 55f;
    CarController car;
    public bool InputEnabled { get; set; }

    public void Setup()
    {
        // main camera + brain
        mainCam = Camera.main;
        if (mainCam == null)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            mainCam = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
        }
        mainCam.nearClipPlane = 0.3f; mainCam.farClipPlane = 2600f;
        var urp = mainCam.GetUniversalAdditionalCameraData();
        urp.renderPostProcessing = true;
        urp.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        var brain = mainCam.GetComponent<CinemachineBrain>() ?? mainCam.gameObject.AddComponent<CinemachineBrain>();
        brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 1.6f);

        // gameplay orbit camera
        var pc = new GameObject("PlayCam");
        pc.transform.SetParent(transform, false);
        playCam = pc.AddComponent<CinemachineCamera>();
        playCam.Lens = LensSettings.Default;
        playCam.Lens.FieldOfView = baseFov;
        playCam.Lens.NearClipPlane = 0.3f;
        playCam.Lens.FarClipPlane = 2600f;
        playCam.Priority = 10;
        orbit = pc.AddComponent<CinemachineOrbitalFollow>();
        orbit.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.Sphere;
        orbit.Radius = targetRadius;
        orbit.TrackerSettings.BindingMode = BindingMode.WorldSpace;
        orbit.TrackerSettings.PositionDamping = new Vector3(0.6f, 0.4f, 0.6f);
        orbit.VerticalAxis.Range = new Vector2(-8f, 70f);
        orbit.VerticalAxis.Value = 14f;
        orbit.HorizontalAxis.Wrap = true;
        var comp = pc.AddComponent<CinemachineRotationComposer>();
        comp.Damping = new Vector2(0.4f, 0.3f);
        comp.TargetOffset = new Vector3(0, 0.25f, 0);
        var deocc = pc.AddComponent<CinemachineDeoccluder>();
        deocc.CollideAgainst = ~0;
        deocc.IgnoreTag = "Player";
        deocc.MinimumDistanceFromTarget = 0.8f;
        // AddComponent at runtime skips Reset(), so every avoidance field is set explicitly
        var avoid = deocc.AvoidObstacles;
        avoid.Enabled = true;
        avoid.DistanceLimit = 0f;
        avoid.MinimumOcclusionTime = 0f;
        avoid.CameraRadius = 0.25f;
        avoid.Strategy = CinemachineDeoccluder.ObstacleAvoidance.ResolutionStrategy.PullCameraForward;
        avoid.MaximumEffort = 4;
        avoid.SmoothingTime = 0f;
        avoid.Damping = 0.4f;
        avoid.DampingWhenOccluded = 0.1f;
        deocc.AvoidObstacles = avoid;
        pc.AddComponent<CinemachineImpulseListener>();
        impulse = pc.AddComponent<CinemachineImpulseSource>();

        // title camera: a slow orbit over the river, castle and bridge
        var tc = new GameObject("TitleCam");
        tc.transform.SetParent(transform, false);
        titleCam = tc.AddComponent<CinemachineCamera>();
        titleCam.Lens = LensSettings.Default;
        titleCam.Lens.FieldOfView = 42f;
        titleCam.Lens.FarClipPlane = 2600f;
        titleCam.Priority = 20;

        // minimap: top-down orthographic into a render texture
        minimapRT = new RenderTexture(320, 320, 16) { name = "MinimapRT" };
        var mm = new GameObject("MinimapCam");
        mm.transform.SetParent(transform, false);
        minimapCam = mm.AddComponent<Camera>();
        minimapCam.orthographic = true;
        minimapCam.orthographicSize = 140f;
        minimapCam.nearClipPlane = 1f; minimapCam.farClipPlane = 900f;
        minimapCam.targetTexture = minimapRT;
        minimapCam.clearFlags = CameraClearFlags.SolidColor;
        minimapCam.backgroundColor = Palette.UIDark;
        var mmData = minimapCam.GetUniversalAdditionalCameraData();
        mmData.renderPostProcessing = false;
        mmData.renderShadows = false;
        mm.transform.rotation = Quaternion.Euler(90, 0, 0);
    }

    public void SetTitle(bool on) { titleCam.Priority = on ? 20 : 0; }

    public void FollowPlayer(Transform target)
    {
        car = null;
        follow = target;
        playCam.Follow = target; playCam.LookAt = target;
        targetRadius = 5.5f;
        orbit.TrackerSettings.PositionDamping = new Vector3(0.6f, 0.4f, 0.6f);
    }

    public void FollowTarget(Transform t, float radius, float yaw)
    {
        car = null; follow = t;
        playCam.Follow = t; playCam.LookAt = t;
        targetRadius = radius;
        orbit.TrackerSettings.PositionDamping = new Vector3(0.3f, 0.3f, 0.3f);
        orbit.HorizontalAxis.Value = yaw;
    }

    public void FollowCar(CarController c)
    {
        car = c;
        follow = c.camTarget;
        playCam.Follow = c.camTarget; playCam.LookAt = c.camTarget;
        targetRadius = 9f;
        orbit.TrackerSettings.PositionDamping = new Vector3(0.25f, 0.3f, 0.25f);
        orbit.HorizontalAxis.Value = c.transform.eulerAngles.y;
    }

    public void Shake(float force) { if (impulse) impulse.GenerateImpulseWithForce(force); }

    void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;
        // title orbit
        titleAngle += titleDegPerSec * dt;
        float a = titleAngle * Mathf.Deg2Rad;
        var tp = titleCenter + new Vector3(Mathf.Sin(a) * titleRadius, titleHeight, Mathf.Cos(a) * titleRadius);
        titleCam.transform.position = tp;
        titleCam.transform.rotation = Quaternion.LookRotation(titleCenter + Vector3.up * 25f - tp);

        if (follow != null && Time.timeScale > 0f)
        {
            Vector2 d = Vector2.zero;
            if (InputEnabled && Mouse.current != null) d = Mouse.current.delta.ReadValue();
            if (d.sqrMagnitude > 0.01f) idle = 0f; else idle += dt;
            orbit.HorizontalAxis.Value += d.x * mouseSens;
            orbit.VerticalAxis.Value = Mathf.Clamp(orbit.VerticalAxis.Value - d.y * mouseSens, orbit.VerticalAxis.Range.x, orbit.VerticalAxis.Range.y);

            if (car != null && idle > 1.2f && Mathf.Abs(car.SpeedKmh) > 8f)
            {
                float yaw = car.transform.eulerAngles.y + (car.SpeedKmh < 0 ? 180f : 0f);
                orbit.HorizontalAxis.Value = Mathf.LerpAngle(orbit.HorizontalAxis.Value, yaw, dt * 2.2f);
                orbit.VerticalAxis.Value = Mathf.Lerp(orbit.VerticalAxis.Value, 12f, dt * 1.5f);
            }
            orbit.Radius = Mathf.Lerp(orbit.Radius, targetRadius + (car ? car.NormSpeed * 2.5f : 0f), dt * 3f);
            float fov = baseFov + (car ? car.NormSpeed * 14f : 0f);
            playCam.Lens.FieldOfView = Mathf.Lerp(playCam.Lens.FieldOfView, fov, dt * 3f);

            minimapCam.transform.position = new Vector3(follow.position.x, follow.position.y + 400f, follow.position.z);
        }
    }

    public float CameraYaw => mainCam ? mainCam.transform.eulerAngles.y : 0f;
}
