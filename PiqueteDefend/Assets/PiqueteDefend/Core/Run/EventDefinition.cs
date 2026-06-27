using System.Collections.Generic;

namespace PiqueteDefend.Core
{
    /// <summary>Qué hace una opción de evento (spec §17.6). Resultados simples que pegan en estado de
    /// run (oro / reliquia / carta), sin tocar el motor. Sin downside por ahora (no hay HP de run, §4):
    /// los eventos son "elegí tu premio"; el riesgo/recompensa real queda para más adelante.</summary>
    public sealed class EventOutcome
    {
        public enum Kind { Gold, Relic, AddRandomCard }

        public Kind kind;
        public int amount;   // para Gold (puede ser negativo); ignorado por los demás

        public EventOutcome(Kind kind, int amount = 0)
        {
            this.kind = kind;
            this.amount = amount;
        }
    }

    /// <summary>Una opción del evento: un texto y sus resultados (spec §17.6).</summary>
    public sealed class EventChoice
    {
        public string text;
        public List<EventOutcome> outcomes = new List<EventOutcome>();

        public EventChoice(string text, params EventOutcome[] outcomes)
        {
            this.text = text;
            if (outcomes != null) this.outcomes.AddRange(outcomes);
        }
    }

    /// <summary>
    /// Evento narrativo con decisión (spec §17.6): título + cuerpo (flavor argento) + opciones. Es
    /// <b>data</b>; <see cref="EventLibrary"/> arma el pool en código y <see cref="RunManager.ResolveEvent"/>
    /// aplica la opción elegida al <see cref="RunState"/>.
    /// </summary>
    public sealed class EventDefinition
    {
        public string id;
        public string title;
        public string body;
        public List<EventChoice> choices = new List<EventChoice>();

        public EventDefinition(string id, string title, string body, params EventChoice[] choices)
        {
            this.id = id;
            this.title = title;
            this.body = body;
            if (choices != null) this.choices.AddRange(choices);
        }
    }
}
