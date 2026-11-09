using System.Collections.Generic;
using Rubik.Core;
using UnityEngine;

/// <summary>
/// 3D view of an NxN cube (N up to <see cref="Cube.MaxSize"/>). The source of truth is <see cref="Model"/>
/// (Rubik.Core.Cube): a move is animated by rotating the turning layers, and when it finishes the model
/// applies the move and the stickers are repainted from the model.
///
/// Rendering does not depend on N: the cube is drawn as at most three boxes (the turning layers and the
/// still parts on either side), 24 vertices each, with the Rubik/CubeFace shader. Sticker colors live in
/// one 6N x N texture, one texel per sticker, and the shader draws the rounded stickers and the gaps.
///
/// Coordinates: the model is right-handed (x right, y up, z towards the viewer, F = +z) and uses doubled
/// integer coordinates (the cube spans -N..N); Unity is left-handed, so z is flipped. A model move about
/// X/Y/Z is +90° about the Unity vectors right / up / back. The cube is scaled to 3 units whatever N is.
/// </summary>
public class CubeView : MonoBehaviour
{
    // Colors by face index of the solved cube: U R F D L B
    public static readonly Color[] Palette =
    {
        new Color(0.95f, 0.95f, 0.95f), // U white
        new Color(0.80f, 0.08f, 0.10f), // R red
        new Color(0.05f, 0.62f, 0.25f), // F green
        new Color(1.00f, 0.84f, 0.00f), // D yellow
        new Color(1.00f, 0.45f, 0.00f), // L orange
        new Color(0.05f, 0.30f, 0.80f), // B blue
    };

    /// <summary>Color of a sticker that is not filled in yet (edit mode).</summary>
    public static readonly Color EmptyColor = new Color(0.32f, 0.33f, 0.36f);

    public static Color ColorOf(byte color) => color < Palette.Length ? Palette[color] : EmptyColor;

    private static readonly Vector3[] AxisVectors = { Vector3.right, Vector3.up, Vector3.back };
    private static readonly Color32[] Palette32 = BuildPalette32();

    /// <summary>Edge length of the cube in Unity units, the same for every N.</summary>
    private const float WorldSize = 3f;

    [Range(1, Cube.MaxSize)] public int size = 3;
    [Tooltip("Seconds per quarter turn")]
    [Range(0.03f, 1f)] public float secondsPerTurn = 0.25f;
    public Color bodyColor = new Color(0.06f, 0.06f, 0.07f);

    private readonly Queue<Move> _queue = new Queue<Move>();
    private byte[] _override; // when not null, these stickers are shown instead of the model (edit mode)
    private Texture2D _colors;
    private Color32[] _pixels;
    private Material _faceMaterial, _bodyMaterial;
    private Transform _pivot;
    private Part _still1, _still2, _turning;

    private bool _animating;
    private Move _current;
    private float _elapsed, _duration, _targetAngle;

    public Cube Model { get; private set; }

    /// <summary>An animation is running or moves are queued.</summary>
    public bool IsBusy => _animating || _queue.Count > 0;

    /// <summary>Number of unfinished moves (including the current one).</summary>
    public int Pending => _queue.Count + (_animating ? 1 : 0);

    /// <summary>Raised after every repaint (move finished, new state, edit mode).</summary>
    public event System.Action Repainted;

    /// <summary>One drawn box: a mesh renderer whose mesh is rebuilt for each move.</summary>
    private sealed class Part
    {
        public readonly GameObject Object;
        public readonly Mesh Mesh;

        public Part(string name, Transform parent, Material face, Material body)
        {
            Object = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            Object.transform.SetParent(parent, false);
            Mesh = new Mesh { name = name };
            Object.GetComponent<MeshFilter>().sharedMesh = Mesh;
            Object.GetComponent<MeshRenderer>().sharedMaterials = new[] { face, body };
        }
    }

