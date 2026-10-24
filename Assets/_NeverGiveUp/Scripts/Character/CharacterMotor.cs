using System;
using UnityEngine;

namespace NeverGiveUp
{
    public enum JumpMode { Short, Long }

    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public sealed class CharacterMotor : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float moveSpeed = 4f;
        [SerializeField, Min(1f)] private float acceleration = 24f;
        [Header("Ground tempo (jump reach remains independent)")]
        [SerializeField, Range(.1f, 1f)] private float groundSpeedMultiplier = .7f;
        [SerializeField, Range(.1f, 1f)] private float groundAccelerationMultiplier = 1f / 3f;
        [SerializeField, Min(1f)] private float groundDeceleration = 10f;
        [SerializeField, Min(.01f)] private float facingDeadband = .25f;
        [SerializeField, Min(0f)] private float facingIntentSeconds = .25f;
        private float pendingFacing = 1f, facingSince;
        private float facingRequestedAt = -100f;
        private float visualAlignment = 1f, visualReportedAt = -100f;
        public float VisualFacingAlignment
        {
            get => Time.time - visualReportedAt < .2f ? visualAlignment : 1f;
            set { visualAlignment = Mathf.Clamp01(value); visualReportedAt = Time.time; }
        }
        [SerializeField, Min(1f)] private float jumpSpeed = 7.8f; // Hard upper bound retained for older scenes.
        [SerializeField, Min(0.2f)] private float shortJumpHeight = 1.55f;
        [SerializeField, Min(0.2f)] private float longJumpHeight = 3.0f;
        [SerializeField] private LayerMask groundMask = ~0;
        private readonly RaycastHit[] hits = new RaycastHit[16];
        private static readonly float[] ObstacleScanHeights = { 0.33f, 0.9f, 1.43f };
        private readonly Collider[] overlaps = new Collider[16];
        private Rigidbody body;
        private CapsuleCollider capsule;
        private float groundIgnoreUntil;
        private Collider contactSupport;
        private Vector3 contactSupportPoint;
        private float supportContactAt = -100f;
        private Vector3 supportedBodyPosition;
        private Quaternion supportedBodyRotation;
        private Matrix4x4 supportMatrix;
        private PhysicsMaterial standingMaterial;
        private PhysicsMaterial fallenMaterial;
        [SerializeField, Min(0.1f)] private float airAcceleration = 3f;
        public Collider Support { get; private set; }
        public Vector3 SupportPoint { get; private set; }
        public int ExternalVersion { get; private set; }
        public float LastExternalTime { get; private set; } = -100f;
        public string ExternalSource { get; private set; } = "none";
        public event Action<Collision> Contact;
        private float controlSuppressedUntil;
        public float DesiredVelocity { get; private set; }
        private float commandAt = -100f;
        private Collider blockedCollider;
        private float blockedSince, blockedAt;
        public Vector3 CommandVelocity { get; private set; }
        public bool ControlSuppressed => Time.time < controlSuppressedUntil;
        public void SuppressControl(float seconds)
        { controlSuppressedUntil = Mathf.Max(controlSuppressedUntil, Time.time + seconds); }
        public float MaxJumpHorizontal => moveSpeed * 1.6f;
        public float AirAcceleration => airAcceleration;

        public Rigidbody Body => body;
        public CapsuleCollider Capsule => capsule;
        public float MoveSpeed => moveSpeed * groundSpeedMultiplier;
        // Existing JSON map identity describes reach, not cosmetic/ground tempo tuning.
        public float JumpReferenceSpeed => moveSpeed;
        public float JumpSpeed => jumpSpeed;
        public float ShortJumpHeight => Mathf.Min(shortJumpHeight, longJumpHeight, MaxJumpHeight);
        public float LongJumpHeight => Mathf.Min(Mathf.Max(longJumpHeight, shortJumpHeight), MaxJumpHeight);
        private float MaxJumpHeight => Physics.gravity.y < -0.001f ?
            jumpSpeed * jumpSpeed / (-2f * Physics.gravity.y) : 0f;
        public float JumpVelocity(JumpMode mode)
        {
            float height = mode == JumpMode.Short ? ShortJumpHeight : LongJumpHeight;
            return Physics.gravity.y < -0.001f ? Mathf.Sqrt(-2f * Physics.gravity.y * height) : 0f;
        }

