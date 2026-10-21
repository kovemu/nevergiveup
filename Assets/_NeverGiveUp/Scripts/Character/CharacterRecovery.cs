using UnityEngine;

namespace NeverGiveUp
{
    [RequireComponent(typeof(CharacterMotor))]
    public sealed class CharacterRecovery : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float downDuration = 0.8f;
        [SerializeField, Min(0.1f)] private float getUpDuration = 0.7f;
        [SerializeField, Min(0.1f)] private float knockdownImpulse = 1f;
        [SerializeField, Min(0.1f)] private float collisionThreshold = 5.5f;
        [SerializeField, Min(0f)] private float tumbleImpulse = 0.6f;
        [SerializeField] private int physicsVersion;
        private CharacterMotor motor;
        private float timer;
        private float stableTime;
        private float immunityUntil;
        private float stumbleUntil;
        public bool Active { get; private set; }
        public bool GettingUp { get; private set; }
        public bool Stumbling => !Active && Time.time < stumbleUntil;
        public bool NeedsRespawn => false; // Retained for old callers; elapsed time is never a respawn reason.

        private void Awake() { motor = GetComponent<CharacterMotor>(); ApplyM3Defaults(); }
        public void ApplyM3Defaults()
        {
            if (physicsVersion >= 3) return;
            if (Mathf.Approximately(knockdownImpulse, 2.5f)) knockdownImpulse = 1f;
            physicsVersion = 3;
        }
        [Header("Palm slap response")]
        [SerializeField, Range(0.1f, 2f)] private float airborneImpulseMultiplier = 0.85f;
        [SerializeField, Range(0f, 2f)] private float angularImpulseMultiplier = 0.65f;
        [SerializeField, Min(1f)] private float maximumKnockbackSpeed = 10f;
        [SerializeField, Min(0.1f)] private float maximumAngularSpeed = 7f;
        [SerializeField, Min(0f)] private float stumbleThreshold = 0.4f;
        [SerializeField, Min(0.1f)] private float knockDownThreshold = 1.6f;
        private bool palmFlight;
        private float lastPalmTime;
        private Vector3 pendingVelocity;
        private float pendingAngularVelocity;
        // Several rendered releases may occur before one physics step when cooldown is zero.
        private void FixedUpdate() { pendingVelocity = Vector3.zero; pendingAngularVelocity = 0f; }
        public Vector3 LastSlapDirection { get; private set; }
        public float LastSlapStrength { get; private set; }
        public bool ExternalReaction => palmFlight || Active || Stumbling || motor.ControlSuppressed;
        public string SlapDebug => $"{(motor.Grounded ? "Grounded" : "Airborne")} / " +
            $"{(GettingUp ? "Recovery" : Active ? "KnockDown" : Stumbling ? "Stumble" : "Balance")}\n" +
            $"velocity {motor.Body.linearVelocity}, angular {motor.Body.angularVelocity}\n" +
            $"external {motor.ExternalSource}, AI suppressed {motor.ControlSuppressed}";
        public void PalmSlap(Vector3 impulse, Vector3 point)
        {
            motor.SampleGround();
            motor.NotifyExternal("player-palm-slap");
            impulse.z = 0f;
            if (!motor.Grounded) impulse *= airborneImpulseMultiplier;
            LastSlapStrength = impulse.magnitude;
            LastSlapDirection = impulse.normalized;
            bool knock = Active || LastSlapStrength >= Mathf.Max(stumbleThreshold, knockDownThreshold);
            if (knock)
            {
                Active = true; GettingUp = false;
                timer = stableTime = 0f;
                motor.SetUpright(false);
            }
            if (LastSlapStrength >= stumbleThreshold) stumbleUntil = Time.time + 0.45f;
            palmFlight = true; lastPalmTime = Time.time;
            motor.SuppressControl(Mathf.Lerp(0.18f, 0.65f, Mathf.Clamp01(LastSlapStrength / 4.5f)));
            Rigidbody body = motor.Body;
            point.z = body.worldCenterOfMass.z;
            // Bound the resulting momentum, not each contribution separately. No duplicate impulse.
            Vector3 velocity = body.linearVelocity + pendingVelocity;
            Vector3 target = Vector3.ClampMagnitude(velocity + impulse / body.mass, maximumKnockbackSpeed);
            Vector3 applied = (target - velocity) * body.mass; applied.z = 0f;
            body.AddForce(applied, ForceMode.Impulse);
            pendingVelocity += applied / body.mass;
            if (knock)
            {
                // AddForce + lever-arm torque is equivalent to AddForceAtPosition, with independent
                // angular gain and a predicted angular-speed limit for an asymmetric capsule.
                float torque = Vector3.Cross(point - body.worldCenterOfMass, applied).z * angularImpulseMultiplier;
                Quaternion inertiaFrame = body.rotation * body.inertiaTensorRotation;
                Vector3 axis = Quaternion.Inverse(inertiaFrame) * Vector3.forward;
                Vector3 inertia = body.inertiaTensor;
                float inverseInertia = axis.x * axis.x / Mathf.Max(0.0001f, inertia.x) +
                    axis.y * axis.y / Mathf.Max(0.0001f, inertia.y) + axis.z * axis.z / Mathf.Max(0.0001f, inertia.z);
                float angular = body.angularVelocity.z + pendingAngularVelocity;
                float desired = Mathf.Clamp(angular + torque * inverseInertia,
                    -maximumAngularSpeed, maximumAngularSpeed);
                body.AddTorque(Vector3.forward * ((desired - angular) / inverseInertia), ForceMode.Impulse);
                pendingAngularVelocity += desired - angular;
                body.maxAngularVelocity = maximumAngularSpeed;
            }
        }
        private void LoseBalance(float direction)
        {
            Active = true;
            GettingUp = false;
            timer = stableTime = 0f;
            motor.SetUpright(false);
            motor.Body.AddTorque(Vector3.forward * direction * tumbleImpulse, ForceMode.Impulse);
        }
        public void Tick(float dt)
        {
            if (palmFlight)
            {
                if (Time.time - lastPalmTime < 0.12f || !motor.Grounded || motor.Body.linearVelocity.y > 0.3f) motor.SuppressControl(0.12f);
                else palmFlight = false;
            }
            if (Stumbling) { motor.GroundDrag(1.3f); return; }
            if (!Active) return;
            motor.GroundDrag(GettingUp ? 2f : 1.3f);
            if (!motor.Grounded) { stableTime = 0f; GettingUp = false; return; }
            timer += dt;
            bool settled = motor.Body.linearVelocity.sqrMagnitude < 0.8f && motor.Body.angularVelocity.sqrMagnitude < 1.2f;
            stableTime = settled ? stableTime + dt : 0f;
            if (!GettingUp && (timer < downDuration || stableTime < 0.22f)) return;

            Vector3 candidate = new Vector3(motor.Body.position.x,
                motor.SupportPoint.y + motor.HalfHeight + 0.045f, 0f);
            if (!motor.IsClear(candidate))
            {
                GettingUp = false;
                // Search only local supported space; crawl there with force, never move the Transform.
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 probe = candidate + Vector3.right * side * 0.55f;
                    if (!motor.IsClear(probe) || !motor.GroundAhead(side)) continue;
                    motor.Drive(side * 0.35f, 0.15f);
                    motor.Body.AddTorque(Vector3.forward * -side * 1.2f, ForceMode.Acceleration);
                    break;
                }
                return;
            }
            if (!GettingUp) { GettingUp = true; timer = 0f; }
            float angle = Mathf.DeltaAngle(0f, motor.Body.rotation.eulerAngles.z) * Mathf.Deg2Rad;
            float torque = -angle * 36f - motor.Body.angularVelocity.z * 9f;
            motor.Body.AddTorque(Vector3.forward * Mathf.Clamp(torque, -45f, 45f), ForceMode.Acceleration);
            // A bounded grounded support force helps the capsule pivot, not launch into the air.
            float lift = Mathf.Clamp((candidate.y - motor.Body.position.y) * 20f - motor.Body.linearVelocity.y * 6f, 0f, 9f);
            motor.Body.AddForce(Vector3.up * lift, ForceMode.Acceleration);
            if (timer < getUpDuration || Mathf.Abs(angle) > 0.035f ||
                Mathf.Abs(motor.Body.angularVelocity.z) > 0.2f) return;
            motor.Body.rotation = Quaternion.identity;
            motor.Body.angularVelocity = Vector3.zero;
            motor.SetUpright(true);
            Active = GettingUp = false;
            immunityUntil = Time.time + 0.6f;
        }
        public void ResetAt(Vector3 center)
        {
            Active = GettingUp = false;
            timer = stableTime = 0f;
            motor.Teleport(center); // Explicit technical recovery only, never used by the gameplay loop.
        }
        private void OnCollisionEnter(Collision collision)
        {
            if (Active || Time.time < immunityUntil || collision.contactCount == 0) return;
            bool movingObject = collision.rigidbody && !collision.rigidbody.isKinematic;
            bool hardFall = collision.relativeVelocity.magnitude > 10f;
            if ((!movingObject && !hardFall) || collision.relativeVelocity.magnitude < collisionThreshold) return;
            // PhysX already delivered the collision impulse: do not apply it a second time.
            float side = collision.GetContact(0).point.x - motor.Body.position.x;
            LoseBalance(side >= 0f ? -1f : 1f);
        }
    }
}