    private void Awake()
    {
        var shader = Resources.Load<Shader>("RubikCubeFace");
        _faceMaterial = new Material(shader) { name = "CubeFace" };
        _bodyMaterial = new Material(shader) { name = "CubeBody" };
        _bodyMaterial.SetFloat("_Body", 1f);
        _faceMaterial.SetColor("_BodyColor", bodyColor);
        _bodyMaterial.SetColor("_BodyColor", bodyColor);

        _pivot = new GameObject("Pivot").transform;
        _pivot.SetParent(transform, false);
        _still1 = new Part("Still", transform, _faceMaterial, _bodyMaterial);
        _still2 = new Part("Still 2", transform, _faceMaterial, _bodyMaterial);
        _turning = new Part("Turning", _pivot, _faceMaterial, _bodyMaterial);

        Build(size);
    }

    private void OnDestroy()
    {
        if (_colors != null) Destroy(_colors);
        Destroy(_faceMaterial);
        Destroy(_bodyMaterial);
    }

    // ---------------------------------------------------------------- public API

    public void Enqueue(Move move)
    {
        if (!move.IsIdentity) _queue.Enqueue(move);
    }

    public void Enqueue(IEnumerable<Move> moves)
    {
        foreach (var m in moves) Enqueue(m);
    }

    /// <summary>Stops the animation and shows the given state (copied); the size follows the cube.</summary>
    public void SetState(Cube cube)
    {
        if (cube.Size != size) Build(cube.Size);
        StopAnimation();
        _queue.Clear();
        Model = cube.Clone();
        _override = null;
        Repaint();
    }

    /// <summary>Shows the given stickers (values outside 0..5 = empty) instead of the model. The array is not copied.</summary>
    public void ShowStickers(byte[] stickers)
    {
        _override = stickers;
        Repaint();
    }

    /// <summary>Goes back to showing the model state.</summary>
    public void ShowModel()
    {
        _override = null;
        Repaint();
    }

    /// <summary>Color of the sticker as currently shown (model or edit mode).</summary>
    public byte DisplayedColor(int index) => _override != null ? _override[index] : Model.Stickers[index];

    public void ResetCube() => SetState(new Cube(size));

    /// <summary>Instantly completes the current animation and the whole queue.</summary>
    public void FinishAll()
    {
        if (_animating) CompleteCurrent();
        while (_queue.Count > 0) Model.Apply(_queue.Dequeue());
        Repaint();
    }

    /// <summary>
    /// The sticker hit by a world-space ray, using the cube at rest (good enough while a layer turns).
    /// Index as in <see cref="Cube.Stickers"/>.
    /// </summary>
    public bool TryPickSticker(Ray ray, out int index)
    {
        index = -1;
        var origin = transform.InverseTransformPoint(ray.origin);
        var direction = transform.worldToLocalMatrix.MultiplyVector(ray.direction);
        float half = size * 0.5f; // cube in local units spans -N/2..N/2

        // slab method: the entry point is where the last of the three slabs is entered
        float tEnter = float.NegativeInfinity, tExit = float.PositiveInfinity;
        int enterAxis = -1;
        for (int a = 0; a < 3; a++)
        {
            if (Mathf.Abs(direction[a]) < 1e-9f)
            {
                if (origin[a] < -half || origin[a] > half) return false;
                continue;
            }
            float t1 = (-half - origin[a]) / direction[a];
            float t2 = (half - origin[a]) / direction[a];
            if (t1 > t2) (t1, t2) = (t2, t1);
            if (t1 > tEnter) { tEnter = t1; enterAxis = a; }
            tExit = Mathf.Min(tExit, t2);
        }
        if (enterAxis < 0 || tEnter > tExit || tExit < 0f) return false;

        var hit = origin + direction * tEnter;
        // back to doubled model coordinates (z flipped), with the hit coordinate snapped to the surface
        var p = new Vector3(hit.x * 2f, hit.y * 2f, -hit.z * 2f);
        int modelAxis = enterAxis; // x and y are the same axis in both spaces, z is z (only its sign flips)
        float sign = Mathf.Sign(p[modelAxis]);
        p[modelAxis] = sign * size;
        index = StickerAt(size, p);
        return index >= 0;
    }

