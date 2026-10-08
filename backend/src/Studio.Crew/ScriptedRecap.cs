using System.Text.Json;
using System.Text.RegularExpressions;

namespace Studio.Crew;

/// <summary>The offline recap: a fixed studio conversation per language, filled from the full-time sheet.</summary>
internal static partial class ScriptedRecap
{
    private sealed record Phrases(string Open, string FirstMoment, string NextMoment, string Stats, string Ref, string Close);

    private static readonly Dictionary<string, Phrases> Book = new()
    {
        ["en"] = new(
            "Full time at {venue}: {home_team} {home_score}, {away_team} {away_score}.",
            "The story starts in minute {moment_1_minute}: a {k1} for {w1}.",
            "Then minute {moment_2_minute}, a {k2} for {w2}, and that shaped the rest of the match.",
            "{home_team} had {home_possession}% of the ball and {home_xg} xG. {away_team} finished on {away_xg}.",
            "Every number in tonight's coverage was checked against the match data before it went on air.",
            "One name to remember: {star_1_name}. That's full time from the Virtual Studio Crew."),
        ["es"] = new(
            "Final en {venue}: {home_team} {home_score}, {away_team} {away_score}.",
            "La historia empieza en el minuto {moment_1_minute}: {k1} de {w1}.",
            "Después, en el minuto {moment_2_minute}, {k2} de {w2}, y eso marcó el resto del partido.",
            "{home_team} tuvo el {home_possession}% del balón y {home_xg} xG. {away_team} terminó con {away_xg}.",
            "Cada cifra de esta noche se comprobó con los datos del partido antes de emitirse.",
            "Un nombre para recordar: {star_1_name}. Esto ha sido todo desde el Virtual Studio Crew."),
        ["fr"] = new(
            "Coup de sifflet final à {venue} : {home_team} {home_score}, {away_team} {away_score}.",
            "Tout commence à la {moment_1_minute}e minute : {k1} pour {w1}.",
            "Puis à la {moment_2_minute}e minute, {k2} pour {w2}, et la suite du match en a dépendu.",
            "{home_team} a eu {home_possession} % du ballon et {home_xg} xG. {away_team} termine à {away_xg}.",
            "Chaque chiffre de ce soir a été vérifié avec les données du match avant d'être diffusé.",
            "Un nom à retenir : {star_1_name}. C'était le Virtual Studio Crew."),
    };

    private static readonly Dictionary<string, Dictionary<string, string>> Kinds = new()
    {
        ["en"] = new() { ["goal"] = "goal", ["big chance"] = "big chance", ["pressing surge"] = "pressing surge", ["momentum swing"] = "momentum swing" },
        ["es"] = new() { ["goal"] = "gol", ["big chance"] = "gran ocasión", ["pressing surge"] = "presión alta", ["momentum swing"] = "cambio de dinámica" },
        ["fr"] = new() { ["goal"] = "but", ["big chance"] = "grosse occasion", ["pressing surge"] = "pressing intense", ["momentum swing"] = "bascule du match" },
    };

    public static string Write(string prompt)
    {
        var lang = LanguagePattern().Match(prompt).Groups[1].Value;
        if (!Book.ContainsKey(lang)) lang = "en";
        var start = prompt.IndexOf("<facts>", StringComparison.Ordinal) + 7;
        var end = prompt.IndexOf("</facts>", StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(prompt[start..end]);
        var facts = FactSheet.ReadPromptFacts(doc.RootElement);

        string Fill(string text)
        {
            foreach (var (name, el) in facts)
                text = text.Replace("{" + name + "}", el.ValueKind == JsonValueKind.Number ? TemplateLocalizer.Format(el.GetDouble(), lang) : el.ToString());
            for (var i = 1; i <= 2; i++)
            {
                // Moments read "goal (Udo Jansen, Saltmarsh City)": split into what happened and who.
                var raw = facts.TryGetValue($"moment_{i}", out var m) ? m.GetString() ?? "" : "";
                var open = raw.IndexOf(" (", StringComparison.Ordinal);
                var kind = open > 0 ? raw[..open] : raw;
                var who = open > 0 ? raw[(open + 2)..].TrimEnd(')') : "";
                text = text.Replace($"{{k{i}}}", Kinds[lang].GetValueOrDefault(kind, kind)).Replace($"{{w{i}}}", who);
            }
            return text;
        }

        var p = Book[lang];
        var lines = new List<RecapLine> { new("host", Fill(p.Open)) };
        if (facts.ContainsKey("moment_1")) lines.Add(new("gaffer", Fill(p.FirstMoment)));
        if (facts.ContainsKey("moment_2")) lines.Add(new("gaffer", Fill(p.NextMoment)));
        lines.Add(new("stats", Fill(p.Stats)));
        lines.Add(new("ref", Fill(p.Ref)));
        lines.Add(new("host", Fill(p.Close)));
        return JsonSerializer.Serialize(new RecapDto(lines));
    }

    [GeneratedRegex(@"Language: (\w\w)")]
    private static partial Regex LanguagePattern();
}
