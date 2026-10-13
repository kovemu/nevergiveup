using UnityEngine;

namespace NeverGiveUp
{
    public sealed class CameraFollow : MonoBehaviour
    {
        [SerializeField] private CharacterMotor target;
        [SerializeField] private Vector3 offset = new Vector3(0f, 1.2f, -20f);
        [SerializeField, Min(0.01f)] private float smoothTime = 0.38f;
        [SerializeField] private float lookAhead = 1.6f;
        [SerializeField] private float maximumFollowSpeed = 12f;
        private Vector3 velocity;
        public void Configure(CharacterMotor character) => target = character;
        private void Start() { if (target) transform.position = target.transform.position + offset; }
        private void LateUpdate()
        {
            if (!target) return;
            Vector3 desired = target.transform.position + offset;
            desired.x += target.Facing * lookAhead;
            float damping = target.Body.linearVelocity.y < -4f ? smoothTime * 1.8f : smoothTime;
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref velocity,
                damping, maximumFollowSpeed);
        }
    }
}