    /// <summary>
    /// Converts a drag that started on a sticker into a layer move: the drag picks one of the two
    /// directions lying in the sticker's face, and the layer turns so the sticker follows the pointer.
    /// Uses the rest pose of the cube, so it also works while an animation is running.
    /// </summary>
    public bool TryGetDragMove(int stickerIndex, Vector2 screenDelta, Camera cam, out Move move)
    {
        move = default;
        int n = size;
        if (stickerIndex < 0 || stickerIndex >= 6 * n * n || screenDelta.sqrMagnitude < 1e-4f) return false;

        int face = stickerIndex / (n * n), rest = stickerIndex % (n * n);
        StickerPoint(n, face, rest / n, rest % n, out var point, out var modelNormal);
        var home = point - modelNormal; // doubled model coordinates of the cubie centre
        var normal = ToUnityDirection(modelNormal);
        var center = ToUnity(home) + normal * 0.5f;
        Vector2 screenCenter = cam.WorldToScreenPoint(transform.TransformPoint(center));
        var drag = screenDelta.normalized;

        float best = 0f;
        var direction = Vector3.zero;
        foreach (var axis in AxisVectors)
        {
            if (Mathf.Abs(Vector3.Dot(axis, normal)) > 0.5f) continue; // not in the face plane
            Vector2 onScreen = (Vector2)cam.WorldToScreenPoint(transform.TransformPoint(center + axis * 0.5f)) - screenCenter;
            if (onScreen.sqrMagnitude < 1e-4f) continue;
            float d = Vector2.Dot(drag, onScreen.normalized);
            if (Mathf.Abs(d) > Mathf.Abs(best))
            {
                best = d;
                direction = axis * Mathf.Sign(d);
            }
        }
        if (best == 0f) return false;

        // The layer turns about the axis perpendicular to both the face normal and the drag
        var turnAxis = Vector3.Cross(normal, direction);
        int axisIndex = 0;
        for (int i = 1; i < 3; i++)
            if (Mathf.Abs(turnAxis[i]) > Mathf.Abs(turnAxis[axisIndex])) axisIndex = i;

        int layer = (n - 1 - home[axisIndex]) / 2;
        var moved = Quaternion.AngleAxis(90f, AxisVectors[axisIndex]) * center;
        int turns = Vector3.Dot(moved - center, direction) > 0f ? 1 : 3;
        move = new Move((Axis)axisIndex, layer, turns);
        return true;
    }

    // ---------------------------------------------------------------- building

