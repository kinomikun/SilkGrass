using System;
using System.Numerics;

namespace SilkGrass.Graphics
{
    /// <summary>
    /// The six frustum planes (left, right, bottom, top, near, far) of a
    /// view-projection, normalized so a plane's value at a point is a distance
    /// in world units. Each plane is (A, B, C, D) with Ax + By + Cz + D >= 0
    /// on the inside.
    /// </summary>
    public sealed class FrustumCuller
    {
        private readonly Vector4[] _planes = new Vector4[6];

        public Vector4[] Planes => _planes;

        /// <summary>Extracts the planes (Gribb/Hartmann). Once per frame, after the camera moved.</summary>
        public void Update(Matrix4x4 view, Matrix4x4 projection)
        {
            Matrix4x4 m = view * projection;
            _planes[0] = new Vector4(m.M14 + m.M11, m.M24 + m.M21, m.M34 + m.M31, m.M44 + m.M41);
            _planes[1] = new Vector4(m.M14 - m.M11, m.M24 - m.M21, m.M34 - m.M31, m.M44 - m.M41);
            _planes[2] = new Vector4(m.M14 + m.M12, m.M24 + m.M22, m.M34 + m.M32, m.M44 + m.M42);
            _planes[3] = new Vector4(m.M14 - m.M12, m.M24 - m.M22, m.M34 - m.M32, m.M44 - m.M42);
            _planes[4] = new Vector4(m.M14 + m.M13, m.M24 + m.M23, m.M34 + m.M33, m.M44 + m.M43);
            _planes[5] = new Vector4(m.M14 - m.M13, m.M24 - m.M23, m.M34 - m.M33, m.M44 - m.M43);

            for (int i = 0; i < 6; i++)
            {
                float length = MathF.Sqrt(_planes[i].X * _planes[i].X + _planes[i].Y * _planes[i].Y + _planes[i].Z * _planes[i].Z);
                if (length > 0.0001f) _planes[i] /= length;
            }
        }
    }
}
