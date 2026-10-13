using UnityEngine;
using UnityEngine.InputSystem;

namespace NeverGiveUp
{
    public sealed class HandInteraction : MonoBehaviour
    {
        [SerializeField] private Camera view;
        [SerializeField] private PhysicsObjectInteraction dragging;
        [SerializeField, Min(0.1f)] private float flickForce = 5.5f;
        [SerializeField, Range(0f, 1f)] private float upwardBias = 0.4f;
        [SerializeField, Min(0f)] private float flickCooldown = 0.15f;
        private CharacterRecovery hoveredCharacter;
        private DraggableBody hoveredBox;
        private RaycastHit hoveredHit;
        private Vector2 mousePosition;
        private Vector3 worldPoint;
        private Vector3 flickDirection;
        private float nextFlick;
        private Texture2D hand;
        private bool focused = true;
        private bool cursorWasVisible;
        private CursorLockMode oldLock;
        private bool ownsCursor;
        public int FlickCount { get; private set; }

        public void Configure(Camera camera, PhysicsObjectInteraction objects)
        { view = camera; dragging = objects; }

        private void OnEnable()
        {
            hand = CreateHand();
            AcquireCursor();
        }
        private void AcquireCursor()
        {
            if (ownsCursor) return;
            cursorWasVisible = Cursor.visible;
            oldLock = Cursor.lockState;
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.None;
            ownsCursor = true;
        }
        private void RestoreCursor()
        {
            if (!ownsCursor) return;
            Cursor.visible = cursorWasVisible;
            Cursor.lockState = oldLock;
            ownsCursor = false;
        }
        private void OnDisable()
        {
            if (dragging) dragging.Release();
            RestoreCursor();
            if (hand) Destroy(hand);
        }
        private void OnApplicationFocus(bool value)
        {
            focused = value;
            if (value) AcquireCursor();
            else { RestoreCursor(); if (dragging) dragging.Release(); }
        }
        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (!focused || mouse == null || !view || !dragging) return;
            mousePosition = mouse.position.ReadValue();
            bool inside = mousePosition.x >= 0 && mousePosition.x < Screen.width &&
                mousePosition.y >= 0 && mousePosition.y < Screen.height;
            if (!inside) { dragging.Release(); RestoreCursor(); return; }
            AcquireCursor();
            Ray ray = view.ScreenPointToRay(mousePosition);
            Plane plane = new Plane(Vector3.forward, Vector3.zero);
            if (plane.Raycast(ray, out float distance)) worldPoint = ray.GetPoint(distance);
            hoveredCharacter = null; hoveredBox = null;
            if (Physics.Raycast(ray, out hoveredHit, 100f, ~0, QueryTriggerInteraction.Ignore))
            {
                hoveredCharacter = hoveredHit.collider.GetComponentInParent<CharacterRecovery>();
                hoveredBox = hoveredHit.collider.GetComponentInParent<DraggableBody>();
            }
            if (hoveredCharacter)
            {
                float dx = hoveredCharacter.transform.position.x - worldPoint.x;
                // Left half pushes right, right half pushes left. Arrow previews this before clicking.
                float direction = Mathf.Abs(dx) < 0.04f ? 1f : Mathf.Sign(dx);
                flickDirection = new Vector3(direction, upwardBias, 0f).normalized;
            }
            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (hoveredCharacter && Time.time >= nextFlick)
                {
                    hoveredCharacter.Flick(flickDirection * flickForce, worldPoint);
                    FlickCount++;
                    nextFlick = Time.time + flickCooldown;
                }
                else if (hoveredBox) dragging.Grab(hoveredBox, worldPoint);
            }
            if (dragging.IsDragging) dragging.SetTarget(worldPoint);
            if (!mouse.leftButton.isPressed) dragging.Release();
        }
        private void OnGUI()
        {
            if (!focused || !ownsCursor || Mouse.current == null || !hand) return;
            Vector2 p = new Vector2(mousePosition.x, Screen.height - mousePosition.y);
            Color previous = GUI.color;
            GUI.color = hoveredCharacter ? new Color(1f, 0.76f, 0.25f) :
                (hoveredBox || dragging.IsDragging ? Color.cyan : Color.white);
            GUI.DrawTexture(new Rect(p.x - 9f, p.y - 4f, 38f, 46f), hand);
            GUI.color = previous;
            if (hoveredCharacter)
            {
                Vector3 screen = view.WorldToScreenPoint(worldPoint + flickDirection * 0.9f);
                DrawLine(p, new Vector2(screen.x, Screen.height - screen.y), new Color(1f, 0.65f, 0.12f));
                GUI.Label(new Rect(p.x + 28, p.y - 28, 130, 28), flickDirection.x > 0 ? "FLICK  >" : "<  FLICK");
            }
            else if (hoveredBox || dragging.IsDragging)
                GUI.Label(new Rect(p.x + 30, p.y - 26, 130, 28), "HOLD TO DRAG");
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
