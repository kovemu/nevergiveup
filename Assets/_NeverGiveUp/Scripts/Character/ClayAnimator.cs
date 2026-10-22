using UnityEngine;

namespace NeverGiveUp
{
    // Presentation only. No force, velocity, collider, learning or world-root writes.
    public sealed class ClayAnimator : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private Transform leftArm, rightArm, leftLeg, rightLeg, head;
        [SerializeField] private float seatedDownSeconds = 1.35f;
        [SerializeField] private float seatedUpSeconds = 1.8f;
        private CharacterBrain brain;
        private CharacterMotor motor;
        private CharacterRecovery recovery;
        private bool wasGrounded;
        private float landUntil, recoveryProgress;
        private Vector3 basePosition;
        private Quaternion facingRotation;
        private string action;
        public string CurrentAction => action;
        public bool Ready => animator && animator.runtimeAnimatorController;
        public float SitSeconds => seatedDownSeconds;
        public float StandSeconds => seatedUpSeconds;
        public Animator Animator => animator;

        public void Configure(Animator value, Transform armL, Transform armR, Transform legL, Transform legR, Transform skull)
        { animator = value; leftArm = armL; rightArm = armR; leftLeg = legL; rightLeg = legR; head = skull; }
        private void Awake()
        {
            basePosition = transform.localPosition;
            facingRotation = transform.localRotation;
            brain = GetComponentInParent<CharacterBrain>();
            motor = GetComponentInParent<CharacterMotor>();
            recovery = GetComponentInParent<CharacterRecovery>();
            if (animator) { animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; }
        }
        public void Tick()
        {
            if (!Ready || !brain || !motor) return;
            bool physical = recovery && recovery.Active;
            bool airHit = recovery && recovery.ExternalReaction && !motor.Grounded;
            if (motor.Grounded && !wasGrounded && !physical) landUntil = Time.time + 0.18f;
            wasGrounded = motor.Grounded;
            string next = Choose(physical, airHit);
            int hash = Animator.StringToHash("Base Layer." + next);
            if (!animator.HasState(0, hash))
            {
                // Missing authored clip: keep a valid idle/fall pose and add a small procedural response.
                next = airHit || !motor.Grounded ? "NGU_Fall" : "NGU_Idle";
                hash = Animator.StringToHash("Base Layer." + next);
            }
            if (next != action)
            {
                if (animator.HasState(0, hash)) animator.CrossFadeInFixedTime(hash, physical ? 0.10f : 0.12f, 0);
                action = next;
                if (action == "NGU_GetUpFromKnockDown") recoveryProgress = 0f;
            }
            if (recovery && recovery.GettingUp)
            {
                // Scrub recovery by actual upright progress; animation never ends physics recovery.
                float upright = 1f - Mathf.Clamp01(Mathf.Abs(Mathf.DeltaAngle(0, motor.Body.rotation.eulerAngles.z)) / 85f);
                recoveryProgress = Mathf.Max(recoveryProgress, Mathf.MoveTowards(recoveryProgress, upright, Time.deltaTime * 1.8f));
                animator.SetFloat("RecoveryProgress", recoveryProgress);
            }
            if (!physical)
            {
                facingRotation = Quaternion.RotateTowards(facingRotation,
                    Quaternion.Euler(0, motor.Facing > 0 ? 90 : -90, 0), Time.deltaTime * 540f);
            }
            transform.localRotation = facingRotation;
            transform.localPosition = basePosition;
            if (airHit) Flail();
        }
        private string Choose(bool physical, bool airHit)
        {
            if (recovery && recovery.GettingUp) return "NGU_GetUpFromKnockDown";
            if (airHit) return "NGU_AirFlail";
            if (physical) return "NGU_KnockDown";
            if (recovery && recovery.Stumbling) return "NGU_Stumble";
            if (brain.State == CharacterState.SittingDown) return "NGU_SitDown";
            if (brain.State == CharacterState.Resting) return "NGU_Rest";
            if (brain.State == CharacterState.GettingUpFromSit) return "NGU_GetUpFromSit";
            if (!motor.Grounded)
                return motor.Body.linearVelocity.y > 0.1f ? (brain.CurrentJumpMode == JumpMode.Short ? "NGU_JumpShort" : "NGU_JumpLong") : "NGU_Fall";
            if (Time.time < landUntil) return "NGU_Land";
            if (brain.State == CharacterState.InspectingObstacle) return "NGU_Inspect";
            if ((brain.State == CharacterState.Running || brain.State == CharacterState.Retrying) && Mathf.Abs(motor.Body.linearVelocity.x) > 0.12f) return "NGU_Run";
            return "NGU_Idle";
        }
        private void Flail()
        {
            // Animator has evaluated the base Fall pose this frame. Each delta is applied once.
            float strength = Mathf.Clamp01(recovery.LastSlapStrength / 4.5f);
            float wave = Mathf.Sin(Time.time * 8f);
            Rotate(leftArm, Vector3.forward, wave * (8 + strength * 14));
            Rotate(rightArm, Vector3.forward, -wave * (8 + strength * 14));
            Rotate(leftLeg, Vector3.right, Mathf.Sin(Time.time * 9f) * (6 + strength * 12));
            Rotate(rightLeg, Vector3.right, -Mathf.Sin(Time.time * 9f + 0.7f) * (6 + strength * 12));
            Rotate(head, Vector3.forward, wave * 3);
        }
        private void Rotate(Transform bone, Vector3 modelAxis, float angle)
        {
            if (!bone) return;
            Vector3 worldAxis = animator.transform.TransformDirection(modelAxis);
            bone.rotation = Quaternion.AngleAxis(angle, worldAxis) * bone.rotation;
        }
    }
}
