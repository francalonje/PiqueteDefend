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
        Unidad,
        Equipo
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
        ApplyStatus,
        MoveUnit,
        SwapUnits
    }

    /// <summary>Sobre qué jugador recae un efecto.</summary>
    public enum TargetType
    {
        Self,
        Opponent
    }

    /// <summary>
    /// Qué hace un <see cref="StatusEffect"/> (spec §7.7). Los dos primeros viven en el jugador
    /// (modelo fire-on-expiry); los demás viven por unidad (modelo active-while-present).
    /// </summary>
    public enum StatusType
    {
        SkipProduction,
        DoubleProduction,
        Poison,
        Stun,
        Furia,
        Desmoralizar
    }

    /// <summary>
    /// Marco de referencia de un <see cref="UnitAttack"/> / pasiva dirigida (spec §7.2/§7.3).
    /// Absolute: el patrón son slots del tablero objetivo (0–5). Relative: offsets desde el slot
    /// de origen (0 = enfrentado / sí mismo).
    /// </summary>
    public enum AttackReference
    {
        Absolute,
        Relative
    }

    /// <summary>
    /// Qué tablero afecta un <see cref="UnitAttack"/> (spec §7.2): daña al rival o cura a los aliados.
    /// </summary>
    public enum AttackEffect
    {
        DamageEnemies,
        HealAllies
    }

    /// <summary>Tipo de efecto pasivo de una unidad (spec §7.3).</summary>
    public enum PassiveType
    {
        ProduceResource,
        Regeneration,
        AuraDamage,
        Retaliate,
        TurnDamage,
        TurnStatus
    }

    /// <summary>Sobre qué tablero recae una pasiva dirigida (spec §7.3). Reemplaza al viejo PassiveScope.</summary>
    public enum PassiveTarget
    {
        Self,
        Allies,
        Enemies
    }

    /// <summary>Stat modificable por equipo (spec §7.1).</summary>
    public enum StatType
    {
        MaxHp,
        Damage
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
