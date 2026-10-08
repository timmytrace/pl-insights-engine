using System.Text.Json;

namespace Studio.Crew;

/// <summary>
/// The offline Host's phrasebook: a headline, an analyst line and a casual line for each kind of
/// story, in each language, filled from the fact sheet. Club fans and player-focus viewers get
/// the casual line plus their own framing. Scripted copy, standing in until a real model is wired.
/// </summary>
internal static class ScriptedHost
{
    private record Lines(string Headline, string Analyst, string Casual);

    private static readonly Dictionary<(string Kind, string Lang), Lines> Book = new()
    {
        [("Goal", "en")] = new("{player} scores!", "{team} turn a {xg} xG chance into a goal. xG now {team_xg} to {opp_xg}.", "A {chance} chance, and {team} make it count."),
        [("Goal", "es")] = new("¡Gol de {player}!", "{team} convierte una ocasión de {xg} xG. xG: {team_xg} a {opp_xg}.", "Una ocasión {chance} y {team} no perdona."),
        [("Goal", "fr")] = new("But de {player} !", "{team} transforme une occasion à {xg} xG. xG : {team_xg} à {opp_xg}.", "Une occasion {chance}, et {team} ne la rate pas."),
        [("BigChance", "en")] = new("{team} so close", "A {xg} xG chance, {outcome}. xG {team_xg} to {opp_xg}.", "What a chance for {team}! That had to be a goal."),
        [("BigChance", "es")] = new("¡Qué ocasión de {team}!", "Ocasión de {xg} xG: {outcome}. xG {team_xg} a {opp_xg}.", "¡Qué ocasión para {team}! Tenía que ser gol."),
        [("BigChance", "fr")] = new("Grosse occasion pour {team}", "Occasion à {xg} xG : {outcome}. xG {team_xg} à {opp_xg}.", "Quelle occasion pour {team} ! Ça devait finir au fond."),
        [("PressSurge", "en")] = new("{team} press higher", "PPDA {ppda10} in the last 10 minutes against {matchPpda} for the match: {team} win it back faster.", "{team} are hunting the ball and winning it back quickly."),
        [("PressSurge", "es")] = new("{team} presiona arriba", "PPDA de {ppda10} en los últimos 10 minutos frente a {matchPpda} en el partido: {team} recupera antes.", "{team} va a por el balón y lo recupera rápido."),
        [("PressSurge", "fr")] = new("{team} presse plus haut", "PPDA de {ppda10} sur les 10 dernières minutes contre {matchPpda} sur le match : {team} récupère plus vite.", "{team} chasse le ballon et le récupère vite."),
        [("MomentumSwing", "en")] = new("Momentum shifts to {team}", "Momentum from {previousMomentum} to {momentum}: {team} now own the territory and the chances.", "The game has swung towards {team}."),
        [("MomentumSwing", "es")] = new("El partido gira hacia {team}", "El impulso pasa de {previousMomentum} a {momentum}: {team} domina territorio y ocasiones.", "El partido se ha puesto del lado de {team}."),
        [("MomentumSwing", "fr")] = new("La dynamique bascule vers {team}", "Dynamique de {previousMomentum} à {momentum} : {team} domine le terrain et les occasions.", "Le match a basculé du côté de {team}."),
        [("ChaosSpell", "en")] = new("Chaos on the pitch", "{turnoversPerMin} turnovers a minute, chaos index {chaos}. Nobody can keep the ball.", "It's end to end and nobody can keep the ball!"),
        [("ChaosSpell", "es")] = new("Caos en el campo", "{turnoversPerMin} pérdidas por minuto, índice de caos {chaos}. Nadie retiene el balón.", "¡Ida y vuelta, nadie consigue quedarse con el balón!"),
        [("ChaosSpell", "fr")] = new("Le chaos sur le terrain", "{turnoversPerMin} pertes par minute, indice de chaos {chaos}. Personne ne garde le ballon.", "Ça part dans tous les sens, personne ne garde le ballon !"),
        [("ControlSpell", "en")] = new("{team} in control", "Control index {control} over the last 5 minutes, {team_possession}% possession for the match.", "{team} are calling the shots right now."),
        [("ControlSpell", "es")] = new("{team} manda", "Índice de control {control} en los últimos 5 minutos, {team_possession}% de posesión en el partido.", "{team} lleva la batuta ahora mismo."),
        [("ControlSpell", "fr")] = new("{team} contrôle le match", "Indice de contrôle {control} sur les 5 dernières minutes, {team_possession} % de possession sur le match.", "{team} a les commandes en ce moment."),
        [("ElitePass", "en")] = new("{player} picks the lock", "Difficulty {difficulty100}/100 over {distanceM} m.", "What a pass from {player}!"),
        [("ElitePass", "es")] = new("{player} abre la defensa", "Dificultad {difficulty100}/100 en {distanceM} m.", "¡Vaya pase de {player}!"),
        [("ElitePass", "fr")] = new("{player} trouve la faille", "Difficulté {difficulty100}/100 sur {distanceM} m.", "Quelle passe de {player} !"),
    };

