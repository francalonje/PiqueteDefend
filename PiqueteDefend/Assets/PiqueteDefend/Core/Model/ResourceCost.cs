using System;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Un costo en un recurso (spec §7.1). Las cartas llevan una <c>List&lt;ResourceCost&gt;</c>:
    /// hoy siempre con una entrada, pero modelado como lista para soportar costos multi-recurso
    /// sin tocar el modelo.
    /// </summary>
    [Serializable]
    public class ResourceCost
    {
        public ResourceType resource;
        public int amount;

        public ResourceCost() { }

        public ResourceCost(ResourceType resource, int amount)
        {
            this.resource = resource;
            this.amount = amount;
        }
    }
}
