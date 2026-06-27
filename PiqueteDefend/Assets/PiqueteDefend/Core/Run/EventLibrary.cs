using System.Collections.Generic;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Fuente de verdad de los eventos del acto 1 (spec §17.6), en código (espejo de
    /// <see cref="EncounterLibrary"/>/<see cref="RelicLibrary"/>). Eventos "elegí tu premio" con flavor
    /// político-argento. Valores <b>rough</b>, a iterar por playtest ([[feedback-playtest-driven]]).
    /// </summary>
    public static class EventLibrary
    {
        public static List<EventDefinition> BuildActo1Pool()
        {
            return new List<EventDefinition>
            {
                new EventDefinition("aporte_base", "Aporte de la base",
                    "Pasan la gorra en la asamblea. Vos decidís en qué cae la diferencia.",
                    new EventChoice("Te quedás con la recaudación (+oro).",
                        new EventOutcome(EventOutcome.Kind.Gold, 25)),
                    new EventChoice("La invertís en sumar gente (una carta).",
                        new EventOutcome(EventOutcome.Kind.AddRandomCard))),

                new EventDefinition("el_contacto", "El contacto",
                    "Un puntero te hace una seña desde la otra vereda. Tiene algo para ofrecer.",
                    new EventChoice("Le aceptás el fierro (una reliquia).",
                        new EventOutcome(EventOutcome.Kind.Relic)),
                    new EventChoice("Le pedís un sobre con guita (+oro).",
                        new EventOutcome(EventOutcome.Kind.Gold, 20))),

                new EventDefinition("la_volqueta", "La volqueta",
                    "Tiraron de todo en una volqueta del centro. Hay para revolver.",
                    new EventChoice("Encontrás algo útil (una carta).",
                        new EventOutcome(EventOutcome.Kind.AddRandomCard)),
                    new EventChoice("Encontrás una joya (una reliquia).",
                        new EventOutcome(EventOutcome.Kind.Relic))),
            };
        }
    }
}
