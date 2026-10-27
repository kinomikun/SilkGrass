using System;
using System.Numerics;

namespace SilkGrass.Cameras
{
    /// <summary>
    /// First-person fly camera: yaw/pitch look, movement along the view
    /// direction, its right vector and world up.
    ///
    /// Yaw 0 looks along +Z, yaw +90 deg along +X; positive pitch looks down.
    /// </summary>
    public class FreeFlyCamera
    {
        public Vector3 Position;
        public float Yaw;
        public float Pitch;

        public float MoveSpeed = 8f;
        public float SprintMultiplier = 6f;
        public float LookSensitivity = 0.0025f;

        /// <summary>Just under 90 deg, so the view never flips over the pole.</summary>
        public const float MaxPitch = 1.553f;

        public Matrix4x4 View { get; private set; } = Matrix4x4.Identity;
        public Matrix4x4 Projection { get; private set; } = Matrix4x4.Identity;

        /// <summary>The TAA sub-pixel offset applied to the projection this frame, in NDC.</summary>
        public Vector2 JitterOffset { get; private set; }

        public Vector3 Forward => new Vector3(
            MathF.Sin(Yaw) * MathF.Cos(Pitch),
            -MathF.Sin(Pitch),
            MathF.Cos(Yaw) * MathF.Cos(Pitch));

        public Vector3 Right => Vector3.Normalize(Vector3.Cross(Forward, Vector3.UnitY));

        public void Look(float deltaX, float deltaY)
        {
            Yaw -= deltaX * LookSensitivity;
            Pitch = Math.Clamp(Pitch + deltaY * LookSensitivity, -MaxPitch, MaxPitch);
        }

        /// <param name="move">x = right, y = up, z = forward; each -1..1.</param>
        public void Move(Vector3 move, bool sprint, float deltaTime)
        {
            Vector3 dir = Right * move.X + Vector3.UnitY * move.Y + Forward * move.Z;
            if (dir.LengthSquared() < 1e-8f) return;
            float speed = MoveSpeed * (sprint ? SprintMultiplier : 1f);
            Position += Vector3.Normalize(dir) * speed * deltaTime;
        }

        /// <summary>Rebuilds the view and an unjittered projection. Once per frame, before rendering.</summary>
        public void UpdateMatrices(float aspectRatio, float fovDeg, float nearClip, float farClip)
        {
            View = Matrix4x4.CreateLookAt(Position, Position + Forward, Vector3.UnitY);
            Projection = Matrix4x4.CreatePerspectiveFieldOfView(fovDeg * MathF.PI / 180f,
                MathF.Max(aspectRatio, 0.01f), nearClip, farClip);
            JitterOffset = Vector2.Zero;
        }

        /// <summary>
        /// Shifts the projection by a sub-pixel amount for TAA. jitterPixels is
        /// in render-target pixels.
        /// </summary>
        public void ApplyJitter(Vector2 jitterPixels, int targetWidth, int targetHeight)
        {
            JitterOffset = new Vector2(2f * jitterPixels.X / targetWidth, 2f * jitterPixels.Y / targetHeight);
            var p = Projection;
            p.M31 += JitterOffset.X;
            p.M32 += JitterOffset.Y;
            Projection = p;
        }
    }
}