        public bool CanReach(Vector3 from, Vector3 to, JumpMode mode)
        {
            float g = -Physics.gravity.y;
            float vertical = JumpVelocity(mode);
            float discriminant = vertical * vertical - 2f * g * (to.y - from.y);
            if (g < 0.001f || discriminant <= 0.0001f) return false;
            float duration = (vertical + Mathf.Sqrt(discriminant)) / g;
            return Mathf.Abs(to.x - from.x) <= MaxJumpHorizontal * duration + 0.01f;
        }
        public bool CanReach(Vector3 from, Vector3 to)
        {
            return CanReach(from, to, JumpMode.Short) || CanReach(from, to, JumpMode.Long);
        }
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
            standingMaterial = new PhysicsMaterial("Character standing (runtime)");
            PhysicsMaterial original = capsule.sharedMaterial;
            standingMaterial.dynamicFriction = original && original.dynamicFriction > 0f ? original.dynamicFriction : 0.08f;
            standingMaterial.staticFriction = original && original.staticFriction > 0f ? original.staticFriction : 0.12f;
            standingMaterial.frictionCombine = PhysicsMaterialCombine.Minimum;
            fallenMaterial = new PhysicsMaterial("Character fallen (runtime)")
            { dynamicFriction = 0.12f, staticFriction = 0.2f, frictionCombine = PhysicsMaterialCombine.Minimum };
            SetUpright(true);
        }

        public void SampleGround()
        {
            Grounded = false;
            Support = null;
            float nearest = float.PositiveInfinity;
            if (Time.time < groundIgnoreUntil) return;
            // A sphere at the lowest point also works while the capsule is lying down.
            Bounds bounds = capsule.bounds;
            Vector3 origin = new Vector3(body.position.x, bounds.min.y + 0.25f, body.position.z);
            int count = Physics.SphereCastNonAlloc(origin, 0.22f, Vector3.down, hits,
                0.16f, groundMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (hits[i].rigidbody == body || hits[i].normal.y < 0.45f) continue;
                if (hits[i].distance >= nearest) continue;
                nearest = hits[i].distance;
                Grounded = true;
                Support = hits[i].collider;
                SupportPoint = hits[i].point;
            }
            // The narrow center probe can miss a real capsule/ledge corner contact.
            // Only a recent upward contact on the lower capsule counts; walls and
            // ceilings never supply support, and the post-jump ignore still applies.
            if (!Grounded && contactSupport && contactSupport.enabled &&
                contactSupport.gameObject.activeInHierarchy &&
                (Time.fixedTime - supportContactAt <= Time.fixedDeltaTime * 2.1f ||
                    body.IsSleeping() && body.position == supportedBodyPosition &&
                    body.rotation == supportedBodyRotation &&
                    contactSupport.transform.localToWorldMatrix == supportMatrix) &&
                body.linearVelocity.y <= .3f)
            {
                Grounded = true;
                Support = contactSupport;
                SupportPoint = contactSupportPoint;
            }
        }

        public void Drive(float desiredVelocity, float authority = 1f)
        {
            if (ControlSuppressed) return;
            if (Mathf.Abs(desiredVelocity) >= facingDeadband) LookToward(desiredVelocity);
            if (Grounded)
            {
                desiredVelocity = Mathf.Clamp(desiredVelocity, -MoveSpeed, MoveSpeed);
                if (Mathf.Abs(desiredVelocity) >= facingDeadband && Mathf.Sign(desiredVelocity) != Facing)
                    desiredVelocity = 0f;
                else if (VisualFacingAlignment < .85f)
                    desiredVelocity *= Mathf.Lerp(.1f, 1f, Mathf.Clamp01(VisualFacingAlignment));
            }
            DesiredVelocity = desiredVelocity; commandAt = Time.time;
            CommandVelocity = body.linearVelocity;
            float rate = Grounded ? (Mathf.Abs(desiredVelocity) < Mathf.Abs(body.linearVelocity.x) ?
                groundDeceleration : acceleration * groundAccelerationMultiplier) : airAcceleration;
            float delta = Mathf.Clamp(desiredVelocity - body.linearVelocity.x,
                -rate * authority * Time.fixedDeltaTime, rate * authority * Time.fixedDeltaTime);
            body.AddForce(Vector3.right * delta, ForceMode.VelocityChange);
        }

        public void LookToward(float direction)
        {
            if (Mathf.Abs(direction) < facingDeadband) return;
            float sign = Mathf.Sign(direction);
            if (Time.time - facingRequestedAt > Mathf.Max(.08f, Time.fixedDeltaTime * 2.5f))
                facingSince = Time.time;
            facingRequestedAt = Time.time;
            if (sign == Facing) { pendingFacing = sign; facingSince = Time.time; return; }
            if (pendingFacing != sign) { pendingFacing = sign; facingSince = Time.time; }
            if (Time.time - facingSince >= facingIntentSeconds)
            { Facing = sign; VisualFacingAlignment = 0f; }
        }

        public void Stop() => Drive(0f);

        public void Jump(float horizontalSpeed, float verticalSpeed)
        {
            if (!Grounded || ControlSuppressed) return;
            horizontalSpeed = Mathf.Clamp(horizontalSpeed, -MaxJumpHorizontal, MaxJumpHorizontal);
            verticalSpeed = Mathf.Clamp(verticalSpeed, 0f, jumpSpeed);
            DesiredVelocity = horizontalSpeed; commandAt = Time.time;
            CommandVelocity = new Vector3(horizontalSpeed, verticalSpeed, 0f);
            body.AddForce(new Vector3(horizontalSpeed - body.linearVelocity.x,
                verticalSpeed - body.linearVelocity.y, 0f), ForceMode.VelocityChange);
            groundIgnoreUntil = Time.time + 0.18f;
            Grounded = false;
        }

        // Side-on capsule-height scan; the old single low ray was blind to overhangs
        // and often mistook an ordinary sloped floor for a blocking wall.
        public bool ObstacleAhead(float direction, out RaycastHit obstacle)
        {
            obstacle = default;
            if (Mathf.Abs(direction) < 0.1f) return false;
            float closest = float.PositiveInfinity;
            for (int level = 0; level < ObstacleScanHeights.Length; level++)
            {
                Vector3 origin = Feet + Vector3.up * ObstacleScanHeights[level];
                int count = Physics.SphereCastNonAlloc(origin, 0.12f,
                    Vector3.right * Mathf.Sign(direction), hits, 0.9f,
                    groundMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    RaycastHit hit = hits[i];
                    if (!hit.collider || hit.rigidbody == body || hit.collider == Support ||
                        hit.distance >= closest || Mathf.Abs(hit.normal.x) < 0.45f) continue;
                    closest = hit.distance;
                    obstacle = hit;
                }
            }
            return closest < float.PositiveInfinity;
        }

        public bool GroundAhead(float direction)
        {
            Vector3 origin = new Vector3(body.position.x + direction * 0.65f, capsule.bounds.min.y + 0.5f, 0f);
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
            if (standingMaterial) capsule.sharedMaterial = upright ? standingMaterial : fallenMaterial;
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

        public void NotifyExternal(string source)
        {
            ExternalVersion++;
            LastExternalTime = Time.time;
            ExternalSource = source;
        }

        public void GroundDrag(float rate)
        {
            if (Grounded) body.AddForce(Vector3.right * (-body.linearVelocity.x * rate), ForceMode.Acceleration);
        }

        // Contact alone is not obstruction: require an opposed command/approach or
        // sustained loss of commanded progress. Upward support normals never qualify.
        public bool TryObstacle(Collision collision, out ObstacleContact obstacle)
        {
            obstacle = default;
            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint c = collision.GetContact(i);
                bool ceiling = c.normal.y < -0.55f && c.point.y > body.worldCenterOfMass.y + 0.15f;
                bool side = Mathf.Abs(c.normal.x) > 0.65f && c.normal.y < 0.45f &&
                    c.point.y > capsule.bounds.min.y + 0.25f;
                if (!ceiling && !side) continue;
                // Unity supplies the other body's velocity relative to this body.
                // Incoming motion projects positively onto our outward contact normal.
                float closing = Mathf.Max(0f, Vector3.Dot(collision.relativeVelocity, c.normal));
                bool opposed = Time.time - commandAt < 0.15f && DesiredVelocity * c.normal.x < -0.2f;
                bool stalled = side && opposed && Mathf.Abs(body.linearVelocity.x) < Mathf.Abs(DesiredVelocity) * 0.4f;
                if (stalled)
                {
                    if (blockedCollider != collision.collider || Time.time - blockedAt > 0.08f) blockedSince = Time.time;
                    blockedCollider = collision.collider; blockedAt = Time.time;
                }
                bool sustained = stalled && Time.time - blockedSince > 0.18f;
                bool movingIntoActor = collision.rigidbody &&
                    Vector3.Dot(collision.rigidbody.GetPointVelocity(c.point), c.normal) > 0.5f;
                if (!(ceiling ? closing > 0.45f : closing > 0.5f && (opposed || !Grounded || movingIntoActor) || sustained)) continue;
                obstacle = new ObstacleContact { Collider = collision.collider, Point = c.point,
                    Normal = c.normal, Speed = closing, Ceiling = ceiling,
                    Approach = Mathf.Abs(DesiredVelocity) > 0.1f ? Mathf.Sign(DesiredVelocity) : Facing };
                return true;
            }
            return false;
        }

        // Swept capsule along the ballistic arc. It includes the target's sides and
        // underside; only a descending top landing is permitted.
        public bool ArcClear(Vector3 feet, Vector3 velocity, float duration, Collider target, out RaycastHit obstruction)
        {
            obstruction = default;
            int steps = Mathf.Clamp(Mathf.CeilToInt(duration / 0.035f), 4, 120);
            float radius = capsule.radius * 0.94f;
            Vector3 previous = feet;
            for (int s = 1; s <= steps; s++)
            {
                float t = duration * s / steps;
                Vector3 next = feet + velocity * t + Physics.gravity * (0.5f * t * t);
                Vector3 delta = next - previous;
                if (delta.sqrMagnitude < 0.000001f) continue;
                int count = Physics.CapsuleCastNonAlloc(previous + Vector3.up * (capsule.radius + 0.035f),
                    previous + Vector3.up * (capsule.height - capsule.radius + 0.035f), radius,
                    delta.normalized, hits, delta.magnitude, groundMask, QueryTriggerInteraction.Ignore);
                if (count == hits.Length) return false;
                for (int i = 0; i < count; i++)
                {
                    RaycastHit hit = hits[i];
                    if (hit.rigidbody == body) continue;
                    if (hit.normal.y > 0.5f && delta.y < 0 && hit.collider == target) continue;
                    if (hit.normal.y > 0.5f && delta.y >= 0 && s <= 2) continue;
                    obstruction = hit; return false;
                }
                previous = next;
            }
            return true;
        }

        private void OnCollisionEnter(Collision collision) => ObserveContact(collision);
        private void OnCollisionStay(Collision collision) => ObserveContact(collision);
        private void ObserveContact(Collision collision)
        {
            if ((groundMask.value & (1 << collision.collider.gameObject.layer)) != 0)
                for (int i = 0; i < collision.contactCount; i++)
                {
                    var point = collision.GetContact(i);
                    if (point.normal.y < .45f || point.point.y > capsule.bounds.min.y + .3f) continue;
                    contactSupport = collision.collider;
                    contactSupportPoint = point.point;
                    supportContactAt = Time.fixedTime;
                    supportedBodyPosition = body.position;
                    supportedBodyRotation = body.rotation;
                    supportMatrix = contactSupport.transform.localToWorldMatrix;
                    break;
                }
            DraggableBody item = collision.collider.GetComponentInParent<DraggableBody>();
            if (item && item.PlayerInfluenced)
                NotifyExternal("player-moved object");
            Contact?.Invoke(collision);
        }
        private void OnCollisionExit(Collision collision)
        {
            if (contactSupport == collision.collider) contactSupport = null;
        }
        private void OnDestroy()
        {
            if (standingMaterial) Destroy(standingMaterial);
            if (fallenMaterial) Destroy(fallenMaterial);
        }

        // Technical recovery API only. Normal falling/learning never calls this method.
        public void Teleport(Vector3 center)
        {
            body.position = new Vector3(center.x, center.y, 0f);
            body.rotation = Quaternion.identity;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            SetUpright(true);
            groundIgnoreUntil = Time.time + 0.1f;
            Grounded = false;
            contactSupport = null;
        }
    }
}
