using UnityEngine;
using UnityEngine.InputSystem;

namespace NeverGiveUp
{
    public sealed class HandInteraction : MonoBehaviour
    {
        [SerializeField] private Camera view;
        [SerializeField] private PhysicsObjectInteraction dragging;
        [Header("Speed in viewport heights per second; impulse in N s")]
        [SerializeField, Min(0.01f)] private float minimumSlapSpeed = 0.15f;
        [SerializeField, Min(0.02f)] private float maximumSlapSpeed = 3f;
        [SerializeField, Min(0f)] private float minimumSlapImpulse = 0.25f;
        [SerializeField, Min(0f)] private float maximumSlapImpulse = 4.5f;
        [SerializeField] private AnimationCurve slapResponse = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [SerializeField, Min(0.01f)] private float slapRadius = 0.32f;
        [SerializeField, Range(.1f,.25f)] private float trajectorySeconds = .2f;
        [SerializeField, Range(0f,.06f)] private float predictionSeconds = .05f;
        [SerializeField, Range(0f,1f)] private float predictionBodyWidths = .75f;
        [SerializeField, HideInInspector] private int palmInputVersion;
        [SerializeField, Min(0f)] private float slapCooldown = 0.18f;
        [SerializeField] private bool showSlapDebug;
        private enum Gesture { None, Palm, Box }
        private Gesture gesture;
        private readonly HandDragSampler sampler = new HandDragSampler();
        private readonly Collider[] contacts = new Collider[32];
        private CharacterRecovery hoveredCharacter, lastTarget;
        private DraggableBody hoveredBox;
        private Vector2 mousePosition, direction;
        private Vector3 worldPoint;
        private float nextSlap, feedbackUntil, lastSpeed, lastImpulse;
        private string result = "ready";
        private bool hit;
        private Texture2D hand;
        private bool focused = true, cursorWasVisible, ownsCursor;
        private CursorLockMode oldLock;
        public int SlapCount { get; private set; }
        public int FlickCount => SlapCount; // Existing scene consumers remain compatible.
        public void Configure(Camera camera, PhysicsObjectInteraction objects)
        { view = camera; dragging = objects; }
        public void ApplyM3Defaults() { } // Old preserve-layout editor menu compatibility.
        private void OnEnable()
        {
            // Migrate only the prior default, preserving deliberate custom radii.
            if(palmInputVersion<1){if(Mathf.Approximately(slapRadius,.22f))slapRadius=.32f;palmInputVersion=1;}
            hand = CreateHand(); AcquireCursor();
        }
        private void AcquireCursor()
        {
            if (ownsCursor) return;
            cursorWasVisible = Cursor.visible; oldLock = Cursor.lockState;
            Cursor.visible = false; Cursor.lockState = CursorLockMode.None; ownsCursor = true;
        }
        private void RestoreCursor()
        {
            if (!ownsCursor) return;
            Cursor.visible = cursorWasVisible; Cursor.lockState = oldLock; ownsCursor = false;
        }
        private void Cancel()
        { gesture = Gesture.None; sampler.Clear(); if (dragging) dragging.Release(); }
        private void OnDisable() { Cancel(); RestoreCursor(); if (hand) Destroy(hand); }
        private void OnApplicationFocus(bool value)
        {
            focused = value;
            if (!value) { Cancel(); RestoreCursor(); }
            else AcquireCursor();
        }
        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (!focused || mouse == null || !view || !dragging || Time.timeScale <= 0f)
            { Cancel(); RestoreCursor(); return; }
            mousePosition = mouse.position.ReadValue();
            if (!view.pixelRect.Contains(mousePosition)) { Cancel(); RestoreCursor(); return; }
            AcquireCursor();
            Vector2 sample = (mousePosition - view.pixelRect.position) / view.pixelHeight;
            worldPoint = Project(sample);
            hoveredCharacter = null; hoveredBox = null;
            if (Physics.Raycast(view.ScreenPointToRay(mousePosition), out RaycastHit hover, 100f,
                ~0, QueryTriggerInteraction.Ignore))
            {
                hoveredCharacter = hover.collider.GetComponentInParent<CharacterRecovery>();
                hoveredBox = hover.collider.GetComponentInParent<DraggableBody>();
            }
            float now = Time.unscaledTime;
            sampler.HistorySeconds=trajectorySeconds;
            if (mouse.leftButton.wasPressedThisFrame)
            {
                Cancel();
                if (hoveredBox) { gesture = Gesture.Box; dragging.Grab(hoveredBox, worldPoint); }
                else { gesture = Gesture.Palm; sampler.Add(sample, now); }
            }
            if (gesture == Gesture.Box) dragging.SetTarget(worldPoint);
            if (gesture == Gesture.Palm)
            {
                sampler.Add(sample, now);
                if (sampler.Velocity(now, out Vector2 velocity)) lastSpeed = velocity.magnitude;
                else lastSpeed = 0f;
            }
            if (mouse.leftButton.wasReleasedThisFrame)
            {
                if (gesture == Gesture.Palm) ReleaseSlap(now);
                Cancel();
            }
            else if (!mouse.leftButton.isPressed) Cancel();
        }
        private Vector3 Project(Vector2 normalized)
        {
            Vector2 screen = view.pixelRect.position + normalized * view.pixelHeight;
            Ray ray = view.ScreenPointToRay(screen);
            Plane plane = new Plane(Vector3.forward, Vector3.zero);
            return plane.Raycast(ray, out float d) ? ray.GetPoint(d) : worldPoint;
        }
        private void ReleaseSlap(float now)
        {
            hit = false; lastImpulse = 0f; result = "no stroke";
            if (!sampler.Velocity(now, out Vector2 velocity) || velocity.magnitude < minimumSlapSpeed) return;
            if (Time.time < nextSlap) { result = "cooldown"; return; }
            lastSpeed = velocity.magnitude;
            Vector3 delta = Project(sampler.Latest + velocity.normalized * 0.01f) - Project(sampler.Latest);
            delta.z = 0f;
            if (delta.sqrMagnitude < 0.000001f) return;
            direction = delta.normalized;
            float t = Mathf.InverseLerp(minimumSlapSpeed, Mathf.Max(minimumSlapSpeed + 0.01f, maximumSlapSpeed), lastSpeed);
            float response = slapResponse == null ? t : Mathf.Clamp01(slapResponse.Evaluate(t));
            lastImpulse = Mathf.Lerp(minimumSlapImpulse, Mathf.Max(minimumSlapImpulse, maximumSlapImpulse), response);
            nextSlap = Time.time + slapCooldown; feedbackUntil = now + 0.18f;
            result = "miss";
            // Only the recent 200ms trajectory is swept, once at release.
            // Reproject all samples through the SAME camera pose: follow motion cannot generate a slap.
            for (int i = sampler.Count - 1; i > 0; i--)
            {
                Vector3 a = Project(sampler.Point(i - 1)), b = Project(sampler.Point(i));
                if(TrySlapSegment(a,b,false))return;
            }
            // A short release grace, not continued cursor movement or extra force.
            Vector3 release=Project(sampler.Latest);
            if(sampler.ContinuesAlong(velocity))
                TrySlapSegment(release,Project(sampler.Latest+velocity*predictionSeconds),true);
        }
        private bool TrySlapSegment(Vector3 a,Vector3 b,bool predicted)
        {
                int count = Physics.OverlapCapsuleNonAlloc(a, b, slapRadius, contacts, ~0, QueryTriggerInteraction.Ignore);
                for (int j = 0; j < count; j++)
                {
                    CharacterRecovery target = contacts[j].GetComponentInParent<CharacterRecovery>();
                    if (!target) continue;
                    Vector3 end=b;
                    if(predicted)
                    {
                        var body=target.GetComponent<CapsuleCollider>();
                        if(!body)continue;
                        float reach=body.radius*2*Mathf.Abs(body.transform.lossyScale.x)*predictionBodyWidths;
                        end=a+Vector3.ClampMagnitude(b-a,reach);
                        Vector3 segment=end-a;
                        float t=segment.sqrMagnitude>.000001f?Mathf.Clamp01(Vector3.Dot(contacts[j].bounds.center-a,segment)/segment.sqrMagnitude):0;
                        Vector3 nearest=a+segment*t;
                        if(Vector3.Distance(contacts[j].ClosestPoint(nearest),nearest)>slapRadius)continue;
                    }
                    Vector3 closest = contacts[j].ClosestPoint(end);
                    target.PalmSlap((Vector3)direction * lastImpulse, closest);
                    lastTarget = target; SlapCount++; hit = true; result = "hit";
                    return true; // Exactly one impulse per release, including forward grace.
                }
                return false;
        }
        private void OnGUI()
        {
            if (!focused || !ownsCursor || !hand) return;
            Vector2 point = new Vector2(mousePosition.x, Screen.height - mousePosition.y);
            float feedback = Mathf.Clamp01((feedbackUntil - Time.unscaledTime) / 0.18f);
            Vector2 recoil = new Vector2(-direction.x, direction.y) * feedback * (hit ? 9f + lastImpulse * 2f : 5f);
            Color old = GUI.color;
            GUI.color = hit && feedback > 0f ? new Color(1f, 0.65f, 0.2f) :
                hoveredBox || gesture == Gesture.Box ? Color.cyan : hoveredCharacter ? Color.yellow : Color.white;
            GUI.DrawTexture(new Rect(point.x - 18 + recoil.x, point.y - 23 + recoil.y, 38, 46), hand);
            GUI.color = old;
            if (gesture == Gesture.Palm && sampler.Velocity(Time.unscaledTime, out Vector2 v))
                DrawLine(point, point + new Vector2(v.x, -v.y).normalized * Mathf.Min(55, v.magnitude * 30), Color.yellow);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (showSlapDebug)
                GUI.Box(new Rect(12, 108, 520, 100), $"Palm: {gesture}, hits {SlapCount}, speed {lastSpeed:F2}, direction {direction}, impulse {lastImpulse:F2}: {result}\n" +
                    (lastTarget ? lastTarget.SlapDebug : "No target hit"));
#endif
        }
        private static void DrawLine(Vector2 start, Vector2 end, Color color)
        {
            Matrix4x4 matrix = GUI.matrix;
            Color old = GUI.color;
            GUI.color = color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(end.y - start.y, end.x - start.x) * Mathf.Rad2Deg, start);
            GUI.DrawTexture(new Rect(start.x, start.y, Vector2.Distance(start, end), 3), Texture2D.whiteTexture);
            GUI.matrix = matrix; GUI.color = old;
        }
        private static Texture2D CreateHand()
        {
            // Small code-drawn open hand, no imported asset or system emoji dependency.
            Texture2D texture = new Texture2D(24, 30, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            for (int y = 0; y < 30; y++)
            for (int x = 0; x < 24; x++)
            {
                bool palm = x >= 6 && x <= 19 && y >= 5 && y <= 17;
                bool wrist = x >= 8 && x <= 17 && y <= 7;
                bool fingers = (x >= 6 && x <= 8 && y >= 16 && y <= 26) ||
                    (x >= 10 && x <= 12 && y >= 16 && y <= 29) ||
                    (x >= 14 && x <= 16 && y >= 16 && y <= 27) ||
                    (x >= 18 && x <= 20 && y >= 15 && y <= 23);
                bool thumb = x >= 2 && x <= 7 && y >= 8 && y <= 15 && x + y >= 13;
                texture.SetPixel(x, y, palm || wrist || fingers || thumb ? Color.white : Color.clear);
            }
            texture.Apply();
            return texture;
        }
    }
}
