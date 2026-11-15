using UnityEngine;
using UnityEngine.InputSystem;

// Arcade car: raycast suspension at four wheels, bicycle-model steering, lateral grip,
// handbrake drift. Rigidbody-driven so it collides with the city.
[RequireComponent(typeof(Rigidbody))]
public class CarController : MonoBehaviour
{
    public float maxSpeed = 34f, motorAccel = 10f, brakeAccel = 22f, reverseMax = 9f;
    public float grip = 9f, driftGrip = 1.6f, wheelBase = 2.6f, maxLateralAccel = 14f;
    public float springK = 32000f, damper = 3200f, restLength = 0.35f, wheelRadius = 0.34f;
    public Transform[] wheels = new Transform[4];
    public Transform camTarget, seat;
    public bool driven;
    public bool ai; public float aiThrottle, aiSteer; // set by Driver (city traffic)

    // Debug-input hook for automated verification
    public float debugThrottle, debugSteer;

    Rigidbody rb;
    readonly Vector3[] attach = new Vector3[4];
    float throttle, steer, wheelSpin;
    AudioSource engineSrc;
    public float SpeedKmh => rb ? Vector3.Dot(rb.linearVelocity, transform.forward) * 3.6f : 0f;
    public float NormSpeed => rb ? Mathf.Clamp01(rb.linearVelocity.magnitude / maxSpeed) : 0f;
    public System.Action<float> OnCrash;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.mass = 1200f;
        rb.linearDamping = 0.05f;
        rb.angularDamping = 2.5f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.centerOfMass = new Vector3(0, 0.35f, 0.1f);
        for (int i = 0; i < 4; i++)
        {
            float x = (i % 2 == 0) ? -0.78f : 0.78f;
            float z = (i < 2) ? 1.35f : -1.3f;
            attach[i] = new Vector3(x, 0.6f, z);
        }
    }

    void Start()
    {
        if (GameAudio.I != null)
        {
            engineSrc = gameObject.AddComponent<AudioSource>();
            engineSrc.clip = GameAudio.I.engine;
            engineSrc.loop = true; engineSrc.spatialBlend = 1f; engineSrc.minDistance = 5f; engineSrc.maxDistance = 70f;
            engineSrc.rolloffMode = AudioRolloffMode.Linear;
            GameAudio.I.Register(engineSrc, 0f, false);
            engineSrc.Play();
        }
    }

    public void SetDriven(bool on)
    {
        driven = on;
        if (!on) { throttle = 0; steer = 0; }
        rb.WakeUp();
    }

    void Update()
    {
        float tIn = 0, sIn = 0;
        if (driven)
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                tIn = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1 : 0) - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1 : 0);
                sIn = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1 : 0) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1 : 0);
                if (kb.hKey.wasPressedThisFrame) GameAudio.I?.PlayAt(GameAudio.I.horn, transform.position, 0.8f, 0f);
                if (kb.rKey.wasPressedThisFrame) Unflip();
            }
            if (debugThrottle != 0 || debugSteer != 0) { tIn = debugThrottle; sIn = debugSteer; }
        }
        else if (ai) { tIn = aiThrottle; sIn = aiSteer; }
        throttle = Mathf.MoveTowards(throttle, tIn, Time.deltaTime * 4f);
        steer = Mathf.MoveTowards(steer, sIn, Time.deltaTime * (sIn == 0 ? 5f : 3f));

        // visuals: wheel spin & steer
        float fwdSpeed = Vector3.Dot(rb.linearVelocity, transform.forward);
        wheelSpin += fwdSpeed / wheelRadius * Mathf.Rad2Deg * Time.deltaTime;
        for (int i = 0; i < 4; i++)
        {
            if (!wheels[i]) continue;
            float yaw = i < 2 ? steer * 28f : 0f;
            wheels[i].localRotation = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(wheelSpin, 0, 0);
        }

        if (engineSrc)
        {
            float n = NormSpeed;
            float rpm = driven || ai ? 0.75f + n * 1.5f + Mathf.Abs(throttle) * 0.2f : 0.6f;
            engineSrc.pitch = Mathf.Lerp(engineSrc.pitch, rpm, Time.deltaTime * 5f);
            GameAudio.I.SetBaseVolume(engineSrc, driven ? 0.35f + n * 0.35f : ai ? 0.1f + n * 0.25f : 0f);
        }
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        int grounded = 0;
        for (int i = 0; i < 4; i++)
        {
            Vector3 p = transform.TransformPoint(attach[i]);
            float rayLen = restLength + wheelRadius;
            if (Physics.Raycast(p, -transform.up, out var hit, rayLen, ~0, QueryTriggerInteraction.Ignore) && hit.rigidbody != rb)
            {
                grounded++;
                float compression = rayLen - hit.distance;
                float vUp = Vector3.Dot(rb.GetPointVelocity(p), transform.up);
                float f = compression * springK - vUp * damper;
                rb.AddForceAtPosition(transform.up * Mathf.Max(0, f), p);
                if (wheels[i]) wheels[i].localPosition = new Vector3(wheels[i].localPosition.x, attach[i].y - (hit.distance - wheelRadius), wheels[i].localPosition.z);
            }
            else if (wheels[i])
                wheels[i].localPosition = new Vector3(wheels[i].localPosition.x, attach[i].y - restLength, wheels[i].localPosition.z);
        }

        if (grounded < 2) return;
        Vector3 v = rb.linearVelocity;
        Vector3 fwd = transform.forward, right = transform.right;
        float fwdSpeed = Vector3.Dot(v, fwd);
        float sideSpeed = Vector3.Dot(v, right);

        bool handbrake = driven && Keyboard.current != null && Keyboard.current.spaceKey.isPressed;
        float a = 0f;
        if (driven || (ai && Mathf.Abs(throttle) > 0.01f))
        {
            if (throttle > 0.01f)
                a = fwdSpeed < -0.5f ? brakeAccel * throttle : motorAccel * throttle * Mathf.Clamp01(1f - fwdSpeed / maxSpeed);
            else if (throttle < -0.01f)
                a = fwdSpeed > 0.5f ? -brakeAccel * -throttle : -motorAccel * 0.6f * -throttle * Mathf.Clamp01(1f + fwdSpeed / reverseMax);
            else a = -fwdSpeed * 0.35f; // engine braking
        }
        else a = -Mathf.Clamp(fwdSpeed * 6f, -brakeAccel, brakeAccel); // parked: hold still
        if (handbrake) a = -Mathf.Sign(fwdSpeed) * Mathf.Min(Mathf.Abs(fwdSpeed) / dt, 8f);
        rb.AddForce(fwd * a, ForceMode.Acceleration);

        float g = handbrake ? driftGrip : grip;
        rb.AddForce(-right * sideSpeed * g, ForceMode.Acceleration);

        float sn = Mathf.Clamp01(Mathf.Abs(fwdSpeed) / maxSpeed);
        float steerAngle = steer * Mathf.Lerp(34f, 10f, sn) * Mathf.Deg2Rad;
        float yawRate = fwdSpeed / wheelBase * Mathf.Tan(steerAngle);
        // cap lateral accel (~1.4 g) so high-speed steering isn't twitchy; handbrake lets the tail swing past it
        float yawCap = maxLateralAccel / Mathf.Max(Mathf.Abs(fwdSpeed), 1f) * (handbrake ? 2f : 1f);
        yawRate = Mathf.Clamp(yawRate, -yawCap, yawCap);
        if (handbrake) yawRate *= 1.35f;
        var av = rb.angularVelocity;
        float localYaw = Vector3.Dot(av, transform.up);
        float newYaw = Mathf.Lerp(localYaw, yawRate, 1f - Mathf.Exp(-dt * 10f));
        rb.angularVelocity = av + transform.up * (newYaw - localYaw);

        rb.AddForce(-transform.up * (v.magnitude * 0.12f), ForceMode.Acceleration); // light downforce
    }

    void OnCollisionEnter(Collision c)
    {
        float impulse = c.impulse.magnitude / rb.mass;
        if (impulse > 2.5f)
        {
            GameAudio.I?.PlayAt(GameAudio.I.crash, c.GetContact(0).point, Mathf.Clamp01(impulse / 10f));
            OnCrash?.Invoke(impulse);
        }
    }

    public void Unflip()
    {
        var p = transform.position + Vector3.up * 1.2f;
        transform.SetPositionAndRotation(p, Quaternion.Euler(0, transform.eulerAngles.y, 0));
        rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
    }

    public bool IsFlipped => Vector3.Dot(transform.up, Vector3.up) < 0.3f;

    // ---------------------------------------------------------------- builder
    public static CarController Build(Vector3 pos, float yaw, Color paint, int style)
    {
        // tagged so the camera deoccluder ignores the car's own body
        var root = new GameObject("Auto") { tag = "Player", layer = Traffic.CarLayer };
        root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        var col = root.AddComponent<BoxCollider>();
        bool van = style == 2;
        float L = van ? 4.4f : 4.1f;
        col.center = new Vector3(0, van ? 1.15f : 0.95f, 0);
        col.size = new Vector3(1.78f, van ? 1.5f : 1.1f, L);
        var rb = root.AddComponent<Rigidbody>();
        var car = root.AddComponent<CarController>();

        var visual = new MeshBuilder();
        var mPaint = Gfx.Mat(paint, 0.65f);
        var mTrim = Gfx.Mat(Palette.Tire, 0.3f);
        var mGlass = Gfx.Mat(Palette.Glass, 0.9f);
        var mChrome = Gfx.Mat(Palette.Chrome, 0.7f);
        var mHead = Gfx.Emissive(Palette.HeadLight, 1.6f);
        var mTail = Gfx.Emissive(Palette.TailLight, 2.2f);
        visual.Box(mPaint, new Vector3(0, 0.72f, 0), new Vector3(1.8f, 0.62f, L));
        visual.Box(mTrim, new Vector3(0, 0.45f, L / 2 + 0.03f), new Vector3(1.84f, 0.22f, 0.12f));
        visual.Box(mTrim, new Vector3(0, 0.45f, -L / 2 - 0.03f), new Vector3(1.84f, 0.22f, 0.12f));
        if (van)
        {
            visual.Box(mPaint, new Vector3(0, 1.45f, -0.35f), new Vector3(1.76f, 0.95f, L - 1.1f));
            visual.Box(mGlass, new Vector3(0, 1.38f, L / 2 - 1.0f), new Vector3(1.66f, 0.6f, 0.6f), Quaternion.Euler(-20, 0, 0));
            visual.Box(mGlass, new Vector3(0, 1.5f, -0.2f), new Vector3(1.78f, 0.45f, 1.6f));
        }
        else
        {
            float cabZ = style == 1 ? -0.15f : -0.3f, cabL = style == 1 ? 2.1f : 1.9f;
            visual.Box(mGlass, new Vector3(0, 1.28f, cabZ), new Vector3(1.6f, 0.52f, cabL));
            visual.Box(mPaint, new Vector3(0, 1.56f, cabZ - 0.05f), new Vector3(1.64f, 0.08f, cabL - 0.25f));
            visual.Box(mPaint, new Vector3(0.79f, 1.28f, cabZ), new Vector3(0.05f, 0.5f, 0.12f));
            visual.Box(mPaint, new Vector3(-0.79f, 1.28f, cabZ), new Vector3(0.05f, 0.5f, 0.12f));
        }
        foreach (float s in new[] { -1f, 1f })
        {
            visual.Box(mHead, new Vector3(s * 0.62f, 0.8f, L / 2 + 0.01f), new Vector3(0.36f, 0.16f, 0.06f));
            visual.Box(mTail, new Vector3(s * 0.66f, 0.82f, -L / 2 - 0.01f), new Vector3(0.3f, 0.14f, 0.06f));
            visual.Box(mChrome, new Vector3(s * 0.93f, 1.08f, L / 2 - 1.45f), new Vector3(0.14f, 0.1f, 0.18f)); // mirrors
        }
        visual.Box(mChrome, new Vector3(0, 0.66f, L / 2 + 0.02f), new Vector3(0.7f, 0.12f, 0.04f)); // grille
        var vis = visual.Build("Karoserie", root.transform, false);
        vis.isStatic = false;

        for (int i = 0; i < 4; i++)
        {
            float x = (i % 2 == 0) ? -0.82f : 0.82f;
            float z = (i < 2) ? 1.35f : -1.3f;
            var w = new GameObject("Wheel" + i).transform;
            w.SetParent(root.transform, false);
            w.localPosition = new Vector3(x, 0.34f, z);
            var wm = new MeshBuilder();
            wm.Cylinder(mTrim, new Vector3(0, -0.12f, 0), 0.34f, 0.24f, 12, true, true);
            wm.Cylinder(mChrome, new Vector3(0, -0.13f, 0), 0.18f, 0.26f, 8, true, true);
            var wg = wm.Build("Mesh", w, false);
            wg.isStatic = false;
            // cylinders are built along +Y; lay them on their side so the axle is X
            wg.transform.localRotation = Quaternion.Euler(0, 0, 90);
            car.wheels[i] = w;
        }

        car.camTarget = new GameObject("CamTarget").transform;
        car.camTarget.SetParent(root.transform, false);
        car.camTarget.localPosition = new Vector3(0, 1.3f, 0);
        car.seat = new GameObject("ExitPoint").transform;
        car.seat.SetParent(root.transform, false);
        car.seat.localPosition = new Vector3(-1.9f, 0.1f, 0.2f);
        return car;
    }
}
