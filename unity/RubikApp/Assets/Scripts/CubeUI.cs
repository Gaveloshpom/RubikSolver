using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Екранний UI (UI Toolkit, зібраний кодом): розгортка куба, палітра, кнопки, статус.
/// Розгортка завжди дзеркалить те, що показує 3D-куб; у режимі введення по ній можна малювати.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class CubeUI : MonoBehaviour
{
    public CubeAppController app;
    public float cellSize = 26f;

    private const string PanelClass = "rubik-panel";

    // Розташування граней у розгортці (стовпець, рядок у сітці 4x3 граней), порядок U R F D L B
    private static readonly Vector2Int[] FaceSlots =
    {
        new Vector2Int(1, 0), new Vector2Int(2, 1), new Vector2Int(1, 1),
        new Vector2Int(1, 2), new Vector2Int(0, 1), new Vector2Int(3, 1),
    };

    private static CubeUI _instance;

    private VisualElement _root;
    private readonly VisualElement[] _cells = new VisualElement[CubeEditor.StickerCount];
    private readonly VisualElement[] _swatches = new VisualElement[6];
    private VisualElement _palette, _playButtons, _editButtons, _solutionPanel;
    private Button _applyButton;
    private Label _status, _solution, _hint;

    /// <summary>Чи вказівник над панеллю UI (щоб камера і клік по кубу його ігнорували).</summary>
    public static bool IsPointerOverUi(Vector2 screenPosition)
    {
        var panel = _instance != null ? _instance._root?.panel : null;
        if (panel == null) return false;
        var p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
        for (var e = panel.Pick(p); e != null; e = e.parent)
            if (e.ClassListContains(PanelClass)) return true;
        return false;
    }

    private void Start()
    {
        _instance = this;
        _root = GetComponent<UIDocument>().rootVisualElement;
        _root.pickingMode = PickingMode.Ignore;
        Build();
        app.view.Repainted += RefreshNet;
        app.Changed += RefreshPanel;
        RefreshNet();
        RefreshPanel();
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
        if (app != null)
        {
            app.Changed -= RefreshPanel;
            if (app.view != null) app.view.Repainted -= RefreshNet;
        }
    }

    // ---------------------------------------------------------------- побудова

    private void Build()
    {
        _root.Clear();

        var side = Panel();
        side.style.position = Position.Absolute;
        side.style.left = 16;
        side.style.top = 16;
        _root.Add(side);

        side.Add(Title("Розгортка"));
        var net = BuildNet();
        side.Add(net);
        side.style.width = net.style.width.value.value + 24; // + відступи панелі

        _palette = new VisualElement();
        _palette.style.flexDirection = FlexDirection.Row;
        _palette.style.marginTop = 10;
        for (int c = 0; c < 6; c++)
        {
            byte color = (byte)c;
            var swatch = new VisualElement();
            swatch.style.width = 36;
            swatch.style.height = 36;
            swatch.style.marginRight = 6;
            swatch.style.backgroundColor = CubeView.Palette[c];
            SetRadius(swatch, 6);
            swatch.tooltip = $"Колір {c + 1}";
            swatch.RegisterCallback<PointerDownEvent>(_ => app.SelectColor(color));
            _swatches[c] = swatch;
            _palette.Add(swatch);
        }
        side.Add(_palette);

        _status = new Label();
        _status.style.marginTop = 10;
        _status.style.whiteSpace = WhiteSpace.Normal;
        _status.style.color = new Color(0.9f, 0.9f, 0.9f);
        _status.style.fontSize = 14;
        side.Add(_status);

        _playButtons = ButtonRow(
            MakeButton("Ввести свій куб", app.BeginEdit),
            MakeButton("Перемішати", app.Scramble),
            MakeButton("Розв'язати", app.Solve),
            MakeButton("Скинути", app.ResetCube));
        side.Add(_playButtons);

        _applyButton = MakeButton("Готово", app.ApplyEdit);
        _editButtons = ButtonRow(
            _applyButton,
            MakeButton("Очистити", app.ClearEdit),
            MakeButton("Скасувати", app.CancelEdit));
        side.Add(_editButtons);

        _hint = new Label();
        _hint.style.marginTop = 8;
        _hint.style.whiteSpace = WhiteSpace.Normal;
        _hint.style.color = new Color(0.6f, 0.62f, 0.66f);
        _hint.style.fontSize = 12;
        side.Add(_hint);

        _solutionPanel = Panel();
        _solutionPanel.style.position = Position.Absolute;
        _solutionPanel.style.left = 16;
        _solutionPanel.style.right = 16;
        _solutionPanel.style.bottom = 16;
        _solution = new Label();
        _solution.style.whiteSpace = WhiteSpace.Normal;
        _solution.style.color = Color.white;
        _solution.style.fontSize = 18;
        _solutionPanel.Add(_solution);
        _root.Add(_solutionPanel);
    }

    private VisualElement BuildNet()
    {
        const float faceGap = 4f, cellGap = 2f;
        float faceSize = cellSize * 3 + cellGap * 2;

        var net = new VisualElement();
        net.style.width = faceSize * 4 + faceGap * 3;
        net.style.height = faceSize * 3 + faceGap * 2;
        net.style.marginTop = 6;

        for (int face = 0; face < 6; face++)
        for (int row = 0; row < 3; row++)
        for (int col = 0; col < 3; col++)
        {
            int index = face * 9 + row * 3 + col;
            var cell = new VisualElement();
            cell.style.position = Position.Absolute;
            cell.style.left = FaceSlots[face].x * (faceSize + faceGap) + col * (cellSize + cellGap);
            cell.style.top = FaceSlots[face].y * (faceSize + faceGap) + row * (cellSize + cellGap);
            cell.style.width = cellSize;
            cell.style.height = cellSize;
            SetRadius(cell, 3);

            // натиснув і веди — фарбує кілька наліпок підряд
            cell.RegisterCallback<PointerDownEvent>(_ => app.PaintSticker(index));
            cell.RegisterCallback<PointerEnterEvent>(e =>
            {
                if ((e.pressedButtons & 1) != 0) app.PaintSticker(index);
            });

            _cells[index] = cell;
            net.Add(cell);
        }
        return net;
    }

    private static VisualElement Panel()
    {
        var p = new VisualElement();
        p.AddToClassList(PanelClass);
        p.style.backgroundColor = new Color(0.08f, 0.09f, 0.11f, 0.88f);
        p.style.paddingLeft = p.style.paddingRight = 12;
        p.style.paddingTop = p.style.paddingBottom = 12;
        SetRadius(p, 10);
        return p;
    }

    private static Label Title(string text)
    {
        var l = new Label(text);
        l.style.color = Color.white;
        l.style.fontSize = 16;
        l.style.unityFontStyleAndWeight = FontStyle.Bold;
        return l;
    }

    private static VisualElement ButtonRow(params Button[] buttons)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.flexWrap = Wrap.Wrap;
        row.style.marginTop = 10;
        foreach (var b in buttons) row.Add(b);
        return row;
    }

    private static Button MakeButton(string text, System.Action onClick)
    {
        var b = new Button(onClick) { text = text };
        b.style.marginLeft = 0;
        b.style.marginRight = 6;
        b.style.marginBottom = 6;
        b.style.paddingLeft = b.style.paddingRight = 10;
        b.style.height = 30;
        b.style.fontSize = 13;
        b.style.color = Color.white;
        b.style.backgroundColor = new Color(0.22f, 0.24f, 0.29f);
        b.style.borderLeftWidth = b.style.borderRightWidth = b.style.borderTopWidth = b.style.borderBottomWidth = 0;
        SetRadius(b, 6);
        return b;
    }

    private static void SetRadius(VisualElement e, float r)
    {
        e.style.borderTopLeftRadius = e.style.borderTopRightRadius = r;
        e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = r;
    }

    private static void SetBorder(VisualElement e, float width, Color color)
    {
        e.style.borderLeftWidth = e.style.borderRightWidth = e.style.borderTopWidth = e.style.borderBottomWidth = width;
        e.style.borderLeftColor = e.style.borderRightColor = e.style.borderTopColor = e.style.borderBottomColor = color;
    }

    // ---------------------------------------------------------------- оновлення

    private void RefreshNet()
    {
        if (_root == null) return;
        for (int i = 0; i < _cells.Length; i++)
            _cells[i].style.backgroundColor = CubeView.ColorOf(app.view.DisplayedColor(i));
    }

    private void RefreshPanel()
    {
        bool editing = app.IsEditing;
        Show(_palette, editing);
        Show(_editButtons, editing);
        Show(_playButtons, !editing);
        _applyButton.SetEnabled(editing && app.EditValidation.IsValid);

        for (int c = 0; c < 6; c++)
            SetBorder(_swatches[c], c == app.Editor.SelectedColor ? 3 : 0, Color.white);

        _status.text = app.Status;
        _hint.text = editing
            ? "Тримай куб білим центром угору і зеленим до себе. Вибери колір (або 1–6) і клікай по наліпках тут чи на 3D-кубі. Esc — скасувати."
            : "Клавіші: U R F D L B, x y z (Shift — проти годинникової), Space — скрамбл, Enter — розв'язати, +/- — швидкість. Миша: тягни — обертати, коліщатко — зум.";

        _solution.text = app.SolutionText;
        Show(_solutionPanel, app.SolutionText.Length > 0);
    }

    private static void Show(VisualElement e, bool visible) =>
        e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
}
