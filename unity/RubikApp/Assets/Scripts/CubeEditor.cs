using Rubik.Core;
using Rubik.Solver3x3;

/// <summary>
/// Стан режиму введення куба 3x3: 54 наліпки, де <see cref="Empty"/> = ще не заповнено.
/// Центри зафіксовані: куб тримають білим центром угору і зеленим до себе.
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

    /// <summary>Фарбує наліпку вибраним кольором. Повертає true, якщо щось змінилося.</summary>
    public bool Paint(int index)
    {
        if (IsCenter(index) || Stickers[index] == SelectedColor) return false;
        Stickers[index] = SelectedColor;
        return true;
    }

    public CubeValidation Validate() => CubeValidator.Validate(Stickers);

    public Cube ToCube() => Cube.FromStickers(3, Stickers);

    private static readonly string[] ColorNames = { "білого", "червоного", "зеленого", "жовтого", "помаранчевого", "синього" };

    public static string Describe(CubeValidation v)
    {
        switch (v.Error)
        {
            case CubeValidationError.None: return "Куб коректний — натисни «Готово»";
            case CubeValidationError.NotFilled: return $"Заповни всі наліпки (порожніх: {v.Detail})";
            case CubeValidationError.WrongCount: return $"Наліпок {ColorNames[v.Detail]} кольору має бути 9, а зараз {v.Count}";
            case CubeValidationError.WrongCenters: return "Центри не на своїх місцях";
            case CubeValidationError.InvalidCorner: return "Є кут із кольорів, яких не буває разом";
            case CubeValidationError.InvalidEdge: return "Є ребро з кольорів, яких не буває разом";
            case CubeValidationError.DuplicateCorner: return "Два однакові кути";
            case CubeValidationError.DuplicateEdge: return "Два однакові ребра";
            case CubeValidationError.TwistedCorner: return "Один кут перекручений — такий куб не зібрати";
            case CubeValidationError.FlippedEdge: return "Одне ребро перевернуте — такий куб не зібрати";
            case CubeValidationError.Parity: return "Два елементи переставлені місцями — такий куб не зібрати";
            default: return v.Error.ToString();
        }
    }
}