    private static readonly Dictionary<string, (string Ours, string Theirs, string Focus)> Framing = new()
    {
        ["en"] = ("Come on!", "Not what we wanted.", "{focus_name}: {focus_passes_completed} passes completed, {focus_xg} xG."),
        ["es"] = ("¡Vamos!", "No era lo que queríamos.", "{focus_name}: {focus_passes_completed} pases completados, {focus_xg} xG."),
        ["fr"] = ("Allez !", "Pas ce qu'on voulait.", "{focus_name} : {focus_passes_completed} passes réussies, {focus_xg} xG."),
    };

    private static readonly Dictionary<string, Dictionary<string, string>> Words = new()
    {
        ["en"] = new() { ["golden"] = "golden", ["tough"] = "tough", ["saved"] = "saved", ["off_target"] = "off target", ["blocked"] = "blocked" },
        ["es"] = new() { ["golden"] = "clarísima", ["tough"] = "difícil", ["saved"] = "parada del portero", ["off_target"] = "fuera", ["blocked"] = "bloqueada" },
        ["fr"] = new() { ["golden"] = "en or", ["tough"] = "difficile", ["saved"] = "arrêtée", ["off_target"] = "non cadrée", ["blocked"] = "contrée" },
    };

    public static string Write(string prompt)
    {
        var viewer = Section(prompt, "viewer");
        using var factsDoc = JsonDocument.Parse(Section(prompt, "facts"));
        var facts = FactSheet.ReadPromptFacts(factsDoc.RootElement);
        var kind = factsDoc.RootElement.GetProperty("moment").GetProperty("kind").GetString()!;
        using var viewerDoc = JsonDocument.Parse(viewer);
        var v = viewerDoc.RootElement;
        var lang = v.GetProperty("language").GetString()!;
        var persona = v.GetProperty("persona").GetString()!;

        string Fill(string template)
        {
            var text = template;
            foreach (var (name, element) in facts)
            {
                var value = element.ValueKind == JsonValueKind.Number
                    ? TemplateLocalizer.Format(element.GetDouble(), lang)
                    : element.ToString();
                text = text.Replace("{" + name + "}", value);
            }
            var words = Words[lang];
            var xg = facts.TryGetValue("xg", out var x) ? x.GetDouble() : 0;
            var difficulty = facts.TryGetValue("difficulty", out var d) ? d.GetDouble() : 0;
            var outcome = facts.TryGetValue("outcome", out var o) ? o.GetString() ?? "" : "";
            return text
                .Replace("{player}", Str(facts, "player_name", Str(facts, "team", "")))
                .Replace("{chance}", xg >= 0.3 ? words["golden"] : words["tough"])
                .Replace("{outcome}", words.GetValueOrDefault(outcome, outcome))
                .Replace("{difficulty100}", Math.Round(difficulty * 100).ToString("0"));
        }

        if (!Book.TryGetValue((kind, lang), out var lines))
            return JsonSerializer.Serialize(new HostDto(Fill("{team}"), ""));

        var body = persona is "analyst" or "metric_focus" ? lines.Analyst : lines.Casual;
        var (ours, theirs, focus) = Framing[lang];
        if (persona == "club_fan" && v.TryGetProperty("storyIsAboutTheirClub", out var mine) && mine.ValueKind != JsonValueKind.Null)
            body = (mine.GetBoolean() ? ours : theirs) + " " + body;
        if (persona == "player_focus" && facts.ContainsKey("focus_name"))
            body += " " + focus;

        return JsonSerializer.Serialize(new HostDto(Fill(lines.Headline), Fill(body)));
    }

    private static string Section(string prompt, string tag)
    {
        var start = prompt.IndexOf($"<{tag}>", StringComparison.Ordinal) + tag.Length + 2;
        var end = prompt.IndexOf($"</{tag}>", StringComparison.Ordinal);
        return prompt[start..end];
    }

    private static string Str(Dictionary<string, JsonElement> facts, string key, string fallback) =>
        facts.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : fallback;
}
