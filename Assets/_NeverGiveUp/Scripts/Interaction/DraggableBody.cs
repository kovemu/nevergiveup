using UnityEngine;

namespace NeverGiveUp
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class DraggableBody : MonoBehaviour
    {
        [SerializeField] private float resetBelowY = -8f;
        private Rigidbody body;
        private Vector3 origin;
        public Rigidbody Body => body;
        public bool Held { get; set; }
        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            origin = body.position;
            body.constraints = RigidbodyConstraints.FreezePositionZ |
                RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }
        private void FixedUpdate()
        {
            if (body.position.y >= resetBelowY || Held) return;
            body.position = origin;
            body.rotation = Quaternion.identity;
            body.linearVelocity = body.angularVelocity = Vector3.zero;
        }
    }
}
