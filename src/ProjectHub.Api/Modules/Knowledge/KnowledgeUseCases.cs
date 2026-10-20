using System.Text.Json.Nodes;

namespace ProjectHub.Api.Modules.Knowledge;

/// <summary>One field of an insight card, e.g. "Hypothese". <c>Highlight</c> marks the field that carries the result.</summary>
public sealed record InsightField(string Key, string Label, bool Highlight = false);

/// <summary>A result an insight can have. <c>Tone</c>: positive, negative, active or neutral (colors in the UI and the galaxy).</summary>
public sealed record InsightOutcome(string Key, string Label, string Tone);

/// <summary>An article type that is an insight: its card fields and possible results. The first outcome is the one of a new article.</summary>
public sealed record InsightKind(string ArticleType, string Label, string PluralLabel, IReadOnlyList<InsightField> Fields, IReadOnlyList<InsightOutcome> Outcomes);

/// <summary>What a knowledge space is used for; it decides which insight kinds its articles offer (ADR 0023).</summary>
public sealed record KnowledgeUseCase(string Key, string Label, string Description, IReadOnlyList<InsightKind> Kinds);

/// <summary>
/// The fixed use cases of knowledge spaces (ADR 0023, DEC-045): general knowledge, experiments and software development.
/// Each one brings insight kinds with the fields of their card and their outcomes. The catalogue lives in code; the
/// client reads it from <c>GET /api/v1/knowledge/use-cases</c>.
/// </summary>
public static class KnowledgeUseCases
{
    public const string General = "general";
    public const string Experiment = "experiment";
    public const string Software = "software";

    public const int MaxConfidence = 4;

    public static readonly IReadOnlyList<string> Tones = ["positive", "negative", "active", "neutral"];

    public static readonly IReadOnlyList<KnowledgeUseCase> All =
    [
        new(General, "Allgemein", "Artikel, Anleitungen, Prozesse und Richtlinien.", []),
        new(Experiment, "Experiment", "Versuche mit Frage, Aufbau, Ergebnis und Erkenntnis.",
        [
            new("experiment", "Experiment", "Experimente",
                [new("question", "Frage"), new("setup", "Aufbau"), new("result", "Ergebnis"), new("insight", "Erkenntnis", Highlight: true)],
                [new("running", "Läuft", "active"), new("confirmed", "Bestätigt", "positive"), new("refuted", "Widerlegt", "negative"), new("inconclusive", "Unklar", "neutral")]),
        ]),
        new(Software, "Softwareentwicklung", "Spikes, Entscheidungen, Retro-Learnings, Vorfälle und Releases.",
        [
            new("spike", "Spike", "Spikes",
                [new("problem", "Problem"), new("approach", "Ansatz"), new("decision", "Entscheidung"), new("learning", "Learning", Highlight: true)],
                [new("running", "Läuft", "active"), new("adopted", "Übernommen", "positive"), new("discarded", "Verworfen", "negative")]),
            new("decision", "Entscheidung", "Entscheidungen",
                [new("context", "Kontext"), new("decision", "Entscheidung", Highlight: true), new("alternatives", "Alternativen"), new("consequences", "Folgen")],
                [new("proposed", "Vorgeschlagen", "active"), new("accepted", "Gilt", "positive"), new("superseded", "Abgelöst", "neutral")]),
            new("retro_learning", "Retro-Learning", "Retro-Learnings",
                [new("observation", "Beobachtung"), new("cause", "Ursache"), new("action", "Maßnahme"), new("learning", "Learning", Highlight: true)],
                [new("trying", "Wird ausprobiert", "active"), new("keep", "Beibehalten", "positive"), new("stop", "Nie wieder", "negative")]),
            new("incident", "Vorfall", "Vorfälle",
                [new("impact", "Auswirkung"), new("cause", "Ursache"), new("fix", "Behebung"), new("learning", "Learning", Highlight: true)],
                [new("open", "Offen", "active"), new("resolved", "Behoben", "positive")]),
            new("release", "Release", "Releases",
                [new("content", "Inhalt"), new("went_well", "Lief gut"), new("went_badly", "Lief schlecht"), new("learning", "Learning", Highlight: true)],
                [new("planned", "Geplant", "active"), new("released", "Veröffentlicht", "positive"), new("rolled_back", "Zurückgenommen", "negative")]),
        ]),
    ];

    public static readonly IReadOnlyList<string> Keys = All.Select(u => u.Key).ToList();

    /// <summary>The article types of all insight kinds.</summary>
    public static readonly IReadOnlyList<string> InsightTypes = All.SelectMany(u => u.Kinds).Select(k => k.ArticleType).Distinct().ToList();

    /// <summary>The insight kind of an article type, or null for ordinary articles.</summary>
    public static InsightKind? KindOf(string articleType) =>
        All.SelectMany(u => u.Kinds).FirstOrDefault(k => k.ArticleType == articleType);

    /// <summary>The empty card of a new insight article: one field per field of its kind.</summary>
    public static JsonObject InsightBlock(InsightKind kind) => new()
    {
        ["type"] = "insight",
        ["fields"] = new JsonArray(kind.Fields
            .Select(f => (JsonNode)new JsonObject { ["key"] = f.Key, ["label"] = f.Label, ["text"] = string.Empty })
            .ToArray()),
    };
}
