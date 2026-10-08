namespace Studio.Engine;

/// <summary>One line of live play-by-play.</summary>
public sealed record CommentaryLine(
    string EventId,
    int Seq,
    double T,
    double Clock,
    int Minute,
    string Language,
    string Text,
    int Excitement,   // 0 calm .. 3 goal
    bool Voiced);     // read aloud: kick-offs, shots, goals, half-time, full-time

/// <summary>
/// The Host on commentary: live play-by-play written straight from the event data, in English,
/// Spanish or French. No model is involved, so every line is instant and every number exact.
/// Big moments are always called; routine play is called sparingly so the commentary breathes.
/// </summary>
public sealed class Commentator(MatchInfo info, string language)
{
    private const double MinGapSeconds = 10;   // match seconds between routine lines

    private readonly Phrases _p = Book.GetValueOrDefault(language, Book["en"]);
    private double _lastLineT = double.NegativeInfinity;

    public static readonly string[] Languages = ["en", "es", "fr"];

    public CommentaryLine? Call(MatchEvent e, MatchState state)
    {
        var who = Name(e.PlayerId);
        var team = info.Team(e.Team).Name;
        string Score() => $"{info.Home.Name} {state.HomeScore}–{state.AwayScore} {info.Away.Name}";

        (string Text, int Excitement, bool Voiced, bool Always)? call = e.Type switch
        {
            EventType.Kickoff when e.Clock is 0 or 2700 => (Pick(e.Period == 1 ? _p.KickOff : _p.SecondHalf, e, who, team), 1, true, true),
            EventType.Kickoff => (Pick(_p.Restart, e, who, team), 0, false, true),
            EventType.PeriodEnd => (string.Format(e.Period == 1 ? _p.HalfTime : _p.FullTime, Score()), 1, true, true),
            EventType.Shot => Shot(e, who, team, Score()),
            EventType.Substitution => (string.Format(_p.Substitution, team, who, Name(e.SubOffId)), 0, false, true),
            EventType.Sprint when e.PlayerSpeedKmh >= Thresholds.SprintSpeedKmh =>
                (string.Format(_p.Sprint, who, Km(e.PlayerSpeedKmh)), 1, false, false),
            EventType.Pass when e.Outcome == "complete" => Pass(e, who),
            EventType.Carry when e.Outcome == "complete" && EndsInBox(e) => (Pick(_p.IntoBox, e, who, team), 2, false, false),
            EventType.Tackle or EventType.Interception when InOwnThird(e) => (Pick(_p.BigTackle, e, who, team), 1, false, false),
            EventType.Foul => (string.Format(_p.Foul, who), 0, false, false),
            _ => null,
        };
        if (call is not { } c) return null;
        if (!c.Always && e.T - _lastLineT < MinGapSeconds) return null;
        _lastLineT = e.T;
        return new CommentaryLine(e.Id, e.Seq, e.T, e.Clock, e.Minute, language, c.Text, c.Excitement, c.Voiced);
    }

    private (string, int, bool, bool) Shot(MatchEvent e, string who, string team, string score)
    {
        var fast = e.BallSpeedKmh >= Thresholds.ShotSpeedKmh ? string.Format(_p.Struck, Km(e.BallSpeedKmh)) : "";
        return e.Outcome switch
        {
            "goal" => (string.Format(_p.Goal, who, team, score), 3, true, true),
            "saved" => (Pick(_p.Saved, e, who, team) + fast, 2, true, true),
            "blocked" => (Pick(_p.Blocked, e, who, team), 1, true, true),
            _ => (Pick(_p.Wide, e, who, team) + fast, 2, true, true),
        };
    }

    private (string, int, bool, bool)? Pass(MatchEvent e, string who)
    {
        var receiver = Name(e.ReceiverId);
        var (sx, sy) = Pitch.AttackingFrame(e.Team, e.X!.Value, e.Y!.Value);
        var (ex, ey) = Pitch.AttackingFrame(e.Team, e.EndX!.Value, e.EndY!.Value);
        if (Pitch.PassDifficulty(sx, sy, ex, ey, e.UnderPressure) >= Thresholds.ElitePassDifficulty)
            return (Pick(_p.GreatPass, e, who, receiver), 1, false, false);
        if (e.DistanceM >= 40 && ex > Pitch.Length / 2) return (Pick(_p.LongBall, e, who, receiver), 0, false, false);
        if (sx < 70 && ex >= 70) return (Pick(_p.FinalThird, e, who, receiver), 1, false, false);
        return null;
    }

    private static bool EndsInBox(MatchEvent e)
    {
        var (x, y) = Pitch.AttackingFrame(e.Team, e.EndX!.Value, e.EndY!.Value);
        return Pitch.InBox(x, y);
    }

    private static bool InOwnThird(MatchEvent e) =>
        e.X is { } x && Pitch.AttackingFrame(e.Team, x, e.Y!.Value).X <= 35;

    private string Name(string? id)
    {
        if (id is null) return "";
        foreach (var t in new[] { info.Home, info.Away })
            if (t.Starters.Concat(t.Bench).FirstOrDefault(p => p.Id == id) is { } p) return p.Name;
        return id;
    }

    private static string Km(double? v) => $"{v:0}";

    // Variety without randomness: the event's sequence number picks the phrasing, so replays match.
    private static string Pick(string[] options, MatchEvent e, string a, string b) =>
        string.Format(options[e.Seq % options.Length], a, b);

