using System;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Efecto pasivo de una unidad (spec §7.3). Hoy sólo <see cref="PassiveType.ProduceResource"/>:
    /// produce <see cref="value"/> de <see cref="resource"/> por turno, resuelto en la fase
    /// PRODUCCIÓN. Modelado genérico para sumar tipos de pasivo sin tocar el modelo.
    /// </summary>
    [Serializable]
    public class PassiveEffect
    {
        public PassiveType passiveType;
        public ResourceType resource;
        public int value;

        public PassiveEffect() { }

        public PassiveEffect(PassiveType passiveType, ResourceType resource, int value)
        {
            this.passiveType = passiveType;
            this.resource = resource;
            this.value = value;
        }
    }
}
