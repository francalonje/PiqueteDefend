using System.Collections.Generic;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Estado completo de un jugador (spec §7.5). Mantiene HP, recursos, mano,
    /// unidades activas y statuses temporizados. No contiene reglas: las transiciones
    /// las maneja <see cref="GameEngine"/>. Los helpers de acá son consultas/mutaciones
    /// atómicas, espejo del simulador.
    /// </summary>
    public class PlayerState
    {
        public Faction faction;
        public int hp;
        public int dinero;
        public int fuerza;
        public int social;

        public readonly List<CardData> hand = new List<CardData>();
        public readonly List<UnitSlot> unitSlots = new List<UnitSlot>();
        public readonly List<StatusEffect> activeStatuses = new List<StatusEffect>();

        // ── Recursos ────────────────────────────────────────────────────────────

        public int GetResource(ResourceType r) => r switch
        {
            ResourceType.Dinero => dinero,
            ResourceType.Fuerza => fuerza,
            ResourceType.Social => social,
            _ => 0
        };

        /// <summary>Asigna un recurso, recortado a [0, max]. Los recursos nunca bajan de 0 (spec §5).</summary>
        public void SetResource(ResourceType r, int value, int max)
        {
            int clamped = value < 0 ? 0 : (value > max ? max : value);
            switch (r)
            {
                case ResourceType.Dinero: dinero = clamped; break;
                case ResourceType.Fuerza: fuerza = clamped; break;
                case ResourceType.Social: social = clamped; break;
            }
        }

        public void AddResource(ResourceType r, int delta, int max)
            => SetResource(r, GetResource(r) + delta, max);

        public bool CanAfford(CardData card)
            => dinero >= card.costDinero && fuerza >= card.costFuerza && social >= card.costSocial;

        public void Pay(CardData card)
        {
            dinero -= card.costDinero;
            fuerza -= card.costFuerza;
            social -= card.costSocial;
        }

        // ── Unidades ────────────────────────────────────────────────────────────

        public UnitSlot SlotFor(string unitId)
        {
            foreach (var s in unitSlots)
                if (s.unitData.id == unitId)
                    return s;
            return null;
        }

        public int UnitAttack()
        {
            int total = 0;
            foreach (var s in unitSlots)
                if (s.unitData.unitSubtype == UnitSubtype.Atacante)
                    total += s.count;
            return total;
        }

        public int UnitDefense()
        {
            int total = 0;
            foreach (var s in unitSlots)
                if (s.unitData.unitSubtype == UnitSubtype.Defensiva)
                    total += s.count;
            return total;
        }

        /// <summary>Producción aportada por las unidades Productoras, por recurso.</summary>
        public void AddUnitProduction(Dictionary<ResourceType, int> into)
        {
            foreach (var s in unitSlots)
                if (s.unitData.unitSubtype == UnitSubtype.Productora)
                    into[s.unitData.productionResource] += s.count;
        }
    }
}
