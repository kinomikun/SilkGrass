using System;

namespace SilkGrass.Grass
{
    /// <summary>
    /// Grass level of detail: the capacity rings that hold the candidate
    /// blades, and the continuous curves the cull pass allocates them by.
    ///
    /// density(d) is one global curve of blades per square metre over the
    /// distance from the field centre. Every ring evaluates it in
    /// grass_cull.comp and supplies blades through a ladder: denser (smaller)
    /// rings supply first and fade out at their own edge, so the rendered
    /// density is density(d) at every distance with no seams between rings.
    ///
    /// width(d) is derived, not set: the eye judges COVERAGE (density x
    /// width), so blades are as wide as it takes to hold the coverage target
    /// at the density the rings actually supply. Lowering the density widens
    /// the blades instead of thinning the field.
    ///
    /// The methods below are CPU mirrors of the GLSL in grass_cull.comp; keep
    /// the two in sync.
    /// </summary>
    public class GrassLodSettings
    {
        // --- Density curve ---
        public const float DefaultNearDensity = 20f;
        public const float DefaultTrailBoostDensity = 8f;

        public float NearDensity = DefaultNearDensity; // blades per square metre at the centre
        public float PlateauRadius = 80f;              // density stays flat out to here
        public float FalloffExponent = 1.1f;           // power-law falloff beyond the plateau
        public float FarFloor = 0.1f;                  // minimum density until MaxDistance
        public float MaxDistance = 1300f;              // grass horizon; fades out over the last FadeOutWidth metres
        public const float FadeOutWidth = 80f;

        // --- Coverage target (width is derived from it) ---
        public float WidthNear = 1f;              // blade width multiplier at full density
        public float CoverageFar = 1f;            // coverage at the horizon relative to the centre
        public float CoverageFalloffStart = 300f; // coverage easing begins here
        public float WidthMax = 15f;              // safety clamp on the derived width

        // --- Mesh buckets ---
        // Survivors are routed into one of four (mesh, feature) buckets by
        // distance, dithered per blade so no radius switches all at once.
        // Bucket 0 must stay inside the near trample sim (InnerFieldSize / 2
        // including the dither) and bucket 1 inside the trail sim.
        public float MeshNearDistance = 135f; // high mesh + full physics ends
        public float MeshMidDistance = 310f;  // trail physics + per-fragment colour ends
        public float MeshFarDistance = 800f;  // medium mesh ends, flat rectangle begins
        public float MeshDither = 30f;

        /// <summary>Extra blades per square metre where the trail sim shows trampled grass.</summary>
        public float TrailBoostDensity = DefaultTrailBoostDensity;

        // --- Capacity rings ---
        // Capacities (blades per square metre) and sizes of the four rings of
        // candidate blades. The inner ring has headroom for the near density
        // plus the trail boost. Changing these needs a field rebuild.
        public float InnerDensity = DefaultNearDensity + DefaultTrailBoostDensity;
        public float InnerFieldSize = 300f;
        public float MiddleDensity = 5f;
        public float MiddleFieldSize = 650f;
        public float FarDensity = 2.5f;
        public float FarFieldSize = 1200f;
        public float DistantDensity = 0.5f;
        public float DistantFieldSize = 2600f;
        /// <summary>Width of the band over which a ring's capacity fades out at its edge.</summary>
        public float TransitionWidth = 40f;

        public int RingCount(int ring) => (int)(RingDensity(ring) * RingSize(ring) * RingSize(ring));

        public float RingDensity(int ring) => ring switch
        {
            0 => InnerDensity, 1 => MiddleDensity, 2 => FarDensity, _ => DistantDensity,
        };

        public float RingSize(int ring) => ring switch
        {
            0 => InnerFieldSize, 1 => MiddleFieldSize, 2 => FarFieldSize, _ => DistantFieldSize,
        };

        /// <summary>
        /// Canopy area each bucket's mesh fills within its width x height
        /// quad (the medium blade's pointed tip covers least, the rectangle
        /// most). The cull pass scales width by MeshArea[0] / MeshArea[bucket]
        /// so the canopy stays continuous across the mesh switches.
        /// GLSL mirror: MESH_AREA_COMP in grass_cull.comp.
        /// </summary>
        public static readonly float[] MeshArea = { 0.80f, 0.70f, 0.70f, 0.95f };

        public float DensityAt(float d)
        {
            float rho = NearDensity * MathF.Pow(MathF.Max(d, PlateauRadius) / PlateauRadius, -FalloffExponent);
            rho = MathF.Max(rho, FarFloor);
            rho *= 1f - SmoothStep(MaxDistance - FadeOutWidth, MaxDistance, d);
            return rho;
        }

        /// <summary>Total density the rings can supply at this distance, edge fades included.</summary>
        public float EnvelopeAt(float d)
        {
            float tw = TransitionWidth, sum = 0f;
            for (int ring = 0; ring < 4; ring++)
                sum += RingDensity(ring) * RingFade(RingSize(ring) * 0.5f - tw, tw, d);
            return sum;
        }

        /// <summary>The density that renders: the target clamped to the supply.</summary>
        public float SuppliedDensityAt(float d) => MathF.Min(DensityAt(d), EnvelopeAt(d));

        public int BucketAt(float d) =>
            d < MeshNearDistance ? 0 : d < MeshMidDistance ? 1 : d < MeshFarDistance ? 2 : 3;

        /// <summary>Blade width multiplier at this distance, mesh-area compensation included.</summary>
        public float WidthAt(float d)
        {
            float keep = Mix(1f, CoverageFar, SmoothStep(CoverageFalloffStart, MaxDistance, d));
            float w = WidthNear * (NearDensity / MathF.Max(SuppliedDensityAt(d), 1e-3f)) * keep;
            w = Math.Clamp(w, WidthNear, WidthMax);
            return w * (MeshArea[0] / MeshArea[BucketAt(d)]);
        }

        /// <summary>Rendered canopy coverage relative to the centre (1 = as dense as at the centre).</summary>
        public float CoverageAt(float d)
        {
            float nearCoverage = NearDensity * WidthNear * MeshArea[0];
            return nearCoverage > 0f
                ? SuppliedDensityAt(d) * WidthAt(d) * MeshArea[BucketAt(d)] / nearCoverage
                : 0f;
        }

        private static float RingFade(float start, float width, float d) => 1f - SmoothStep(start, start + width, d);

        private static float Mix(float a, float b, float t) => a + (b - a) * t;

        private static float SmoothStep(float e0, float e1, float x)
        {
            float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
            return t * t * (3f - 2f * t);
        }
    }
}
