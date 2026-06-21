using System.Collections.Generic;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Estado completo de un jugador (spec §7.5). Recursos, mano, unidades en el tablero y
    /// statuses temporizados. No contiene reglas: las transiciones las maneja
    /// <see cref="GameEngine"/>; los helpers de acá son consultas/mutaciones atómicas.
    ///
    /// El jugador NO tiene HP propio: pierde cuando se queda sin unidades (spec §5).
    /// </summary>
    public sealed class PlayerState
    {
        public Faction faction;
        public int dinero;
        public int fuerza;
        public int social;

        public readonly List<CardData> hand = new List<CardData>();

        /// <summary>Slots de unidades, indexados por posición. <c>null</c> = slot vacío.</summary>
        public readonly UnitSlot[] unitSlots;

        public readonly List<StatusEffect> activeStatuses = new List<StatusEffect>();

        public PlayerState(int slotCount)
        {
            unitSlots = new UnitSlot[slotCount];
        }

        // ── Recursos ────────────────────────────────────────────────────────────

        public int GetResource(ResourceType r) => r switch
        {
            ResourceType.Dinero => dinero,
            ResourceType.Fuerza => fuerza,
            ResourceType.Social => social,
            _ => 0
        };

        /// <summary>Asigna un recurso, recortado a [0, max]. Los recursos nunca bajan de 0 (spec §3).</summary>
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
        {
            foreach (ResourceCost c in card.costs)
                if (GetResource(c.resource) < c.amount) return false;
            return true;
        }

        public void Pay(CardData card)
        {
            foreach (ResourceCost c in card.costs)
                SetResource(c.resource, GetResource(c.resource) - c.amount, int.MaxValue);
        }

        // ── Unidades ────────────────────────────────────────────────────────────

        public int AliveUnitCount()
        {
            int n = 0;
            foreach (UnitSlot s in unitSlots)
                if (s != null) n++;
            return n;
        }

        public bool HasAnyUnit()
        {
            foreach (UnitSlot s in unitSlots)
                if (s != null) return true;
            return false;
        }

        public int TotalUnitHp()
        {
            int total = 0;
            foreach (UnitSlot s in unitSlots)
                if (s != null) total += s.currentHp;
            return total;
        }

        /// <summary>Primer slot libre permitido por la unidad; -1 si no hay ninguno.</summary>
        public int FirstFreeAllowedSlot(UnitCardData unit)
        {
            for (int i = 0; i < unitSlots.Length; i++)
                if (unitSlots[i] == null && unit.AllowsSlot(i)) return i;
            return -1;
        }

        /// <summary>True si hay al menos un slot permitido (libre u ocupado) para reemplazar.</summary>
        public bool HasAnyAllowedSlot(UnitCardData unit)
        {
            for (int i = 0; i < unitSlots.Length; i++)
                if (unit.AllowsSlot(i)) return true;
            return false;
        }

        /// <summary>Producción aportada por las unidades, por recurso (spec §7.3).</summary>
        public void AddUnitProduction(Dictionary<ResourceType, int> into)
        {
            foreach (UnitSlot s in unitSlots)
            {
                if (s == null) continue;
                foreach (PassiveEffect p in s.AllPassives())
                    if (p.passiveType == PassiveType.ProduceResource)
                        into[p.resource] += p.value * s.count;
            }
        }
    }
}
