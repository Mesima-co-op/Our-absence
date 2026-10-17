using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Animation Test Ground controls (on the camera). Every ClipPreviewer in the scene follows this clock.
///   Space pause / play          , .  step one frame back / forward (while paused)
///   - =  slower / faster         R    restart all in sync
///   Tab / Shift+Tab  focus the next / previous clip     O  overview     M  left hand (mirrored) on / off
///   Fly: hold right mouse to look, WASD move, Q/E down/up, Shift fast. F1 hides the help.
/// The focused clip's timeline shows its hit window (red) and chain point (yellow).
/// </summary>
public class AnimationTestGround : MonoBehaviour
{
    public static AnimationTestGround Instance { get; private set; }

    public float moveSpeed = 4f;
    public float lookSpeed = 0.15f;

    private static readonly float[] Speeds = { 0.05f, 0.1f, 0.25f, 0.5f, 1f, 1.5f, 2f };
    private int speedIndex = 4;
    private bool paused;
    private bool showHelp = true;
    private ClipPreviewer[] previewers = new ClipPreviewer[0];
    private int focus = -1;
    private float yaw, pitch;

    private const float FrameTime = 1f / 60f;

    private void Awake()
    {
        Instance = this;
        // Rows first (front to back), then left to right
        previewers = FindObjectsOfType<ClipPreviewer>()
            .OrderBy(p => Mathf.Round(p.transform.position.z))
            .ThenBy(p => p.transform.position.x)
            .ToArray();
        var e = transform.eulerAngles;
        yaw = e.y;
        pitch = e.x > 180f ? e.x - 360f : e.x;
        overviewPosition = transform.position;
        overviewRotation = transform.rotation;
    }

    private Vector3 overviewPosition;
    private Quaternion overviewRotation;

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.spaceKey.wasPressedThisFrame) paused = !paused;
            if (kb.rKey.wasPressedThisFrame) foreach (var p in previewers) p.Restart();
            if (kb.minusKey.wasPressedThisFrame) speedIndex = Mathf.Max(0, speedIndex - 1);
            if (kb.equalsKey.wasPressedThisFrame) speedIndex = Mathf.Min(Speeds.Length - 1, speedIndex + 1);
            if (kb.mKey.wasPressedThisFrame) foreach (var p in previewers) p.leftHand = !p.leftHand;
            if (kb.f1Key.wasPressedThisFrame) showHelp = !showHelp;
            if (kb.oKey.wasPressedThisFrame)
            {
                focus = -1;
                transform.SetPositionAndRotation(overviewPosition, overviewRotation);
                var e = overviewRotation.eulerAngles;
                yaw = e.y;
                pitch = e.x > 180f ? e.x - 360f : e.x;
            }
            if (kb.tabKey.wasPressedThisFrame && previewers.Length > 0)
            {
                bool back = kb.shiftKey.isPressed;
                focus = (focus + (back ? -1 : 1) + previewers.Length) % previewers.Length;
                FrameCamera(previewers[focus]);
            }
        }

        float step = 0f;
        if (!paused) step = Time.deltaTime * Speeds[speedIndex];
        else if (kb != null && kb.periodKey.wasPressedThisFrame) step = FrameTime;
        else if (kb != null && kb.commaKey.wasPressedThisFrame) step = -FrameTime;
        if (step > 0f) foreach (var p in previewers) p.Advance(step);
        else if (step < 0f) foreach (var p in previewers) p.StepBack(-step);

        Fly(kb);
    }

    /// <summary>Puts the camera in front-right of a clip, looking at the body.</summary>
    private void FrameCamera(ClipPreviewer p)
    {
        var t = p.transform;
        Vector3 target = t.position + Vector3.up * 1.0f + t.forward * 0.5f;
        transform.position = target + t.forward * 2.6f + t.right * 2.2f + Vector3.up * 0.6f;
        transform.LookAt(target);
        var e = transform.eulerAngles;
        yaw = e.y;
        pitch = e.x > 180f ? e.x - 360f : e.x;
    }

    private void Fly(Keyboard kb)
    {
        var mouse = Mouse.current;
        if (mouse != null && mouse.rightButton.isPressed)
        {
            Vector2 d = mouse.delta.ReadValue() * lookSpeed;
            yaw += d.x;
            pitch = Mathf.Clamp(pitch - d.y, -89f, 89f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
        if (kb == null) return;
        Vector3 move = Vector3.zero;
        if (kb.wKey.isPressed) move += Vector3.forward;
        if (kb.sKey.isPressed) move += Vector3.back;
        if (kb.aKey.isPressed) move += Vector3.left;
        if (kb.dKey.isPressed) move += Vector3.right;
        if (kb.eKey.isPressed) move += Vector3.up;
        if (kb.qKey.isPressed) move += Vector3.down;
        float speed = moveSpeed * (kb.shiftKey.isPressed ? 3f : 1f);
        transform.position += transform.TransformDirection(move) * speed * Time.unscaledDeltaTime;
    }

    private Texture2D white;

    private void OnGUI()
    {
        if (white == null) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }
        var box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, richText = true };

        string state = $"<b>ANIMATION TEST GROUND</b>   {(paused ? "<color=#ffcc44>PAUSED</color>" : "playing")}   speed x{Speeds[speedIndex]}   " +
                       $"{(previewers.Length > 0 && previewers[0].leftHand ? "LEFT hand (mirrored)" : "right hand")}";
        string help = showHelp
            ? "\nSpace pause · , . frame step · - = speed · R restart · Tab / Shift+Tab focus · O overview · M left hand · F1 help\n" +
              "Fly: hold RMB to look, WASD, Q/E, Shift fast\n" +
              "Green bar = facing, red bar = reach. Label turns red in the hit window.\n" +
              "Tune live in Data/Combat/Moveset_*.asset (speed, hit window, lunge, mirror, blade in fist, reach)."
            : "";
        GUI.Box(new Rect(10, 10, 640, showHelp ? 96 : 26), state + help, box);

        if (focus < 0 || focus >= previewers.Length) return;
        var p = previewers[focus];
        var s = p.Step;
        float w = Screen.width - 40f, x = 20f, y = Screen.height - 70f;
        string info = $"<b>{p.Title.Replace("\n", " · ")}</b>   t={p.Normalized:0.000}";
        if (s != null && !p.recovery)
            info += $"   speed {s.speed:0.##}  damage x{s.damage:0.##}  hit {s.hitStart:0.00}-{s.hitEnd:0.00}  chain {s.chainFrom:0.00}  lunge {s.lunge:0.##} m  mirror {s.mirror}";
        GUI.Box(new Rect(x - 10, y - 28, w + 20, 62), info, box);

        // Timeline: hit window, chain point, playhead
        GUI.color = new Color(1f, 1f, 1f, 0.25f);
        GUI.DrawTexture(new Rect(x, y, w, 18), white);
        if (s != null && !p.recovery)
        {
            GUI.color = new Color(1f, 0.2f, 0.2f, 0.7f);
            GUI.DrawTexture(new Rect(x + w * s.hitStart, y, w * (s.hitEnd - s.hitStart), 18), white);
            GUI.color = new Color(1f, 0.85f, 0.2f, 1f);
            GUI.DrawTexture(new Rect(x + w * s.chainFrom - 1, y - 3, 3, 24), white);
        }
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(x + w * p.Normalized - 1, y - 4, 3, 26), white);
    }
}
