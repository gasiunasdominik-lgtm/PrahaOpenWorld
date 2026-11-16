using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    public float walkSpeed = 4.2f, runSpeed = 8f, jumpSpeed = 5.2f, gravity = 20f, turnSpeed = 720f;
    public Transform camTarget;
    public bool controlEnabled = true;

    // Debug-input hook for automated verification (Input System polling can't be simulated in play mode).
    public Vector2 debugMove; public bool debugRun;

    CharacterController cc;
    CharacterMotion motion;
    Vector3 velocity;
    float vy, safeTimer;
    Vector3 lastSafe;
    bool wasGrounded = true;

    public Vector3 Velocity => velocity;
    public System.Action<string> OnToast;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        motion = GetComponent<CharacterMotion>();
        lastSafe = transform.position;
    }

    void Start()
    {
        motion.maxSpeed = runSpeed;
        motion.OnFootstep = () => GameAudio.I?.PlayUI(GameAudio.I.footstep, 0.22f, 0.15f);
    }

    public void Teleport(Vector3 p, float yaw)
    {
        cc.enabled = false;
        transform.SetPositionAndRotation(p, Quaternion.Euler(0, yaw, 0));
        cc.enabled = true;
        velocity = Vector3.zero; vy = 0;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        Vector2 input = Vector2.zero; bool run = false, jump = false;
        var kb = Keyboard.current;
        if (controlEnabled)
        {
            if (kb != null)
            {
                input.x = (kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0);
                input.y = (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0);
                run = kb.leftShiftKey.isPressed;
                jump = kb.spaceKey.wasPressedThisFrame;
            }
            if (debugMove != Vector2.zero) { input = debugMove; run = debugRun; }
        }
        input = Vector2.ClampMagnitude(input, 1f);

        var cam = Camera.main ? Camera.main.transform : transform;
        var fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
        var right = Vector3.Cross(Vector3.up, fwd);
        var wish = fwd * input.y + right * input.x;
        float target = run ? runSpeed : walkSpeed;
        var targetVel = wish * target;
        float accel = cc.isGrounded ? 30f : 8f;
        velocity = Vector3.MoveTowards(velocity, targetVel, accel * dt);

        if (wish.sqrMagnitude > 0.01f)
        {
            var q = Quaternion.LookRotation(wish, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, q, turnSpeed * dt);
        }

        if (cc.isGrounded)
        {
            if (!wasGrounded) motion.Land(-vy);
            vy = -2f;
            if (jump) vy = jumpSpeed;
        }
        else vy -= gravity * dt;
        wasGrounded = cc.isGrounded;

        cc.Move((velocity + Vector3.up * vy) * dt);

        motion.Grounded = cc.isGrounded;
        motion.Tick(cc.velocity, dt);

        // the Vltava is cold — fall in and you're back on the bank
        safeTimer += dt;
        if (cc.isGrounded && transform.position.y > -0.5f && safeTimer > 0.5f) { lastSafe = transform.position; safeTimer = 0; }
        var tp = transform.position;
        if (tp.y < -1.8f && !Metro.InStation(tp)) // (not down in the metro: some vestibules lie under the river)
        {
            Teleport(lastSafe + Vector3.up * 0.5f, transform.eulerAngles.y);
            OnToast?.Invoke("Vltava je dnes studená!");
        }
    }
}
