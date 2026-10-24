using UnityEngine;

namespace NeverGiveUp.Training
{
    // Separate presentation adapter: never writes the physics root or imported clips.
    public sealed class TrainingPresentation : MonoBehaviour
    {
        public ClimbAgent agent;
        public Animator animator;
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
            transform.localRotation=Quaternion.RotateTowards(transform.localRotation,
                Quaternion.Euler(0,motor.Facing>0?90:-90,0),150*Time.deltaTime);
        }
    }
}
