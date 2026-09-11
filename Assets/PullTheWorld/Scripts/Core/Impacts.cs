using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// How hard did two things actually hit? Shared by the ball and the props so both answer the
    /// same way.
    ///
    /// The answer is the relative velocity ALONG THE CONTACT NORMAL - the speed at which the two
    /// surfaces closed - not the relative velocity's magnitude. The magnitude includes the rolling
    /// speed, and the level is a compound body of one box collider per block, so a ball rolling
    /// along a flat row raises OnCollisionEnter at every seam. Measured by magnitude each seam was
    /// a 3-6 m/s "impact" five times a second: a full thud, a world shake, a haptic tap and a
    /// flash on the orb, heard as a strange knocking under the music. Measured along the normal
    /// the same seams are 0.7-1.8 m/s and fall under every threshold, while a real landing keeps
    /// its full value.
    /// </summary>
    public static class Impacts
    {
        public static float ClosingSpeed(Collision c)
        {
            if (c.contactCount == 0) return c.relativeVelocity.magnitude;
            Vector3 n = c.GetContact(0).normal;
            return Mathf.Abs(Vector3.Dot(c.relativeVelocity, n));
        }
    }
}
