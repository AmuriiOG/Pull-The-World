using System.Collections.Generic;
using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// Every dynamic Rigidbody in the level, kept in one list.
    ///
    /// WorldRotator has to wake sleeping bodies on any rotation (see its comments for why), and
    /// that has to happen inside FixedUpdate while the level is turning - which is exactly the
    /// wrong place to be calling FindObjectsByType. Bodies register themselves on enable instead,
    /// so the wake pass is a walk over a short list and costs nothing.
    /// </summary>
    public static class DynamicRegistry
    {
        public static readonly List<Rigidbody> Bodies = new List<Rigidbody>(32);

        public static void Register(Rigidbody rb)
        {
            if (rb && !Bodies.Contains(rb)) Bodies.Add(rb);
        }

        public static void Unregister(Rigidbody rb)
        {
            if (rb) Bodies.Remove(rb);
        }

        /// <summary>Levels are torn down and rebuilt constantly; drop the corpses.</summary>
        public static void Prune()
        {
            for (int i = Bodies.Count - 1; i >= 0; i--)
                if (!Bodies[i]) Bodies.RemoveAt(i);
        }
    }
}
