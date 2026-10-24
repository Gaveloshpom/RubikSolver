using System.Collections.Generic;
using Rubik.Core;
using UnityEngine;

/// <summary>
/// 3D-вигляд куба NxN. Джерело істини — <see cref="Model"/> (Rubik.Core.Cube):
/// хід анімується поворотом групи кубиків, а після завершення модель застосовує хід,
/// кубики повертаються на свої місця і наліпки перефарбовуються з моделі.
///
/// Координати: модель праворука (x вправо, y вгору, z до глядача, F = +z),
/// Unity ліворука, тому z інвертується. Хід моделі по осі X/Y/Z = +90° навколо
/// Unity-векторів right / up / back.
/// </summary>
public class CubeView : MonoBehaviour
{
    // Кольори за номером грані розв'язаного куба: U R F D L B
    public static readonly Color[] Palette =
    {
        new Color(0.95f, 0.95f, 0.95f), // U білий
        new Color(0.80f, 0.08f, 0.10f), // R червоний
        new Color(0.05f, 0.62f, 0.25f), // F зелений
        new Color(1.00f, 0.84f, 0.00f), // D жовтий
        new Color(1.00f, 0.45f, 0.00f), // L помаранчевий
        new Color(0.05f, 0.30f, 0.80f), // B синій
    };

    /// <summary>Колір незаповненої наліпки в режимі введення.</summary>
    public static readonly Color EmptyColor = new Color(0.32f, 0.33f, 0.36f);

    public static Color ColorOf(byte color) => color < Palette.Length ? Palette[color] : EmptyColor;

    private static readonly Vector3[] AxisVectors = { Vector3.right, Vector3.up, Vector3.back };
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    [Range(1, 10)] public int size = 3;
    [Tooltip("Секунд на чверть оберту")]
    [Range(0.03f, 1f)] public float secondsPerTurn = 0.25f;
    public Material stickerMaterial;
    public Material bodyMaterial;

    private class Cubie
    {
        public Transform Transform;
        public Vector3Int Home; // подвоєні координати моделі центру кубика
    }

    private struct Sticker
    {
        public Renderer Renderer;
        public int Index; // індекс у Cube.Stickers
    }

    private readonly List<Cubie> _cubies = new List<Cubie>();
    private readonly List<Sticker> _stickers = new List<Sticker>();
    private readonly Dictionary<Collider, int> _stickerByCollider = new Dictionary<Collider, int>();
    private byte[] _override; // якщо не null, показуємо ці наліпки замість моделі (режим введення)
    private readonly Queue<Move> _queue = new Queue<Move>();
    private readonly List<Cubie> _turning = new List<Cubie>();
    private MaterialPropertyBlock _block;
    private Transform _pivot;

    private bool _animating;
    private Move _current;
    private float _elapsed, _duration, _targetAngle;

    public Cube Model { get; private set; }

    /// <summary>Йде анімація або в черзі є ходи.</summary>
    public bool IsBusy => _animating || _queue.Count > 0;

    /// <summary>Скільки ходів ще не завершено (включно з поточним).</summary>
    public int Pending => _queue.Count + (_animating ? 1 : 0);

    /// <summary>Викликається після кожного перефарбування (хід завершено, новий стан, режим введення).</summary>
    public event System.Action Repainted;

    private void Awake()
    {
        _block = new MaterialPropertyBlock();
        Build(size);
    }

    // ---------------------------------------------------------------- публічне API

    public void Enqueue(Move move)
    {
        if (!move.IsIdentity) _queue.Enqueue(move);
    }

    public void Enqueue(IEnumerable<Move> moves)
    {
        foreach (var m in moves) Enqueue(m);
    }

    /// <summary>Зупиняє анімацію і ставить заданий стан (копію).</summary>
    public void SetState(Cube cube)
    {
        if (cube.Size != size) Build(cube.Size);
        StopAnimation();
        _queue.Clear();
        Model = cube.Clone();
        _override = null;
        Repaint();
    }

    /// <summary>Показує задані наліпки (значення поза 0..5 = порожня) замість моделі. Масив не копіюється.</summary>
    public void ShowStickers(byte[] stickers)
    {
        _override = stickers;
        Repaint();
    }

    /// <summary>Повертає показ стану моделі.</summary>
    public void ShowModel()
    {
        _override = null;
        Repaint();
    }

    /// <summary>Колір наліпки, яку зараз видно (з моделі або з режиму введення).</summary>
    public byte DisplayedColor(int index) => _override != null ? _override[index] : Model.Stickers[index];

    public bool TryGetStickerIndex(Collider collider, out int index) =>
        _stickerByCollider.TryGetValue(collider, out index);

    public void ResetCube() => SetState(new Cube(size));

    /// <summary>Миттєво докручує поточну анімацію і всю чергу.</summary>
    public void FinishAll()
    {
        if (_animating) CompleteCurrent();
        while (_queue.Count > 0) Model.Apply(_queue.Dequeue());
        Repaint();
    }

    // ---------------------------------------------------------------- побудова

