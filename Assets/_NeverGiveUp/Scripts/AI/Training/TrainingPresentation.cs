using UnityEngine;

namespace NeverGiveUp.Training
{
    // Separate presentation adapter: never writes the physics root or imported clips.
    public sealed class TrainingPresentation : MonoBehaviour
    {
        public ClimbAgent agent;
        public Animator animator;
        public bool Ready => isActiveAndEnabled && animator && animator.runtimeAnimatorController;
        string current;
        void LateUpdate()
        {
            if(!agent || !agent.Motor || !animator)return;
            var motor=agent.Motor;
            string state=!motor.Grounded ? (motor.Body.linearVelocity.y>.1f ?
                (agent.CurrentJump==JumpMode.Short?"NGU_JumpShort":"NGU_JumpLong"):"NGU_Fall") :
                agent.Preparation>0?"NGU_Inspect":agent.Settling?"NGU_Land":
                Mathf.Abs(motor.Body.linearVelocity.x)>.15f?"NGU_Run":"NGU_Idle";
            if(state!=current)
            {animator.CrossFadeInFixedTime("Base Layer."+state,.15f);current=state;}
            animator.applyRootMotion=false;
            var target=Quaternion.Euler(0,motor.Facing>0?90:-90,0);
            if(motor.Grounded)transform.localRotation=Quaternion.RotateTowards(transform.localRotation,target,150*Time.deltaTime);
            motor.VisualFacingAlignment=Mathf.Clamp01(1-Quaternion.Angle(transform.localRotation,target)/90);
        }
    }
}
