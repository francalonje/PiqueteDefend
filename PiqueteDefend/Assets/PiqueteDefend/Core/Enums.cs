namespace PiqueteDefend.Core
{
    /// <summary>Las dos facciones enfrentadas.</summary>
    public enum Faction
    {
        Manifestantes,
        Policias
    }

    /// <summary>Tipo de carta. Derivado de la subclase concreta de <see cref="CardData"/>.</summary>
    public enum CardType
    {
        Accion,
        Unidad
        // [FUTURO] Equipo
    }

    /// <summary>
    /// Subtipo temático de una unidad. Punto de extensión (spec §7.1): hoy no afecta la lógica
    /// (el comportamiento real lo dan maxHp / attack / passiveEffects).
    /// </summary>
    public enum UnitSubtype
    {
        Atacante,
        Defensiva,
        Productora
    }

    /// <summary>Los tres recursos que maneja cada jugador.</summary>
    public enum ResourceType
    {
        Dinero,
        Fuerza,
        Social
    }

    /// <summary>Qué hace un <see cref="CardEffect"/> al resolverse (spec §7.6).</summary>
    public enum CardEffectType
    {
        ModifyHP,
        ModifyResource,
        RemoveUnit,
        ApplyStatus
    }

    /// <summary>Sobre qué jugador recae un efecto.</summary>
    public enum TargetType
    {
        Self,
        Opponent
    }

    /// <summary>Qué hace un <see cref="StatusEffect"/> al activarse (spec §7.7).</summary>
    public enum StatusType
    {
        SkipProduction,
        DoubleProduction
    }

    /// <summary>
    /// Marco de referencia de un <see cref="UnitAttack"/> (spec §7.2).
    /// Absolute: el patrón son slots del oponente (0–5). Relative: offsets desde el slot
    /// del atacante (0 = enfrentado).
    /// </summary>
    public enum AttackReference
    {
        Absolute,
        Relative
    }

    /// <summary>Tipo de efecto pasivo de una unidad (spec §7.3).</summary>
    public enum PassiveType
    {
        ProduceResource
        // [FUTURO] boost a vecinas, etc.
    }

    /// <summary>
    /// Categoría visual/temática de una carta de acción. No afecta la lógica:
    /// la lógica real siempre la define la lista de <see cref="CardEffect"/>.
    /// </summary>
    public enum ActionCategory
    {
        Ataque,
        Defensa,
        Sabotaje,
        Boost,
        EfectoEspecial
    }

    /// <summary>Condición por la que terminó la partida (spec §5).</summary>
    public enum WinCondition
    {
        KO,       // el oponente perdió su última unidad
        Timeout,  // se alcanzó maxTurns → desempate determinista
        Draw      // muerte simultánea: ambos perdieron su última unidad a la vez
    }
}
