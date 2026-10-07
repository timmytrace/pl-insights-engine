using System.Globalization;
using Studio.Engine;

namespace Studio.Crew;

/// <summary>
/// Fixed translations for template graphics, so a Spanish or French viewer never sees English
/// while the crew is still working. Story bodies are left out of translated templates: the
/// numbers carry the graphic until The Host's own version lands.
/// </summary>
public static class TemplateLocalizer
{
    private static readonly Dictionary<string, (string Es, string Fr)> Labels = new()
    {
        ["Shot speed"] = ("Velocidad del disparo", "Vitesse de frappe"),
        ["Distance"] = ("Distancia", "Distance"),
        ["Speed"] = ("Velocidad", "Vitesse"),
        ["Sprint"] = ("Sprint", "Sprint"),
        ["Difficulty"] = ("Dificultad", "Difficulté"),
        ["Ball speed"] = ("Velocidad del balón", "Vitesse du ballon"),
        ["PPDA (match)"] = ("PPDA (partido)", "PPDA (match)"),
        ["Momentum"] = ("Impulso", "Dynamique"),
        ["Chaos index"] = ("Índice de caos", "Indice de chaos"),
        ["Turnovers / min"] = ("Pérdidas / min", "Pertes / min"),
        ["Control index"] = ("Índice de control", "Indice de contrôle"),
        ["Tempo"] = ("Ritmo", "Tempo"),
    };

    public static InsightCard ForViewer(InsightCard template, ViewerProfile viewer, MatchInfo info)
    {
        var card = template with { Id = $"{template.Id}-{viewer.Id}", Viewer = viewer.Id, Language = viewer.Language, Persona = PersonaName(viewer.Persona) };
        if (viewer.Language == "en") return card;

        var team = info.Team(template.Team);
        var player = template.PlayerId is { } pid ? info.Team(template.Team).Player(pid).Name : team.Name;
        string L(string es, string fr) => viewer.Language == "es" ? es : fr;

        var headline = template.Kind switch
        {
            MomentKind.Goal => L($"¡GOL! — {player}", $"BUT ! — {player}"),
            MomentKind.BigChance => L($"Gran ocasión — {player}", $"Grosse occasion — {player}"),
            MomentKind.PressSurge => L($"{team.Name} presiona arriba", $"{team.Name} presse plus haut"),
            MomentKind.MomentumSwing => L($"El partido gira hacia {team.Name}", $"La dynamique bascule vers {team.Name}"),
            MomentKind.ChaosSpell => L("Control o caos: caos", "Contrôle ou chaos : chaos"),
            MomentKind.ControlSpell => L($"{team.ShortName} al mando", $"{team.ShortName} aux commandes"),
            MomentKind.ElitePass => L($"Calidad de pase — {player}", $"Qualité de passe — {player}"),
            _ => template.Headline,
        };
        var body = template.Kind switch
        {
            MomentKind.ShotSpeed => L("Velocidad del disparo", "Vitesse de frappe"),
            MomentKind.SprintSpeed => L("Sprint a máxima velocidad", "Sprint à pleine vitesse"),
            _ => "",
        };
        var stats = template.Stats.Select(s => Labels.TryGetValue(s.Label, out var t)
            ? s with { Label = L(t.Es, t.Fr), Value = Decimal(s.Value, viewer.Language) }
            : s with { Value = Decimal(s.Value, viewer.Language) }).ToList();
        return card with { Headline = headline, Body = body, Stats = stats };
    }

    public static string PersonaName(Persona p) => p switch
    {
        Persona.Analyst => "analyst",
        Persona.Casual => "casual",
        Persona.ClubFan => "club_fan",
        Persona.PlayerFocus => "player_focus",
        _ => "neutral",
    };

    /// <summary>Spanish and French use a decimal comma.</summary>
    public static string Decimal(string value, string language) =>
        language == "en" ? value : value.Replace('.', ',');

    public static string Format(double v, string language) =>
        Decimal(FactSheet.Format(v), language);

    public static string LanguageName(string code) =>
        CultureInfo.GetCultureInfo(code).EnglishName;
}
