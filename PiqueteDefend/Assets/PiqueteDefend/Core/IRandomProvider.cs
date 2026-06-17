using System;
using System.Collections.Generic;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Abstracción de aleatoriedad. El núcleo nunca usa Random directo: recibe un
    /// proveedor, para que los tests sean deterministas y reproducibles.
    /// </summary>
    public interface IRandomProvider
    {
        /// <summary>Entero en [0, maxExclusive).</summary>
        int Next(int maxExclusive);

        /// <summary>Elemento aleatorio de una lista no vacía.</summary>
        T Choice<T>(IReadOnlyList<T> list);
    }

    /// <summary>Implementación por defecto sobre <see cref="System.Random"/>, con semilla opcional.</summary>
    public sealed class SystemRandomProvider : IRandomProvider
    {
        private readonly Random _random;

        public SystemRandomProvider() => _random = new Random();
        public SystemRandomProvider(int seed) => _random = new Random(seed);

        public int Next(int maxExclusive) => _random.Next(maxExclusive);

        public T Choice<T>(IReadOnlyList<T> list)
        {
            if (list == null || list.Count == 0)
                throw new ArgumentException("No se puede elegir de una lista vacía.", nameof(list));
            return list[_random.Next(list.Count)];
        }
    }
}
