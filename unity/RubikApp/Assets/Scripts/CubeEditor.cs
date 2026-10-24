using Rubik.Core;
using Rubik.Solver3x3;

/// <summary>
/// Edit-mode state for a 3x3 cube: 54 stickers, where <see cref="Empty"/> = not filled in yet.
/// Centres are fixed: the cube is held with the white centre up and the green centre facing you.
/// </summary>
public class CubeEditor
{
    public const byte Empty = 255;
    public const int StickerCount = 54;

    public byte[] Stickers { get; } = new byte[StickerCount];
    public byte SelectedColor { get; set; }

    public static bool IsCenter(int index) => index % 9 == 4;

    public void Load(Cube cube) => cube.Stickers.CopyTo(Stickers);

    public void Clear()
    {
        for (int i = 0; i < StickerCount; i++)
            Stickers[i] = IsCenter(i) ? (byte)(i / 9) : Empty;
    }

    /// <summary>Paints a sticker with the selected color. Returns true if anything changed.</summary>
    public bool Paint(int index)
    {
        if (IsCenter(index) || Stickers[index] == SelectedColor) return false;
        Stickers[index] = SelectedColor;
        return true;
    }

    public CubeValidation Validate() => CubeValidator.Validate(Stickers);

    public Cube ToCube() => Cube.FromStickers(3, Stickers);

    /// <summary>Localization key and arguments describing a validation result.</summary>
    public static (string key, object[] args) Describe(CubeValidation v)
    {
        switch (v.Error)
        {
            case CubeValidationError.None: return ("val.none", null);
            case CubeValidationError.NotFilled: return ("val.notFilled", new object[] { v.Detail });
            case CubeValidationError.WrongCount: return ("val.wrongCount", new object[] { Loc.T("color." + v.Detail), v.Count });
            case CubeValidationError.WrongCenters: return ("val.wrongCenters", null);
            case CubeValidationError.InvalidCorner: return ("val.invalidCorner", null);
            case CubeValidationError.InvalidEdge: return ("val.invalidEdge", null);
            case CubeValidationError.DuplicateCorner: return ("val.duplicateCorner", null);
            case CubeValidationError.DuplicateEdge: return ("val.duplicateEdge", null);
            case CubeValidationError.TwistedCorner: return ("val.twistedCorner", null);
            case CubeValidationError.FlippedEdge: return ("val.flippedEdge", null);
            default: return ("val.parity", null);
        }
    }
}
