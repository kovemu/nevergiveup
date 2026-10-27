using UnityEngine;

namespace NeverGiveUp
{
    public sealed class StageController : MonoBehaviour
    {
        [SerializeField] private CharacterBrain character;
        [SerializeField] private GameManager game;
        [SerializeField] private Transform safeOrigin; // Legacy authoring reference, never a respawn destination.
        [SerializeField] private StageWaypoint[] waypoints;
        [SerializeField] private float fallThreshold = -5f; // Retained for scene serialization compatibility.
        private CharacterMotor motor;
        private float refreshAt;
        public PlatformGraph Graph { get; private set; }
        public PlatformGraph.Platform GoalPlatform => Graph.At(GoalPosition);
        public Vector3 GoalPosition => waypoints != null && waypoints.Length > 0
            ? waypoints[waypoints.Length - 1].Position : transform.position;
        public int Index { get; private set; }
        public bool Completed { get; private set; }
        public bool IsRespawning => false;
        public float FallThreshold => fallThreshold;
        public StageWaypoint Current => waypoints != null && Index < waypoints.Length ? waypoints[Index] : null;
        public StageWaypoint Next => waypoints != null && Index + 1 < waypoints.Length ? waypoints[Index + 1] : null;
        public void Configure(CharacterBrain actor, GameManager manager, Transform origin,
            StageWaypoint[] route, float lowerLimit)
        { character = actor; game = manager; safeOrigin = origin; waypoints = route; fallThreshold = lowerLimit; }
        private void Start()
        {
            if (!character || waypoints == null || waypoints.Length == 0 || !game)
            { Debug.LogError("Stage requires a character, manager and goal waypoint.", this); enabled = false; return; }
            motor = character.GetComponent<CharacterMotor>();
            EnsureM3Geometry();
            Graph = GetComponent<PlatformGraph>();
            if (!Graph) Graph = gameObject.AddComponent<PlatformGraph>();
            Graph.Initialize(transform.Find("Terrain"), motor);
            character.Initialize(this);
        }
        private void FixedUpdate()
        {
            if (!motor || !Graph) return;
            if (Time.time >= refreshAt) { Graph.Rebuild(); refreshAt = Time.time + 0.6f; }
            if (Completed || !motor.Grounded || !character.SafeForGoal) return;
            var support = Graph.Support(motor.Support);
            var goal = GoalPlatform;
            if (support == null || goal == null || support.Id != goal.Id) return;
            if (Mathf.Abs(motor.Feet.x - GoalPosition.x) > 0.4f || Mathf.Abs(motor.Feet.y - GoalPosition.y) > 0.5f) return;
            Completed = true;
            Index = waypoints.Length;
            game.CompleteStage();
        }
        // Old API is intentionally non-teleporting. Existing callers request a local replan only.
        public void RequestRespawn() { if (character) character.RestartRoute(); }
        public void Advance() { if (character) character.RestartRoute(); }

        public void EnsureM3Geometry()
        {
            // Authored maps own their geometry; retain migration only for older scenes.
            if (GetComponent<LevelMap>()) return;
            Transform terrain = transform.Find("Terrain");
            if (!terrain) { Debug.LogError("Stage/Terrain is missing.", this); return; }
            if (terrain.Find("NGU Safety Floor")) return;
            Collider[] colliders = terrain.GetComponentsInChildren<Collider>();
            bool any = false; Bounds bounds = default; float lowestTop = float.PositiveInfinity;
            Material material = null;
            foreach (Collider col in colliders)
            {
                if (col.isTrigger || col.attachedRigidbody) continue;
                if (!any) { bounds = col.bounds; any = true; } else bounds.Encapsulate(col.bounds);
                lowestTop = Mathf.Min(lowestTop, col.bounds.max.y);
                Renderer renderer = col.GetComponent<Renderer>();
                if (renderer && !material) material = renderer.sharedMaterial;
            }
            if (!any) return;
            float floorTop = lowestTop - 2.6f;
            float left = bounds.min.x - 8f, right = bounds.max.x + 8f;
            Solid("NGU Safety Floor", terrain, new Vector3((left + right) * 0.5f, floorTop - 0.4f, 0f),
                new Vector3(right - left, 0.8f, 3f), material);
            float wallHeight = bounds.max.y - floorTop + 15f;
            Solid("NGU Boundary Left", terrain, new Vector3(left - 0.4f, floorTop + wallHeight * 0.5f, 0f),
                new Vector3(0.8f, wallHeight, 3f), material);
            Solid("NGU Boundary Right", terrain, new Vector3(right + 0.4f, floorTop + wallHeight * 0.5f, 0f),
                new Vector3(0.8f, wallHeight, 3f), material);
        }
        private static void Solid(string name, Transform parent, Vector3 position, Vector3 size, Material material)
        {
            GameObject item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            item.name = name; item.transform.SetParent(parent, false);
            item.transform.position = position; item.transform.localScale = size;
            if (material) item.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
