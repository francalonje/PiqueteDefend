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
    /// Cómo un <see cref="UnitAttack"/> / pasiva dirigida elige sus objetivos sobre el tablero
    /// indicado (spec §6/§7.2/§7.3). El targeting está <b>anclado a la formación</b> (a la unidad
    /// ocupada, nunca a un slot fijo): el frente del tablero objetivo es el extremo cercano al rival
    /// (índices altos), el fondo el lejano (índices bajos).
    /// <list type="bullet">
    /// <item><b>Frontmost</b>: la unidad más adelantada ocupada + (count−1) espacios consecutivos
    /// hacia el fondo. count=1 = "al de adelante de todo" (nunca whiffea). count&gt;1 = penetrante
    /// (los espacios profundos vacíos whiffean).</item>
    /// <item><b>Backmost</b>: la unidad más atrasada ocupada + (count−1) espacios hacia el frente
    /// (excepción "pega al fondo").</item>
    /// <item><b>Any</b>: el jugador elige <c>count</c> unidades cualesquiera (snipe; en pasivas el
    /// motor resuelve determinista).</item>
    /// <item><b>All</b>: todas las unidades del objetivo (AoE).</item>
    /// <item><b>Adjacent</b>: las vecinas (±1) de la unidad fuente (auras).</item>
    /// <item><b>Self</b>: la propia unidad fuente.</item>
    /// </list>
    /// <b>Invariante anti-deadlock:</b> Frontmost/Backmost/Any/All siempre resuelven a ≥1 unidad
    /// ocupada si el tablero objetivo no está vacío, así que un ataque de daño nunca se cancela
    /// contra un tablero con unidades.
    /// </summary>
    public enum TargetMode
    {
        Frontmost,
        Backmost,
        Any,
        All,
        Adjacent,
        Self
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
        TurnStatus,

        /// <summary>
        /// Death-rattle (spec §7.3): se dispara cuando la unidad muere por <b>cualquier</b> fuente
        /// (ataque, Poison, ModifyHP de carta, muerte súbita), antes de liberar el slot. Reusa el
        /// payload dirigido (<c>target</c>/<c>mode</c>/<c>count</c>): con <c>status</c> aplica un
        /// estado (ej. el Jubilado mártir → Furia a aliados adyacentes); sin <c>status</c>, <c>value</c>
        /// de daño directo (ej. explosión a enemigos). NO lo dispara <c>RemoveUnit</c> (remoción ≠ muerte).
        /// </summary>
        OnDeath,

        /// <summary>
        /// Blindaje (spec §7.3): reduce en <c>value</c> el daño de cada <b>ataque de unidad</b> recibido
        /// (piso 0). NO mitiga el daño directo (Poison, TurnDamage, ModifyHP de carta, OnDeath ni muerte
        /// súbita, que "ignora defensas", §5.1). Identidad defensiva de Policías (escudo antimotín).
        /// </summary>
        Armor,

        /// <summary>
        /// Chorro / empuje (spec §7.3): tras resolver el ataque de la unidad que la porta, cada objetivo
        /// sobreviviente es <b>empujado al slot libre más al fondo</b> (menor índice) de su formación;
        /// no-op si no hay lugar más atrás. Reposiciona al rival (rompe su formación). Ignora
        /// <c>allowedSlots</c> (es involuntario, como SwapUnits). <c>value</c> sin uso. Identidad de Policías (carro hidrante).
        /// </summary>
        PushBack
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
