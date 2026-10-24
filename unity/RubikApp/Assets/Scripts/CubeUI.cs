using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Screen UI (UI Toolkit, built in code): settings, cube net, palette, buttons, status and the solution player.
/// The net always mirrors what the 3D cube shows; in edit mode you can paint on it.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class CubeUI : MonoBehaviour
{
    public CubeAppController app;
    public float cellSize = 26f;

    private const string PanelClass = "rubik-panel";
    private const string ScalePrefsKey = "uiScale";
    private const float MinScale = 0.6f, MaxScale = 1.8f, ScaleStep = 0.1f;

    // Position of each face in the net (column, row in a 4x3 grid of faces), order U R F D L B
    private static readonly Vector2Int[] FaceSlots =
    {
        new Vector2Int(1, 0), new Vector2Int(2, 1), new Vector2Int(1, 1),
        new Vector2Int(1, 2), new Vector2Int(0, 1), new Vector2Int(3, 1),
    };

    private static readonly Color ButtonColor = new Color(0.22f, 0.24f, 0.29f);
    private static readonly Color AccentColor = new Color(0.25f, 0.55f, 0.95f);
    private static readonly Color TextColor = new Color(0.9f, 0.9f, 0.9f);

    private static CubeUI _instance;

    private VisualElement _root;
    private PanelSettings _panelSettings;
    private Vector2Int _baseReferenceResolution;
    private float _scale = 1f;

    private readonly VisualElement[] _cells = new VisualElement[CubeEditor.StickerCount];
    private readonly VisualElement[] _swatches = new VisualElement[6];
    private VisualElement _palette, _playButtons, _editButtons, _speedRow, _solutionPanel, _tokens;
    private Button _applyButton, _playButton;
    private Label _status, _hint, _progress, _nextMove, _speedValue, _scaleValue;
    private Slider _speed;
    private readonly List<Label> _tokenLabels = new List<Label>();
    private int _tokensVersion = -1;

    /// <summary>Whether the pointer is over a UI panel (so the camera and cube clicks ignore it).</summary>
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
        var document = GetComponent<UIDocument>();

        // work on a copy so changing the UI size does not modify the asset
        _panelSettings = Instantiate(document.panelSettings);
        document.panelSettings = _panelSettings;
        _baseReferenceResolution = _panelSettings.referenceResolution;
        SetScale(PlayerPrefs.GetFloat(ScalePrefsKey, 1f), save: false);

        _root = document.rootVisualElement;
        _root.pickingMode = PickingMode.Ignore;
        Rebuild();
        app.view.Repainted += RefreshNet;
        app.Changed += RefreshPanel;
        Loc.Changed += Rebuild;
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
        Loc.Changed -= Rebuild;
        if (app != null)
        {
            app.Changed -= RefreshPanel;
            if (app.view != null) app.view.Repainted -= RefreshNet;
        }
        if (_panelSettings != null) Destroy(_panelSettings);
    }

    private void SetScale(float scale, bool save = true)
    {
        _scale = Mathf.Clamp(Mathf.Round(scale / ScaleStep) * ScaleStep, MinScale, MaxScale);
        // ScaleWithScreenSize: a smaller reference resolution makes everything bigger
        _panelSettings.referenceResolution = Vector2Int.RoundToInt((Vector2)_baseReferenceResolution / _scale);
        if (_scaleValue != null) _scaleValue.text = $"{_scale * 100f:0}%";
        if (!save) return;
        PlayerPrefs.SetFloat(ScalePrefsKey, _scale);
        PlayerPrefs.Save();
    }

    // ---------------------------------------------------------------- building

    /// <summary>Builds the whole UI from scratch (also on a language switch).</summary>
    private void Rebuild()
    {
        _root.Clear();
        _tokensVersion = -1;

        var side = Panel();
        side.style.position = Position.Absolute;
        side.style.left = 16;
        side.style.top = 16;
        _root.Add(side);

        side.Add(BuildSettingsRow());
        side.Add(Title(Loc.T("net.title")));
        var net = BuildNet();
        side.Add(net);
        side.style.width = net.style.width.value.value + 24; // + panel padding

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
            swatch.tooltip = Loc.T("color.tooltip", c + 1);
            swatch.RegisterCallback<PointerDownEvent>(_ => app.SelectColor(color));
            _swatches[c] = swatch;
            _palette.Add(swatch);
        }
        side.Add(_palette);

        _status = new Label();
        _status.style.marginTop = 10;
        _status.style.whiteSpace = WhiteSpace.Normal;
        _status.style.color = TextColor;
        _status.style.fontSize = 14;
        side.Add(_status);

        _playButtons = ButtonRow(
            MakeButton(Loc.T("btn.edit"), app.BeginEdit),
            MakeButton(Loc.T("btn.scramble"), app.Scramble),
            MakeButton(Loc.T("btn.solve"), app.Solve),
            MakeButton(Loc.T("btn.reset"), app.ResetCube));
        side.Add(_playButtons);

        _applyButton = MakeButton(Loc.T("btn.done"), app.ApplyEdit);
        _editButtons = ButtonRow(
            _applyButton,
            MakeButton(Loc.T("btn.clear"), app.ClearEdit),
            MakeButton(Loc.T("btn.cancel"), app.CancelEdit));
        side.Add(_editButtons);

        _speedRow = Row();
        _speedRow.style.marginTop = 4;
        _speed = new Slider(0.5f, 12f);
        _speed.style.flexGrow = 1;
        _speed.RegisterValueChangedCallback(e => app.TurnsPerSecond = e.newValue);
        _speedValue = SmallLabel("");
        _speedValue.style.width = 80;
        _speedRow.Add(SmallLabel(Loc.T("speed.label")));
        _speedRow.Add(_speed);
        _speedRow.Add(_speedValue);
        side.Add(_speedRow);

        _hint = new Label();
        _hint.style.marginTop = 8;
        _hint.style.whiteSpace = WhiteSpace.Normal;
        _hint.style.color = new Color(0.6f, 0.62f, 0.66f);
        _hint.style.fontSize = 12;
        side.Add(_hint);

        BuildPlayer();
        RefreshNet();
        RefreshPanel();
    }

    private VisualElement BuildSettingsRow()
    {
        var row = Row();
        row.style.marginBottom = 10;
        row.style.flexWrap = Wrap.Wrap;

        row.Add(SmallLabel(Loc.T("settings.language")));
        foreach (var (language, code) in new[] { (Language.English, "EN"), (Language.Ukrainian, "UK") })
        {
            var b = MakeButton(code, () => Loc.Current = language);
            b.style.marginLeft = 6;
            b.style.marginRight = 0;
            b.style.marginBottom = 0;
            b.style.backgroundColor = Loc.Current == language ? AccentColor : ButtonColor;
            row.Add(b);
        }

        var spacer = new VisualElement();
        spacer.style.flexGrow = 1;
        row.Add(spacer);

        row.Add(SmallLabel(Loc.T("settings.uiScale")));
        var smaller = MakeButton("-", () => SetScale(_scale - ScaleStep));
        var bigger = MakeButton("+", () => SetScale(_scale + ScaleStep));
        smaller.style.marginLeft = 6;
        smaller.style.marginRight = bigger.style.marginRight = 0;
        smaller.style.marginBottom = bigger.style.marginBottom = 0;
        _scaleValue = SmallLabel($"{_scale * 100f:0}%");
        _scaleValue.style.width = 46;
        _scaleValue.style.unityTextAlign = TextAnchor.MiddleCenter;
        row.Add(smaller);
        row.Add(_scaleValue);
        row.Add(bigger);
        return row;
    }

    private void BuildPlayer()
    {
        _solutionPanel = Panel();
        _solutionPanel.style.position = Position.Absolute;
        _solutionPanel.style.left = 16;
        _solutionPanel.style.right = 16;
        _solutionPanel.style.bottom = 16;
        _root.Add(_solutionPanel);

        var header = Row();
        header.style.flexWrap = Wrap.Wrap;
        _solutionPanel.Add(header);

        _nextMove = new Label();
        _nextMove.style.color = Color.white;
        _nextMove.style.fontSize = 34;
        _nextMove.style.unityFontStyleAndWeight = FontStyle.Bold;
        _nextMove.style.minWidth = 90;
        header.Add(_nextMove);

        _progress = new Label();
        _progress.style.color = new Color(0.75f, 0.77f, 0.8f);
        _progress.style.fontSize = 14;
        _progress.style.marginRight = 16;
        header.Add(_progress);

        _playButton = MakeButton(Loc.T("player.play"), app.TogglePlay);
        header.Add(MakeButton(Loc.T("player.start"), () => app.JumpTo(0)));
        header.Add(MakeButton(Loc.T("player.back"), app.StepBack));
        header.Add(_playButton);
        header.Add(MakeButton(Loc.T("player.forward"), app.StepForward));
        header.Add(MakeButton(Loc.T("player.end"), () => app.JumpTo(app.SolutionLength)));

        _tokens = new VisualElement();
        _tokens.style.flexDirection = FlexDirection.Row;
        _tokens.style.flexWrap = Wrap.Wrap;
        _tokens.style.marginTop = 8;
        _solutionPanel.Add(_tokens);
    }

    private void RebuildTokens()
    {
        _tokensVersion = app.SolutionVersion;
        _tokens.Clear();
        _tokenLabels.Clear();
        for (int i = 0; i < app.SolutionLength; i++)
        {
            int after = i + 1;
            var token = new Label(app.SolutionMove(i));
            token.style.fontSize = 18;
            token.style.paddingLeft = token.style.paddingRight = 6;
            token.style.paddingTop = token.style.paddingBottom = 2;
            token.style.marginRight = 4;
            token.style.marginBottom = 4;
            SetRadius(token, 4);
            token.tooltip = Loc.T("player.token", after);
            token.RegisterCallback<PointerDownEvent>(_ => app.JumpTo(after));
            _tokenLabels.Add(token);
            _tokens.Add(token);
        }
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

            // press and drag to paint several stickers in a row
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

    private static VisualElement Row()
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        return row;
    }

    private static Label Title(string text)
    {
        var l = new Label(text);
        l.style.color = Color.white;
        l.style.fontSize = 16;
        l.style.unityFontStyleAndWeight = FontStyle.Bold;
        return l;
    }

    private static Label SmallLabel(string text)
    {
        var l = new Label(text);
        l.style.color = TextColor;
        l.style.fontSize = 13;
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
        b.style.backgroundColor = ButtonColor;
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

    // ---------------------------------------------------------------- refreshing

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
        _hint.text = Loc.T(editing ? "hint.edit" : "hint.play");

        Show(_speedRow, !editing);
        _speed.SetValueWithoutNotify(app.TurnsPerSecond);
        _speedValue.text = Loc.T("speed.value", app.TurnsPerSecond);

        RefreshPlayer();
    }

    private void RefreshPlayer()
    {
        Show(_solutionPanel, app.HasSolution);
        if (!app.HasSolution) return;
        if (_tokensVersion != app.SolutionVersion) RebuildTokens();

        int pos = app.SolutionPosition, total = app.SolutionLength;
        _nextMove.text = pos < total ? app.SolutionMove(pos) : "—";
        _progress.text = total == 0 ? Loc.T("status.alreadySolved")
            : pos < total ? Loc.T("player.next", pos, total)
            : Loc.T("player.solved", total);
        _playButton.text = Loc.T(app.IsPlaying ? "player.pause" : "player.play");
        _playButton.style.backgroundColor = app.IsPlaying ? AccentColor : ButtonColor;

        for (int i = 0; i < _tokenLabels.Count; i++)
        {
            var t = _tokenLabels[i];
            bool done = i < pos, next = i == pos;
            t.style.color = done ? new Color(0.5f, 0.52f, 0.56f) : Color.white;
            t.style.backgroundColor = next ? AccentColor : Color.clear;
        }
    }

    private static void Show(VisualElement e, bool visible) =>
        e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
}
