namespace PiqueteDefend.Core
{
    /// <summary>Las dos facciones enfrentadas.</summary>
    public enum Faction
    {
        Manifestantes,
        Policias
    }

    /// <summary>Tipo de carta: acción de un solo uso o unidad persistente.</summary>
    public enum CardType
    {
        Accion,
        Unidad
    }

    /// <summary>Subtipo de una carta de unidad. Define su efecto pasivo por turno.</summary>
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

    /// <summary>Qué hace un <see cref="CardEffect"/> al resolverse.</summary>
    public enum CardEffectType
    {
        ModifyHP,
        ModifyResource,
        RemoveUnit,
        ApplyStatus
    }

    /// <summary>Sobre quién recae un efecto.</summary>
    public enum TargetType
    {
        Self,
        Opponent
    }

    /// <summary>Qué hace un <see cref="StatusEffect"/> al dispararse.</summary>
    public enum StatusType
    {
        SkipProduction,
        DoubleProduction
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

    /// <summary>Condición por la que terminó la partida.</summary>
    public enum WinCondition
    {
        KO,
        HegemoniaSocial,
        PoderEconomico,
        Timeout
    }
}