    private void Build(int n)
    {
        size = n;
        for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
        _cubies.Clear();
        _stickers.Clear();
        _stickerByCollider.Clear();
        _queue.Clear();
        _override = null;
        _animating = false;

        if (stickerMaterial == null) stickerMaterial = CreateFallbackMaterial(Color.white);
        if (bodyMaterial == null) bodyMaterial = CreateFallbackMaterial(new Color(0.06f, 0.06f, 0.07f));

        _pivot = new GameObject("Pivot").transform;
        _pivot.SetParent(transform, false);

        int m = n - 1;
        var byHome = new Dictionary<Vector3Int, Cubie>();
        for (int x = -m; x <= m; x += 2)
        for (int y = -m; y <= m; y += 2)
        for (int z = -m; z <= m; z += 2)
        {
            bool surface = Mathf.Abs(x) == m || Mathf.Abs(y) == m || Mathf.Abs(z) == m;
            if (!surface) continue;

            var home = new Vector3Int(x, y, z);
            var go = new GameObject($"Cubie {x},{y},{z}");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = ToUnity(home);

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(body.GetComponent<Collider>());
            body.name = "Body";
            body.transform.SetParent(go.transform, false);
            body.transform.localScale = Vector3.one * 0.97f;
            body.GetComponent<Renderer>().sharedMaterial = bodyMaterial;

            var cubie = new Cubie { Transform = go.transform, Home = home };
            _cubies.Add(cubie);
            byHome[home] = cubie;
        }

        var stickerMesh = CreateRoundedSquare(0.14f, 6);
        for (int face = 0; face < 6; face++)
        for (int row = 0; row < n; row++)
        for (int col = 0; col < n; col++)
        {
            StickerPoint(n, face, row, col, out var point, out var normal);
            var cubie = byHome[point - normal];

            var quad = new GameObject($"Sticker {(Face)face}{row}{col}",
                typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            quad.GetComponent<MeshFilter>().sharedMesh = stickerMesh;
            quad.GetComponent<MeshCollider>().sharedMesh = stickerMesh;
            quad.transform.SetParent(cubie.Transform, false);
            var unityNormal = ToUnityDirection(normal);
            quad.transform.localPosition = unityNormal * 0.487f;
            // Quad видно з боку -forward, тож forward дивиться всередину куба
            quad.transform.localRotation = Quaternion.LookRotation(-unityNormal);
            quad.transform.localScale = Vector3.one * 0.86f;
            var r = quad.GetComponent<Renderer>();
            r.sharedMaterial = stickerMaterial;
            int index = face * n * n + row * n + col;
            _stickers.Add(new Sticker { Renderer = r, Index = index });
            _stickerByCollider[quad.GetComponent<Collider>()] = index;
        }

        Model = new Cube(n);
        Repaint();
    }

    /// <summary>
    /// Квадрат 1x1 із заокругленими кутами в площині XY, видимий з боку -Z (як вбудований Quad).
    /// Віяло трикутників від центру по периметру.
    /// </summary>
    private static Mesh CreateRoundedSquare(float radius, int segmentsPerCorner)
    {
        var vertices = new List<Vector3> { Vector3.zero };
        float inner = 0.5f - radius;
        var corners = new[]
        {
            new Vector2(inner, inner), new Vector2(-inner, inner),
            new Vector2(-inner, -inner), new Vector2(inner, -inner),
        };
        for (int c = 0; c < 4; c++)
        for (int s = 0; s <= segmentsPerCorner; s++)
        {
            float angle = (c + (float)s / segmentsPerCorner) * Mathf.PI * 0.5f; // проти годинникової
            vertices.Add(new Vector3(corners[c].x + Mathf.Cos(angle) * radius, corners[c].y + Mathf.Sin(angle) * radius, 0f));
        }

        int perimeter = vertices.Count - 1;
        var triangles = new int[perimeter * 3];
        for (int i = 0; i < perimeter; i++)
        {
            // за годинниковою, якщо дивитися з -Z: так Unity вважає грань лицьовою
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = 1 + (i + 1) % perimeter;
            triangles[i * 3 + 2] = 1 + i;
        }

        var normals = new Vector3[vertices.Count];
        for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.back;

        var mesh = new Mesh { name = "RoundedSticker" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Material CreateFallbackMaterial(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(shader);
        mat.SetColor(BaseColorId, color);
        mat.color = color;
        return mat;
    }

    /// <summary>Те саме, що Cube.ToPoint: наліпка -> подвоєна точка на поверхні + нормаль грані (модель).</summary>
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

    private static Vector3 ToUnity(Vector3Int doubled) => new Vector3(doubled.x, doubled.y, -doubled.z) * 0.5f;
    private static Vector3 ToUnityDirection(Vector3Int d) => new Vector3(d.x, d.y, -d.z);

    // ---------------------------------------------------------------- фарбування

    private void Repaint()
    {
        foreach (var s in _stickers)
        {
            _block.SetColor(BaseColorId, ColorOf(DisplayedColor(s.Index)));
            s.Renderer.SetPropertyBlock(_block);
        }
        Repainted?.Invoke();
    }

    // ---------------------------------------------------------------- анімація

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

        // 3 чверті показуємо як одну чверть у зворотний бік
        int turns = move.Turns == 3 ? -1 : move.Turns;
        _targetAngle = 90f * turns;
        _duration = secondsPerTurn * (Mathf.Abs(turns) == 2 ? 1.5f : 1f);

        int m = size - 1;
        _turning.Clear();
        _pivot.localRotation = Quaternion.identity;
        foreach (var c in _cubies)
        {
            int coord = c.Home[(int)move.Axis];
            int layer = (m - coord) / 2; // шар 0 = додатна грань
            if (layer >= move.LayerFrom && layer <= move.LayerTo)
            {
                c.Transform.SetParent(_pivot, true);
                _turning.Add(c);
            }
        }
    }

    private void CompleteCurrent()
    {
        StopAnimation();
        Model.Apply(_current);
        Repaint();
    }

    private void StopAnimation()
    {
        foreach (var c in _turning)
        {
            c.Transform.SetParent(transform, false);
            c.Transform.localPosition = ToUnity(c.Home);
            c.Transform.localRotation = Quaternion.identity;
        }
        _turning.Clear();
        if (_pivot != null) _pivot.localRotation = Quaternion.identity;
        _animating = false;
    }
}
