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
        [Header("Optional imported rig: child Animator, in-place NGU_* states on layer 0")]
        [SerializeField] private Animator animator;
        [SerializeField] private Transform head;
        private CharacterRecovery recovery;
        private ClayAnimator clayAnimator;
        public float SitSeconds => clayAnimator && clayAnimator.Ready ? clayAnimator.SitSeconds : 0.35f;
        public float StandSeconds => clayAnimator && clayAnimator.Ready ? clayAnimator.StandSeconds : 0.5f;
        private bool reactionPose;
        private bool animatorWasEnabled;
        private readonly Quaternion[] savedPose = new Quaternion[5];
        private Transform[] reactionBones;
        private Vector3 animatorPosition;
        private Quaternion animatorRotation;
        private string currentAction;
        public string CurrentAction => currentAction;
        private void Awake()
        {
            recovery = GetComponent<CharacterRecovery>();
            clayAnimator = GetComponentInChildren<ClayAnimator>();
            if (visual) visual.gameObject.SetActive(!(clayAnimator && clayAnimator.Ready));
            if (clayAnimator && !clayAnimator.Ready) clayAnimator.gameObject.SetActive(false);
            if (!(clayAnimator && clayAnimator.Ready) && visual)
            {
                // Older scenes hid these six primitive children when M4 art was added.
                // Restore only known fallback parts; keep extra imported art hidden.
                foreach (Transform child in visual)
                    child.gameObject.SetActive(child == leftArm || child == rightArm ||
                        child == leftLeg || child == rightLeg ||
                        child.name == "Round Head - no face" || child.name == "Soft Body");
            }
            if (!animator) animator = GetComponentInChildren<Animator>();
            // An Animator must never own the physics root.
            if (animator && animator.transform == transform)
            {
                Debug.LogWarning("Place the visual Animator on a child of the physics root.", this);
                animator.enabled = false; animator = null;
            }
            if (animator)
            {
                animator.applyRootMotion = false;
                animatorPosition = animator.transform.localPosition;
                animatorRotation = animator.transform.localRotation;
                if (animator.isHuman)
                {
                    leftArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                    rightArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                    leftLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                    rightLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                    head = animator.GetBoneTransform(HumanBodyBones.Head);
                }
            }
            if (!head && visual) head = visual.Find("Round Head - no face");
            reactionBones = new[] { leftArm, rightArm, leftLeg, rightLeg, head };
        }
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
            if (clayAnimator && clayAnimator.Ready)
            { clayAnimator.Tick(); currentAction = clayAnimator.CurrentAction; return; }
            if (!brain || !motor || !visual) return;
            if (motor.Grounded && !wasGrounded) landing = 1f;
            wasGrounded = motor.Grounded;
            landing = Mathf.MoveTowards(landing, 0f, Time.deltaTime * 5f);
            bool airborneReaction = recovery && recovery.ExternalReaction && !motor.Grounded;
            bool imported = animator && animator.runtimeAnimatorController;
            bool physicalPose = imported && recovery && (recovery.Active && !recovery.GettingUp || airborneReaction);
            SetPhysicalPose(physicalPose);
            string action = ActionName();
            if (imported)
            {
                if (!physicalPose && animator.enabled && action != currentAction)
                {
                    int hash = Animator.StringToHash("Base Layer." + action);
                    if (animator.HasState(0, hash)) animator.CrossFadeInFixedTime(hash, 0.12f, 0);
                }
                // Only the Rigidbody can move/rotate the world actor. Imported clips must be in-place.
                animator.transform.localPosition = animatorPosition;
                animator.transform.localRotation = animatorRotation;
                if (physicalPose) PoseReaction(true);
                currentAction = action;
                return;
            }
            currentAction = action;
            if (!leftArm || !rightArm || !leftLeg || !rightLeg) return;

            bool running = brain.State == CharacterState.Running || brain.State == CharacterState.Retrying;
            bool down = brain.State == CharacterState.KnockedDown;
            bool resting = brain.State == CharacterState.Resting || brain.State == CharacterState.SittingDown;
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
            if (airborneReaction) { PoseReaction(false); return; }
            float spread = resting ? 10f : down ? 25f : 65f;
            leftArm.localRotation = Quaternion.Slerp(leftArm.localRotation,
                Quaternion.Euler(swing * 16f * stride, 0f, -spread - swing * 7f), Time.deltaTime * 12f);
            rightArm.localRotation = Quaternion.Slerp(rightArm.localRotation,
                Quaternion.Euler(-swing * 16f * stride, 0f, spread - swing * 7f), Time.deltaTime * 12f);
            leftLeg.localRotation = Quaternion.Slerp(leftLeg.localRotation,
                Quaternion.Euler(0f, 0f, resting ? -58f : swing * 32f * stride), Time.deltaTime * 10f);
            rightLeg.localRotation = Quaternion.Slerp(rightLeg.localRotation,
                Quaternion.Euler(0f, 0f, resting ? 58f : -swing * 32f * stride), Time.deltaTime * 10f);
            if (head)
                head.localRotation = Quaternion.Slerp(head.localRotation,
                    Quaternion.Euler(0, 0, recovery && recovery.Stumbling ? Mathf.Sin(Time.time * 13f) * 5f : 0f),
                    Time.deltaTime * 10f);
        }
        private string ActionName()
        {
            if (recovery && recovery.GettingUp) return "NGU_GetUpFromKnockDown";
            if (recovery && recovery.Active) return motor.Grounded ? "NGU_KnockDown" : "NGU_Fall";
            switch (brain.State)
            {
                case CharacterState.SittingDown: return "NGU_SitDown";
                case CharacterState.Resting: return "NGU_Rest";
                case CharacterState.GettingUpFromSit: return "NGU_GetUpFromSit";
                case CharacterState.Stumbling: return "NGU_Stumble";
                case CharacterState.Jumping: return brain.CurrentJumpMode == JumpMode.Short ? "NGU_JumpShort" : "NGU_JumpLong";
                case CharacterState.Falling: return "NGU_Fall";
                case CharacterState.InspectingObstacle: return "NGU_Inspect";
                case CharacterState.Running:
                case CharacterState.Retrying: return "NGU_Run";
                default: return motor.Grounded && landing > 0.1f ? "NGU_Land" : "NGU_Idle";
            }
        }
        private void SetPhysicalPose(bool enabled)
        {
            if (enabled == reactionPose) return;
            if (enabled)
            {
                animatorWasEnabled = animator.enabled;
                animator.enabled = false; // Single owner per bone, no Animator/procedural accumulation.
                for (int i = 0; i < reactionBones.Length; i++)
                    if (reactionBones[i]) savedPose[i] = reactionBones[i].localRotation;
            }
            else
            {
                for (int i = 0; i < reactionBones.Length; i++)
                    if (reactionBones[i]) reactionBones[i].localRotation = savedPose[i];
                if (animator) animator.enabled = animatorWasEnabled;
                currentAction = null;
            }
            reactionPose = enabled;
        }
        private void PoseReaction(bool relative)
        {
            float intensity = Mathf.Clamp01(recovery.LastSlapStrength / 4.5f);
            float wave = Mathf.Sin(Time.time * 8f);
            float roll = Mathf.Clamp(motor.Body.angularVelocity.z * 2f, -14f, 14f);
            for (int i = 0; i < reactionBones.Length; i++)
            {
                Transform bone = reactionBones[i];
                if (!bone) continue;
                float sign = i % 2 == 0 ? -1f : 1f;
                float angle = i < 2 ? sign * (55f + wave * (12f + intensity * 15f)) + roll :
                    i < 4 ? sign * Mathf.Sin(Time.time * 9f + 0.8f) * (15f + intensity * 22f) :
                    -recovery.LastSlapDirection.x * 5f + wave * 4f - roll * 0.2f;
                Quaternion pose = (relative ? savedPose[i] : Quaternion.identity) * Quaternion.Euler(0, 0, angle);
                bone.localRotation = Quaternion.Slerp(bone.localRotation, pose, Time.deltaTime * 12f);
            }
        }
        private void OnDisable() { if (reactionPose) SetPhysicalPose(false); }
    }
}
