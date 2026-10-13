using UnityEngine;

namespace NeverGiveUp
{
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public sealed class CharacterMotor : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float moveSpeed = 4f;
        [SerializeField, Min(1f)] private float acceleration = 24f;
        [SerializeField, Min(1f)] private float jumpSpeed = 7.8f;
        [SerializeField] private LayerMask groundMask = ~0;
        private readonly RaycastHit[] hits = new RaycastHit[16];
        private readonly Collider[] overlaps = new Collider[16];
        private Rigidbody body;
        private CapsuleCollider capsule;
        private float groundIgnoreUntil;

        public Rigidbody Body => body;
        public CapsuleCollider Capsule => capsule;
        public float MoveSpeed => moveSpeed;
        public float JumpSpeed => jumpSpeed;
        public float HalfHeight => capsule.height * 0.5f;
        public Vector3 Feet => body.position + Vector3.down * HalfHeight;
        public bool Grounded { get; private set; }
        public float Facing { get; private set; } = 1f;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            SetUpright(true);
        }

        public void SampleGround()
        {
            Grounded = false;
            if (Time.time < groundIgnoreUntil) return;
            // A sphere at the lowest point also works while the capsule is lying down.
            Bounds bounds = capsule.bounds;
            Vector3 origin = new Vector3(body.position.x, bounds.min.y + 0.25f, body.position.z);
            int count = Physics.SphereCastNonAlloc(origin, 0.22f, Vector3.down, hits,
                0.16f, groundMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (hits[i].rigidbody == body || hits[i].normal.y < 0.45f) continue;
                Grounded = true;
                break;
            }
        }

        public void Drive(float desiredVelocity)
        {
            if (Mathf.Abs(desiredVelocity) > 0.05f) Facing = Mathf.Sign(desiredVelocity);
            float delta = Mathf.Clamp(desiredVelocity - body.linearVelocity.x,
                -acceleration * Time.fixedDeltaTime, acceleration * Time.fixedDeltaTime);
            body.AddForce(Vector3.right * delta, ForceMode.VelocityChange);
        }

        public void Stop() => Drive(0f);

        public void Jump(float horizontalSpeed, float verticalSpeed)
        {
            if (!Grounded) return;
            body.linearVelocity = new Vector3(horizontalSpeed, verticalSpeed, 0f);
            groundIgnoreUntil = Time.time + 0.18f;
            Grounded = false;
        }

        public bool ObstacleAhead(float direction, out RaycastHit obstacle)
        {
            obstacle = default;
            Vector3 origin = Feet + Vector3.up * 0.45f;
            int count = Physics.RaycastNonAlloc(origin, Vector3.right * direction, hits,
                0.85f, groundMask, QueryTriggerInteraction.Ignore);
            float closest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].rigidbody == body || hits[i].distance >= closest) continue;
                closest = hits[i].distance;
                obstacle = hits[i];
            }
            return closest < float.PositiveInfinity;
        }

        public bool GroundAhead(float direction)
        {
            Vector3 origin = Feet + new Vector3(direction * 0.65f, 0.5f, 0f);
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, 1.1f,
                groundMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (hits[i].rigidbody != body && hits[i].normal.y > 0.45f) return true;
            return false;
        }

        public void SetUpright(bool upright)
        {
            body.constraints = RigidbodyConstraints.FreezePositionZ |
                RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY;
            if (upright) body.constraints |= RigidbodyConstraints.FreezeRotationZ;
        }

        public bool IsClear(Vector3 center)
        {
            float radius = capsule.radius * 0.95f;
            float segment = HalfHeight - capsule.radius;
            int count = Physics.OverlapCapsuleNonAlloc(center + Vector3.up * segment,
                center - Vector3.up * segment, radius, overlaps, groundMask,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (overlaps[i].attachedRigidbody != body) return false;
            return count < overlaps.Length;
        }

        public void Teleport(Vector3 center)
        {
            body.position = new Vector3(center.x, center.y, 0f);
            body.rotation = Quaternion.identity;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            SetUpright(true);
            groundIgnoreUntil = Time.time + 0.1f;
            Grounded = false;
        }
    }
}
