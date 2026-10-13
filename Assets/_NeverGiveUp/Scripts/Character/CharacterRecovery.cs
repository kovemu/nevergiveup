using UnityEngine;

namespace NeverGiveUp
{
    [RequireComponent(typeof(CharacterMotor))]
    public sealed class CharacterRecovery : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float downDuration = 1.15f;
        [SerializeField, Min(0.1f)] private float getUpDuration = 0.65f;
        [SerializeField, Min(0.1f)] private float knockdownImpulse = 2.5f;
        [SerializeField, Min(0.1f)] private float collisionThreshold = 5.5f;
        private CharacterMotor motor;
        private float timer;
        private float totalTime;
        private float immunityUntil;
        public bool Active { get; private set; }
        public bool GettingUp { get; private set; }
        public bool NeedsRespawn { get; private set; }

        private void Awake() => motor = GetComponent<CharacterMotor>();

        public void Flick(Vector3 impulse, Vector3 point)
        {
            if (impulse.magnitude >= knockdownImpulse)
            {
                Active = true;
                GettingUp = false;
                NeedsRespawn = false;
                timer = 0f;
                totalTime = 0f;
                motor.SetUpright(false);
                float sign = Mathf.Abs(impulse.x) > 0.01f ? -Mathf.Sign(impulse.x) : 1f;
                motor.Body.AddTorque(Vector3.forward * sign * 3f, ForceMode.Impulse);
            }
            // Impulse lies in XY. Click point supplies additional natural rotation.
            impulse.z = 0f;
            point.z = motor.Body.position.z;
            motor.Body.AddForceAtPosition(impulse, point, ForceMode.Impulse);
        }

        public void Tick(float dt)
        {
            if (!Active) return;
            totalTime += dt;
            if (totalTime > 7f) { NeedsRespawn = true; return; }
            if (!GettingUp)
            {
                if (motor.Grounded) timer += dt;
                if (timer < downDuration) return;
                GettingUp = true;
                timer = 0f;
            }
            timer += dt;
            // Dynamic torque keeps collision response alive during the entire recovery.
            float angle = Mathf.DeltaAngle(0f, motor.Body.rotation.eulerAngles.z) * Mathf.Deg2Rad;
            float torque = -angle * 65f - motor.Body.angularVelocity.z * 13f;
            motor.Body.AddTorque(Vector3.forward * Mathf.Clamp(torque, -90f, 90f), ForceMode.Acceleration);
            motor.Drive(0f);
            // Help the centre rise as the capsule rotates around its contact with the ground.
            if (motor.Grounded && Mathf.Abs(angle) > 0.15f)
                motor.Body.AddForce(Vector3.up * 11f, ForceMode.Acceleration);
            if (timer < getUpDuration || Mathf.Abs(angle) > 0.12f || !motor.Grounded) return;
            if (!motor.IsClear(motor.Body.position + Vector3.up * 0.035f)) return;
            motor.Body.rotation = Quaternion.identity;
            motor.Body.angularVelocity = Vector3.zero;
            motor.SetUpright(true);
            Active = GettingUp = false;
            immunityUntil = Time.time + 0.8f;
        }

        public void ResetAt(Vector3 center)
        {
            Active = GettingUp = NeedsRespawn = false;
            timer = totalTime = 0f;
            immunityUntil = Time.time + 0.8f;
            motor.Teleport(center);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (Active || Time.time < immunityUntil || collision.contactCount == 0) return;
            ContactPoint contact = collision.GetContact(0);
            // Normal jumping/landing is not a knockdown; falling boxes and hard falls are.
            bool movingObject = collision.rigidbody && !collision.rigidbody.isKinematic;
            bool hardFall = collision.relativeVelocity.magnitude > 12f;
            if (!movingObject && !hardFall) return;
            if (collision.relativeVelocity.magnitude < collisionThreshold) return;
            Vector3 impulse = contact.normal * Mathf.Min(collision.relativeVelocity.magnitude * 0.45f, 6f);
            Flick(impulse, contact.point);
        }
    }
}
