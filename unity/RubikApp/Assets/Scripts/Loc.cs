using System;
using System.Collections.Generic;
using UnityEngine;

public enum Language { English, Ukrainian }

/// <summary>
/// Minimal localization: every key has one string per <see cref="Language"/> (same order as the enum).
/// The chosen language is stored in PlayerPrefs; English is the default.
/// </summary>
public static class Loc
{
    private const string PrefsKey = "language";

    // Loaded lazily: PlayerPrefs may not be read from static initializers
    private static Language? _loaded;
    private static Language Active => _loaded ?? (_loaded = Load()).Value;

    public static event Action Changed;

    public static Language Current
    {
        get => Active;
        set
        {
            if (Active == value) return;
            _loaded = value;
            PlayerPrefs.SetInt(PrefsKey, (int)value);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }

    public static string T(string key)
    {
        if (Table.TryGetValue(key, out var texts)) return texts[(int)Active];
        Debug.LogWarning($"[Loc] Missing key '{key}'");
        return key;
    }

    public static string T(string key, params object[] args) => string.Format(T(key), args);

    private static Language Load()
    {
        int saved = PlayerPrefs.GetInt(PrefsKey, (int)Language.English);
        return Enum.IsDefined(typeof(Language), saved) ? (Language)saved : Language.English;
    }

    private static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
    {
        // panel
        ["net.title"] = new[] { "Cube net", "Розгортка" },
        ["settings.language"] = new[] { "Language", "Мова" },
        ["settings.uiScale"] = new[] { "UI size", "Розмір UI" },
        ["speed.label"] = new[] { "Speed", "Швидкість" },
        ["speed.value"] = new[] { " {0:0.#} moves/s", " {0:0.#} ход/с" },
        ["color.tooltip"] = new[] { "Color {0}", "Колір {0}" },

        ["btn.edit"] = new[] { "Enter my cube", "Ввести свій куб" },
        ["btn.scramble"] = new[] { "Scramble", "Перемішати" },
        ["btn.solve"] = new[] { "Solve", "Розв'язати" },
        ["btn.reset"] = new[] { "Reset", "Скинути" },
        ["btn.done"] = new[] { "Done", "Готово" },
        ["btn.clear"] = new[] { "Clear", "Очистити" },
        ["btn.cancel"] = new[] { "Cancel", "Скасувати" },
        ["btn.menu"] = new[] { "Menu", "Меню" },
        ["btn.hideMenu"] = new[] { "Hide menu", "Сховати меню" },

        ["hint.touch"] = new[]
        {
            "Drag a sticker to turn its layer, \ndrag the background to rotate the cube, \npinch to zoom.",
            "Тягни наліпку — крутити шар, \nтягни фон — обертати куб, \nдва пальці — зум.",
        },

        ["hint.play"] = new[]
        {
            "Mouse: drag a sticker to turn its layer, drag the background to orbit, wheel to zoom. " +
            "\nKeys: U R F D L B, x y z (Shift = counter-clockwise), \nSpace = scramble, \nEnter = solve, \n+/- = speed. " +
            "\nSolution: Left/Right arrow = step, \nP play/pause, \nHome/End start/end.",
            "\nМиша: тягни наліпку — крутити шар, \nтягни фон — обертати камеру, \nколіщатко — зум. " +
            "\nКлавіші: U R F D L B, x y z (Shift — проти годинникової), \nSpace — скрамбл, \nEnter — розв'язати, \n+/- — швидкість. " +
            "\nРішення: стрілки вліво/вправо — крок, \nP — грати/пауза, \nHome/End — початок/кінець.",
        },
        ["hint.edit"] = new[]
        {
            "Hold the cube with the white centre up and the green centre facing you. Pick a color (or 1–6) and click stickers here or on the 3D cube. Esc = cancel.",
            "Тримай куб білим центром угору і зеленим до себе. Вибери колір (або 1–6) і клікай по наліпках тут чи на 3D-кубі. Esc — скасувати.",
        },

        // player
        ["player.start"] = new[] { "|< Start", "|< Початок" },
        ["player.back"] = new[] { "< Back", "< Назад" },
        ["player.play"] = new[] { "Play", "Грати" },
        ["player.pause"] = new[] { "Pause", "Пауза" },
        ["player.forward"] = new[] { "Forward >", "Вперед >" },
        ["player.end"] = new[] { "End >|", "Кінець >|" },
        ["player.next"] = new[] { "Next move\n{0} / {1}", "Наступний хід\n{0} / {1}" },
        ["player.solved"] = new[] { "Solved!\n{0} / {0}", "Зібрано!\n{0} / {0}" },
        ["player.token"] = new[] { "Jump to the state after move {0}", "Перейти до стану після ходу {0}" },

        // status
        ["status.loading"] = new[] { "Loading the solver...", "Завантажую солвер..." },
        ["status.reset"] =new[] { "Reset", "Скинуто" },
        ["status.scramble"] = new[] { "Scramble: {0}", "Скрамбл: {0}" },
        ["status.only3x3"] = new[] { "The solver supports 3x3 only", "Солвер підтримує тільки 3x3" },
        ["status.alreadySolved"] = new[] { "The cube is already solved", "Куб уже зібраний" },
        ["status.solving"] = new[] { "Searching for a solution...", "Шукаю рішення..." },
        ["status.solverError"] = new[] { "Solver error (see Console)", "Помилка солвера (див. Console)" },
        ["status.solution"] = new[] { "Solution: {0} moves. Press Play or the Right arrow to step", "Рішення: {0} ходів. «Грати» або стрілка вправо — крок" },
        ["status.entered"] = new[] { "Cube entered, ready to solve", "Куб введено — можна розв'язувати" },

        // validation (CubeEditor)
        ["val.none"] = new[] { "The cube is valid, press Done", "Куб коректний — натисни «Готово»" },
        ["val.notFilled"] = new[] { "Fill in all stickers ({0} empty)", "Заповни всі наліпки (порожніх: {0})" },
        ["val.wrongCount"] = new[] { "There must be 9 {0} stickers, found {1}", "Наліпок {0} кольору має бути 9, а зараз {1}" },
        ["val.wrongCenters"] = new[] { "The centres are not in place", "Центри не на своїх місцях" },
        ["val.invalidCorner"] = new[] { "A corner has colors that never meet", "Є кут із кольорів, яких не буває разом" },
        ["val.invalidEdge"] = new[] { "An edge has colors that never meet", "Є ребро з кольорів, яких не буває разом" },
        ["val.duplicateCorner"] = new[] { "Two identical corners", "Два однакові кути" },
        ["val.duplicateEdge"] = new[] { "Two identical edges", "Два однакові ребра" },
        ["val.twistedCorner"] = new[] { "A corner is twisted, this cube cannot be solved", "Один кут перекручений — такий куб не зібрати" },
        ["val.flippedEdge"] = new[] { "An edge is flipped, this cube cannot be solved", "Одне ребро перевернуте — такий куб не зібрати" },
        ["val.parity"] = new[] { "Two pieces are swapped, this cube cannot be solved", "Два елементи переставлені місцями — такий куб не зібрати" },

        // color names as used in val.wrongCount (Ukrainian needs the genitive)
        ["color.0"] = new[] { "white", "білого" },
        ["color.1"] = new[] { "red", "червоного" },
        ["color.2"] = new[] { "green", "зеленого" },
        ["color.3"] = new[] { "yellow", "жовтого" },
        ["color.4"] = new[] { "orange", "помаранчевого" },
        ["color.5"] = new[] { "blue", "синього" },
    };
}
