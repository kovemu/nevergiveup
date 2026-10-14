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
        public void Flick(Vector3 impulse, Vector3 point)
        {
            motor.NotifyExternal("hand flick");
            impulse.z = 0f;
            point.z = motor.Body.position.z;
            stumbleUntil = Time.time + 0.5f;
            if (impulse.magnitude >= knockdownImpulse)
                LoseBalance(Mathf.Abs(impulse.x) > 0.01f ? -Mathf.Sign(impulse.x) : 1f);
            motor.Body.AddForceAtPosition(impulse, point, ForceMode.Impulse);
        }
        private void LoseBalance(float direction)
        {
            Active = true;
            GettingUp = false;
            timer = stableTime = 0f;
            motor.SetUpright(false);
            motor.Body.AddTorque(Vector3.forward * direction * tumbleImpulse, ForceMode.Impulse);
        }
        public void Push(float direction, Vector3 point)
        {
            if (!motor.Grounded || Mathf.Abs(point.x - motor.Body.position.x) > 0.65f ||
                Mathf.Abs(point.y - motor.Body.position.y) > 0.8f) return;
            motor.NotifyExternal("hand push");
            // Horizontal contact force with a terminal speed, never lift or velocity snapping.
            float along = motor.Body.linearVelocity.x * direction;
            float force = 1.6f * Mathf.Clamp01(1f - along / 1.2f);
            motor.Body.AddForce(Vector3.right * direction * force, ForceMode.Force);
            stumbleUntil = Time.time + 0.18f;
        }
        public void Tick(float dt)
        {
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
