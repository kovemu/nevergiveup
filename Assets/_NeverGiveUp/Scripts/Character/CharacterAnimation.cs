using UnityEngine;

namespace NeverGiveUp
{
    public sealed class CharacterAnimation : MonoBehaviour
    {
        [SerializeField] private CharacterBrain brain;
        [SerializeField] private CharacterMotor motor;
        [SerializeField] private Transform visual;
        [SerializeField] private Transform leftArm;
        [SerializeField] private Transform rightArm;
        [SerializeField] private Transform leftLeg;
        [SerializeField] private Transform rightLeg;
        private float phase;
        private float landing;
        private bool wasGrounded;

        public void Configure(CharacterBrain owner, CharacterMotor movement, Transform root,
            Transform armL, Transform armR, Transform legL, Transform legR)
        {
            brain = owner; motor = movement; visual = root;
            leftArm = armL; rightArm = armR; leftLeg = legL; rightLeg = legR;
        }

        private void LateUpdate()
        {
            if (!brain || !motor || !visual) return;
            if (motor.Grounded && !wasGrounded) landing = 1f;
            wasGrounded = motor.Grounded;
            landing = Mathf.MoveTowards(landing, 0f, Time.deltaTime * 5f);
            bool running = brain.State == CharacterState.Running || brain.State == CharacterState.Retrying;
            bool down = brain.State == CharacterState.KnockedDown;
            bool resting = brain.State == CharacterState.Resting;
            float stride = running ? Mathf.Clamp01(Mathf.Abs(motor.Body.linearVelocity.x) / motor.MoveSpeed) : 0f;
            phase += Time.deltaTime * (running ? 17f : 3f);
            float swing = Mathf.Sin(phase);
            float crouch = resting ? 0.36f : brain.PreparingJump ? 0.18f : landing * 0.14f;
            Vector3 scale = new Vector3(1f + crouch * 0.4f, 1f - crouch, 1f + crouch * 0.4f);
            visual.localScale = Vector3.Lerp(visual.localScale, scale, Time.deltaTime * 18f);
            visual.localPosition = Vector3.Lerp(visual.localPosition,
                new Vector3(0f, Mathf.Abs(swing) * 0.045f * stride - (resting ? 0.27f : crouch * 0.35f), 0f),
                Time.deltaTime * 9f);
            float tilt = running ? -motor.Facing * 7f : 0f;
            if (brain.State == CharacterState.Stumbling) tilt = Mathf.Sin(Time.time * 15f) * 8f;
            if (brain.State == CharacterState.InspectingObstacle) tilt = motor.Facing * 9f;
            visual.localRotation = Quaternion.Slerp(visual.localRotation,
                Quaternion.Euler(0f, 0f, tilt), Time.deltaTime * 9f);
            // Face-free emotion: arms droop, knees bend and the clay body settles.
            // Physics capsule stays at its real position; only the visual crouches.
            float spread = resting ? 10f : down ? 25f : 65f;
            leftArm.localRotation = Quaternion.Slerp(leftArm.localRotation,
                Quaternion.Euler(swing * 16f * stride, 0f, -spread - swing * 7f), Time.deltaTime * 12f);
            rightArm.localRotation = Quaternion.Slerp(rightArm.localRotation,
                Quaternion.Euler(-swing * 16f * stride, 0f, spread - swing * 7f), Time.deltaTime * 12f);
            leftLeg.localRotation = Quaternion.Slerp(leftLeg.localRotation,
                Quaternion.Euler(0f, 0f, resting ? -58f : swing * 32f * stride), Time.deltaTime * 10f);
            rightLeg.localRotation = Quaternion.Slerp(rightLeg.localRotation,
                Quaternion.Euler(0f, 0f, resting ? 58f : -swing * 32f * stride), Time.deltaTime * 10f);
        }
    }
}