    private void Build(int n)
    {
        size = n;
        _queue.Clear();
        _override = null;
        _animating = false;
        _pivot.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one * (WorldSize / n);

        if (_colors != null) Destroy(_colors);
        _colors = new Texture2D(6 * n, n, TextureFormat.RGBA32, mipChain: false)
        {
            name = "StickerColors",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        _pixels = new Color32[6 * n * n];
        _faceMaterial.mainTexture = _colors;

        Model = new Cube(n);
        ShowWholeCube();
        Repaint();
    }

    private static Color32[] BuildPalette32()
    {
        var result = new Color32[Palette.Length + 1];
        for (int i = 0; i < Palette.Length; i++) result[i] = Palette[i];
        result[Palette.Length] = EmptyColor;
        return result;
    }

    /// <summary>Same as Cube.ToPoint: sticker -> doubled point on the surface + face normal (model space).</summary>
    private static void StickerPoint(int n, int face, int row, int col, out Vector3Int point, out Vector3Int normal)
    {
        int m = n - 1;
        int a = 2 * col - m, b = 2 * row - m;
        switch (face)
        {
            case 0: point = new Vector3Int(a, n, b); normal = Vector3Int.up; break;          // U
            case 1: point = new Vector3Int(n, -b, -a); normal = Vector3Int.right; break;     // R
            case 2: point = new Vector3Int(a, -b, n); normal = new Vector3Int(0, 0, 1); break; // F
            case 3: point = new Vector3Int(a, -n, -b); normal = Vector3Int.down; break;      // D
            case 4: point = new Vector3Int(-n, -b, a); normal = Vector3Int.left; break;      // L
            default: point = new Vector3Int(-a, -b, -n); normal = new Vector3Int(0, 0, -1); break; // B
        }
    }

    /// <summary>
    /// Inverse of <see cref="StickerPoint"/> on a continuous scale: a point on face <paramref name="face"/>
    /// (doubled model coordinates) -> (column, row) in stickers, 0..N across the face.
    /// </summary>
    private static Vector2 FaceCoords(int n, int face, Vector3 p)
    {
        switch (face)
        {
            case 0: return new Vector2((p.x + n) / 2f, (p.z + n) / 2f);  // U
            case 1: return new Vector2((n - p.z) / 2f, (n - p.y) / 2f);  // R
            case 2: return new Vector2((p.x + n) / 2f, (n - p.y) / 2f);  // F
            case 3: return new Vector2((p.x + n) / 2f, (n - p.z) / 2f);  // D
            case 4: return new Vector2((p.z + n) / 2f, (n - p.y) / 2f);  // L
            default: return new Vector2((n - p.x) / 2f, (n - p.y) / 2f); // B
        }
    }

    /// <summary>Face index (U R F D L B) of the outward direction along a model axis.</summary>
    private static int FaceOf(int axis, int sign)
    {
        switch (axis)
        {
            case 0: return sign > 0 ? 1 : 4; // R / L
            case 1: return sign > 0 ? 0 : 3; // U / D
            default: return sign > 0 ? 2 : 5; // F / B
        }
    }

    /// <summary>Sticker index at a point on the surface (doubled model coordinates), or -1.</summary>
    private static int StickerAt(int n, Vector3 p)
    {
        for (int axis = 0; axis < 3; axis++)
        {
            if (Mathf.Abs(Mathf.Abs(p[axis]) - n) > 1e-3f) continue;
            int face = FaceOf(axis, p[axis] > 0 ? 1 : -1);
            var c = FaceCoords(n, face, p);
            int col = Mathf.Clamp(Mathf.FloorToInt(c.x), 0, n - 1);
            int row = Mathf.Clamp(Mathf.FloorToInt(c.y), 0, n - 1);
            return face * n * n + row * n + col;
        }
        return -1;
    }

    private static Vector3 ToUnity(Vector3Int doubled) => new Vector3(doubled.x, doubled.y, -doubled.z) * 0.5f;
    private static Vector3 ToUnity(Vector3 doubled) => new Vector3(doubled.x, doubled.y, -doubled.z) * 0.5f;
    private static Vector3 ToUnityDirection(Vector3Int d) => new Vector3(d.x, d.y, -d.z);

    /// <summary>
    /// Builds a box spanning [min, max] (doubled model coordinates) into <paramref name="mesh"/>.
    /// Faces on the cube surface go to submesh 0 with UVs in stickers of the color texture;
    /// inner faces (cuts between turning and still layers) go to submesh 1 and are drawn as the body.
    /// </summary>
    private static void BuildBox(Mesh mesh, int n, Vector3Int min, Vector3Int max)
    {
        var vertices = new List<Vector3>(24);
        var normals = new List<Vector3>(24);
        var uvs = new List<Vector2>(24);
        var surface = new List<int>(36);
        var inner = new List<int>(36);

        for (int axis = 0; axis < 3; axis++)
        for (int sign = -1; sign <= 1; sign += 2)
        {
            int planeCoord = sign > 0 ? max[axis] : min[axis];
            bool onSurface = planeCoord == sign * n;
            int face = FaceOf(axis, sign);
            int b = (axis + 1) % 3, c = (axis + 2) % 3;

            var modelNormal = Vector3Int.zero;
            modelNormal[axis] = sign;
            var normal = ToUnityDirection(modelNormal);

            int start = vertices.Count;
            var corners = new Vector3[4];
            for (int k = 0; k < 4; k++)
            {
                var p = Vector3.zero;
                p[axis] = planeCoord;
                p[b] = k == 0 || k == 3 ? min[b] : max[b];
                p[c] = k < 2 ? min[c] : max[c];
                corners[k] = ToUnity(p);
                vertices.Add(corners[k]);
                normals.Add(normal);
                if (onSurface)
                {
                    var fc = FaceCoords(n, face, p);
                    uvs.Add(new Vector2(face * n + fc.x, fc.y));
                }
                else uvs.Add(Vector2.zero);
            }

            // Unity draws triangles that are clockwise seen from the front, i.e. whose
            // cross product points towards the viewer (outwards here)
            bool flip = Vector3.Dot(Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]), normal) < 0f;
            var triangles = onSurface ? surface : inner;
            if (!flip)
                triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            else
                triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
        }

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(surface, 0);
        mesh.SetTriangles(inner, 1);
        mesh.RecalculateBounds();
    }

    private void ShowWholeCube()
    {
        int n = size;
        BuildBox(_still1.Mesh, n, new Vector3Int(-n, -n, -n), new Vector3Int(n, n, n));
        _still1.Object.SetActive(true);
        _still2.Object.SetActive(false);
        _turning.Object.SetActive(false);
    }

    // ---------------------------------------------------------------- painting

    private void Repaint()
    {
        int count = 6 * size * size, n2 = size * size;
        for (int i = 0; i < count; i++)
        {
            byte color = DisplayedColor(i);
            // texel (face*N + col, row) holds sticker face*N*N + row*N + col
            int face = i / n2, rest = i % n2;
            _pixels[(rest / size) * 6 * size + face * size + rest % size] = Palette32[Mathf.Min(color, (byte)Palette.Length)];
        }
        _colors.SetPixels32(_pixels);
        _colors.Apply(false);
        Repainted?.Invoke();
    }

    // ---------------------------------------------------------------- animation

    private void Update()
    {
        if (!_animating)
        {
            if (_queue.Count == 0) return;
            StartMove(_queue.Dequeue());
        }

        _elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_elapsed / _duration);
        t = t * t * (3f - 2f * t); // smoothstep
        _pivot.localRotation = Quaternion.AngleAxis(_targetAngle * t, AxisVectors[(int)_current.Axis]);

        if (_elapsed >= _duration) CompleteCurrent();
    }

    private void StartMove(Move move)
    {
        _current = move;
        _animating = true;
        _elapsed = 0f;

        // three quarter turns are shown as one quarter turn the other way
        int turns = move.Turns == 3 ? -1 : move.Turns;
        _targetAngle = 90f * turns;
        _duration = secondsPerTurn * (Mathf.Abs(turns) == 2 ? 1.5f : 1f);

        // Layer l occupies [n - 2(l+1), n - 2l] along the axis (layer 0 is next to the positive face).
        // The turning layers form one box; the still layers on either side form up to two more.
        int n = size, axis = (int)move.Axis;
        int hi = n - 2 * move.LayerFrom, lo = n - 2 * (move.LayerTo + 1);
        var min = new Vector3Int(-n, -n, -n);
        var max = new Vector3Int(n, n, n);

        var turnMin = min; turnMin[axis] = lo;
        var turnMax = max; turnMax[axis] = hi;
        BuildBox(_turning.Mesh, n, turnMin, turnMax);
        _turning.Object.SetActive(true);

        bool above = hi < n, below = lo > -n;
        if (above)
        {
            var aMin = min; aMin[axis] = hi;
            BuildBox(_still1.Mesh, n, aMin, max);
        }
        if (below)
        {
            var bMax = max; bMax[axis] = lo;
            BuildBox((above ? _still2 : _still1).Mesh, n, min, bMax);
        }
        _still1.Object.SetActive(above || below);
        _still2.Object.SetActive(above && below);
        _pivot.localRotation = Quaternion.identity;
    }

    private void CompleteCurrent()
    {
        StopAnimation();
        Model.Apply(_current);
        Repaint();
    }

    private void StopAnimation()
    {
        if (_animating) ShowWholeCube();
        _pivot.localRotation = Quaternion.identity;
        _animating = false;
    }
}