    private sealed record Phrases(
        string[] KickOff, string[] SecondHalf, string[] Restart, string HalfTime, string FullTime,
        string Goal, string[] Saved, string[] Wide, string[] Blocked, string Struck, string Substitution,
        string Sprint, string[] GreatPass, string[] LongBall, string[] FinalThird, string[] IntoBox,
        string[] BigTackle, string Foul);

    private static readonly Dictionary<string, Phrases> Book = new()
    {
        ["en"] = new(
            KickOff: ["{0} gets us under way for {1}.", "And we're off! {0} kicks off for {1}."],
            SecondHalf: ["The second half is under way.", "{0} restarts for the second half."],
            Restart: ["{0} restarts for {1}."],
            HalfTime: "Half-time: {0}.",
            FullTime: "Full-time! {0}.",
            Goal: "GOAL! {0} scores for {1}! {2}.",
            Saved: ["{0} shoots… saved!", "{0} tests the keeper… good save!", "Shot from {0}, and the keeper gathers."],
            Wide: ["{0} goes for goal… just wide.", "{0} pulls it wide!", "{0} shoots, over the bar."],
            Blocked: ["{0} lets fly, but it's blocked.", "{0} shoots, straight into a defender.", "Blocked! {0} can't find a way through."],
            Struck: " Struck at {0} km/h.",
            Substitution: "Change for {0}: {1} comes on for {2}.",
            Sprint: "{0} is flying, {1} km/h!",
            GreatPass: ["What a ball from {0} to {1}!", "Lovely pass, {0} finds {1}."],
            LongBall: ["Long ball from {0}, looking for {1}.", "{0} goes long towards {1}."],
            FinalThird: ["{0} finds {1} in the final third.", "{0} slips it into {1}."],
            IntoBox: ["{0} drives into the box!", "{0} is in the area!"],
            BigTackle: ["Big tackle from {0}!", "{0} wins it back for {1}."],
            Foul: "Foul by {0}. Free kick."),
        ["es"] = new(
            KickOff: ["{0} pone en marcha el partido para {1}.", "¡Arrancamos! Saca {0} para {1}."],
            SecondHalf: ["Comienza la segunda parte.", "{0} reanuda el juego en la segunda parte."],
            Restart: ["{0} saca de centro para {1}."],
            HalfTime: "Descanso: {0}.",
            FullTime: "¡Final del partido! {0}.",
            Goal: "¡GOOOL! ¡Marca {0} para {1}! {2}.",
            Saved: ["Dispara {0}… ¡paradón!", "{0} prueba al portero… ¡buena parada!", "Disparo de {0}, atrapa el portero."],
            Wide: ["{0} lo intenta… se marcha fuera.", "¡{0} la manda desviada!", "Dispara {0}, por encima del larguero."],
            Blocked: ["Remate de {0}, pero lo bloquean.", "{0} dispara y se topa con un defensa.", "¡Bloqueado! {0} no encuentra hueco."],
            Struck: " A {0} km/h.",
            Substitution: "Cambio en {0}: entra {1} por {2}.",
            Sprint: "¡{0} vuela, {1} km/h!",
            GreatPass: ["¡Qué pase de {0} a {1}!", "Gran envío, {0} encuentra a {1}."],
            LongBall: ["Balón largo de {0} buscando a {1}.", "{0} busca en largo a {1}."],
            FinalThird: ["{0} encuentra a {1} en el último tercio.", "{0} filtra para {1}."],
            IntoBox: ["¡{0} se mete en el área!", "¡{0} entra en el área!"],
            BigTackle: ["¡Gran entrada de {0}!", "{0} recupera para {1}."],
            Foul: "Falta de {0}. Tiro libre."),
        ["fr"] = new(
            KickOff: ["{0} donne le coup d'envoi pour {1}.", "C'est parti ! {0} engage pour {1}."],
            SecondHalf: ["La seconde période est lancée.", "{0} relance pour la seconde période."],
            Restart: ["{0} remet en jeu pour {1}."],
            HalfTime: "Mi-temps : {0}.",
            FullTime: "Coup de sifflet final ! {0}.",
            Goal: "BUT ! {0} marque pour {1} ! {2}.",
            Saved: ["{0} frappe… arrêt du gardien !", "{0} teste le gardien… belle parade !", "Frappe de {0}, captée par le gardien."],
            Wide: ["{0} tente sa chance… à côté.", "{0} manque le cadre !", "{0} frappe, au-dessus de la barre."],
            Blocked: ["Frappe de {0}, contrée.", "{0} frappe dans un défenseur.", "Contré ! {0} ne trouve pas l'ouverture."],
            Struck: " Frappée à {0} km/h.",
            Substitution: "Changement pour {0} : {1} remplace {2}.",
            Sprint: "{0} s'envole, {1} km/h !",
            GreatPass: ["Quelle passe de {0} pour {1} !", "Superbe ballon, {0} trouve {1}."],
            LongBall: ["Long ballon de {0} pour {1}.", "{0} cherche {1} dans la profondeur."],
            FinalThird: ["{0} trouve {1} dans le dernier tiers.", "{0} glisse le ballon à {1}."],
            IntoBox: ["{0} entre dans la surface !", "{0} est dans la surface !"],
            BigTackle: ["Gros tacle de {0} !", "{0} récupère pour {1}."],
            Foul: "Faute de {0}. Coup franc."),
    };
}
