using System;
using System.Numerics;

namespace SilkGrass.Grass
{
    /// <summary>
    /// CPU pre-cull for grass_cull.comp. Every ring's instance buffer is
    /// sorted into a CellsPerSide x CellsPerSide grid (SortIntoCells); each
    /// frame CullRing lists the cells that can hold a surviving blade, and the
    /// cull pass only spends compute threads on those. The rest of the ring
    /// (behind the camera, off to the sides, in the corners past its reach)
    /// is never dispatched.
    ///
    /// A cell is skipped only when the shader would reject every blade in it.
    /// The two tests here are the shader's own - no supply or demand at that
    /// distance, and the frustum - run on the box of everywhere the cell's
    /// blades can be (its square, at any terrain height within the bound),
    /// with some slack. grass_cull.comp still tests every dispatched blade, so
    /// the survivors are exactly the same as without the pre-cull.
    ///
    /// The list is ordered front to back from the camera, so each bucket's
    /// blades come out roughly nearest first and the depth test rejects the
    /// hidden ones before they are shaded. The order matters: the buckets
    /// inherit it, and listing the same cells back to front measured a third
    /// more time in the near grass draw.
    /// </summary>
    public sealed class GrassCellCuller
    {
        public const int CellsPerSide = 32;
        public const int CellCount = CellsPerSide * CellsPerSide;
        public const int RingCount = 4;

        /// <summary>Threads per compute work group: local_size_x in grass_cull.comp.</summary>
        public const int GroupSize = 256;

        /// <summary>uints one ring takes in <see cref="Cells"/>: (first instance, count) per cell.</summary>
        public const int RingStride = CellCount * 2;

        // Metres of slack around every tile and on the frustum margin. The
        // shader wraps and tests blades in float32, this class in double; half
        // a metre covers that rounding out to hundreds of kilometres from the
        // origin, and a blade sitting exactly on a cell border.
        private const double Slack = 0.5;

        /// <summary>
        /// Per ring, back to back: (first instance, instance count) for each
        /// listed cell. Uploaded as the CullCells uniform block, one
        /// RingStride-sized range per ring.
        /// </summary>
        public readonly uint[] Cells = new uint[RingCount * RingStride];

        private readonly int[] _listed = new int[RingCount];

        /// <summary>How many cells CullRing listed for this ring.</summary>
        public int ListedCells(int ring) => _listed[ring];

        // A wrap window can cut into one more tile than there are cells per
        // axis, two when the slack reaches the next tile border.
        private const int MaxTiles = CellsPerSide + 2;

        // Per-axis tile data for the ring being culled. A tile is one image of
        // a cell under the toroidal wrap, clipped to the wrap window.
        private readonly double[] _nearSqX = new double[MaxTiles];
        private readonly double[] _nearSqZ = new double[MaxTiles];
        private readonly double[] _planeX = new double[6 * MaxTiles];
        private readonly double[] _planeZ = new double[6 * MaxTiles];
        private readonly double[] _planeW = new double[6];
        private readonly int[] _cellX = new int[MaxTiles];
        private readonly int[] _cellZ = new int[MaxTiles];

        // Cells already listed this call (a cell on the wrap seam shows up as
        // two tiles)
        private readonly int[] _listedStamp = new int[CellCount];
        private int _stamp;

        // State of the CullRing call in progress
        private int[] _cellStart;
        private int _ringBase;
        private int _count;
        private double _reachSq;
        private bool _useFrustum;

        /// <summary>
        /// Sorts a ring's packed instances (3 floats each: X, Z, scale) into
        /// grid cells, cell = cellZ * CellsPerSide + cellX. Returns the sorted
        /// copy; cellStart[c] .. cellStart[c + 1] is cell c's instance range.
        /// </summary>
        public static float[] SortIntoCells(float[] instances, float fieldSize, out int[] cellStart)
        {
            int count = instances.Length / 3;
            var cellOf = new ushort[count];
            cellStart = new int[CellCount + 1];
            for (int i = 0; i < count; i++)
            {
                int cell = CellCoord(instances[i * 3 + 1], fieldSize) * CellsPerSide
                         + CellCoord(instances[i * 3], fieldSize);
                cellOf[i] = (ushort)cell;
                cellStart[cell + 1]++;
            }
            for (int c = 0; c < CellCount; c++)
                cellStart[c + 1] += cellStart[c];

            var next = new int[CellCount];
            Array.Copy(cellStart, next, CellCount);
            var sorted = new float[instances.Length];
            for (int i = 0; i < count; i++)
            {
                int dst = next[cellOf[i]]++ * 3;
                sorted[dst] = instances[i * 3];
                sorted[dst + 1] = instances[i * 3 + 1];
                sorted[dst + 2] = instances[i * 3 + 2];
            }
            return sorted;
        }

        private static int CellCoord(float v, float fieldSize)
        {
            int c = (int)MathF.Floor((v + fieldSize * 0.5f) / (fieldSize / CellsPerSide));
            return Math.Clamp(c, 0, CellsPerSide - 1);
        }

        /// <summary>
        /// Work groups the cull pass runs per listed cell: enough for the
        /// fullest cell, so a thread finds its cell from its work group index.
        /// </summary>
        public static int GroupsPerCell(int[] cellStart)
        {
            int largest = 0;
            for (int c = 0; c < CellCount; c++)
                largest = Math.Max(largest, cellStart[c + 1] - cellStart[c]);
            return Math.Max(1, (largest + GroupSize - 1) / GroupSize);
        }

        /// <summary>Lists the cells of one ring that can hold a surviving blade.</summary>
        /// <param name="builtFieldSize">Field size the cells were sorted for.</param>
        /// <param name="fieldSize">The ring's wrap period this frame (the shader's FieldSize).</param>
        /// <param name="center">World XZ the ring wraps around (the field centre).</param>
        /// <param name="camera">Camera XZ, for the front-to-back order.</param>
        /// <param name="keepReach">No blade survives at or beyond this distance from the centre.</param>
        /// <param name="frustumPlanes">The cull pass's six planes; null when frustum culling is off.</param>
        /// <param name="frustumMargin">The shader's FrustumMargin.</param>
        /// <param name="heightBound">The shader's TerrainHeightBound: no blade roots above or below it.</param>
        public void CullRing(int ring, int[] cellStart, float builtFieldSize, float fieldSize,
            Vector2 center, Vector2 camera, float keepReach,
            Vector4[] frustumPlanes, float frustumMargin, float heightBound)
        {
            _cellStart = cellStart;
            _ringBase = ring * RingStride;
            _count = 0;
            if (++_stamp == int.MaxValue)
            {
                Array.Clear(_listedStamp);
                _stamp = 1;
            }

            // The cells only line up with the wrap while the ring still has
            // the size it was built for (the panel can change the size ahead
            // of the rebuild). Otherwise - and for a field too small for the
            // slack, or a centre no tile maths survives - every cell is listed.
            double cellSize = (double)fieldSize / CellsPerSide;
            bool finite = MathF.Abs(center.X) < 1e9f && MathF.Abs(center.Y) < 1e9f
                       && MathF.Abs(camera.X) < 1e9f && MathF.Abs(camera.Y) < 1e9f;
            if (fieldSize != builtFieldSize || !(cellSize > 4.0 * Slack) || !finite)
            {
                for (int cell = 0; cell < CellCount; cell++) List(cell);
                _listed[ring] = _count;
                return;
            }

            _reachSq = (double)keepReach * keepReach;
            // The shader's tight frustum test runs at the blade's terrain
            // height; whatever that is, it moves a plane's value by no more
            // than |normal.y| * heightBound.
            _useFrustum = frustumPlanes != null;
            if (_useFrustum)
                for (int p = 0; p < 6; p++)
                    _planeW[p] = frustumPlanes[p].W + MathF.Abs(frustumPlanes[p].Y) * heightBound + frustumMargin + Slack;

            int firstX = SetupAxis(center.X, fieldSize, cellSize, frustumPlanes, false,
                _nearSqX, _planeX, _cellX, out int tilesX);
            int firstZ = SetupAxis(center.Y, fieldSize, cellSize, frustumPlanes, true,
                _nearSqZ, _planeZ, _cellZ, out int tilesZ);
            int lastX = firstX + tilesX - 1, lastZ = firstZ + tilesZ - 1;

            // Square rings of tiles around the camera's tile, nearest first.
            // Along any ray from the camera the ring number never decreases,
            // which is all front-to-back needs.
            double half = fieldSize * 0.5;
            int camX = (int)Math.Floor((camera.X + half) / cellSize);
            int camZ = (int)Math.Floor((camera.Y + half) / cellSize);
            int nearest = Math.Max(Math.Max(firstX - camX, camX - lastX), Math.Max(firstZ - camZ, camZ - lastZ));
            int farthest = Math.Max(Math.Max(camX - firstX, lastX - camX), Math.Max(camZ - firstZ, lastZ - camZ));
            for (int k = Math.Max(0, nearest); k <= farthest; k++)
            {
                int xLo = Math.Max(camX - k, firstX), xHi = Math.Min(camX + k, lastX);
                if (camZ - k >= firstZ)
                    for (int x = xLo; x <= xHi; x++) Visit(x - firstX, camZ - k - firstZ);
                if (k > 0 && camZ + k <= lastZ)
                    for (int x = xLo; x <= xHi; x++) Visit(x - firstX, camZ + k - firstZ);

                // The two side columns, minus the corners the rows covered
                int zLo = Math.Max(camZ - k + 1, firstZ), zHi = Math.Min(camZ + k - 1, lastZ);
                if (camX - k >= firstX)
                    for (int z = zLo; z <= zHi; z++) Visit(camX - k - firstX, z - firstZ);
                if (k > 0 && camX + k <= lastX)
                    for (int z = zLo; z <= zHi; z++) Visit(camX + k - firstX, z - firstZ);
            }
            _listed[ring] = _count;
        }

        /// <summary>
        /// One axis of the tile grid: the tiles the wrap window cuts into,
        /// each clipped to the window and widened by the slack. Fills, per
        /// tile, the squared distance from the centre to its nearest edge,
        /// each frustum plane's largest value across it, and the cell it is an
        /// image of. Returns the first tile's index.
        /// </summary>
        private static int SetupAxis(double center, double fieldSize, double cellSize, Vector4[] planes, bool zAxis,
            double[] nearSq, double[] plane, int[] cell, out int tiles)
        {
            // Mirror of GetInfinitePos: a blade lands within half a field of
            // the centre. Base positions start at -half, so tile t spans
            // [t, t + 1] * cellSize - half and is an image of cell t mod N.
            double half = fieldSize * 0.5;
            double windowLo = center - half - Slack, windowHi = center + half + Slack;
            int first = (int)Math.Floor((windowLo + half) / cellSize);
            int last = (int)Math.Floor((windowHi + half) / cellSize);
            tiles = last - first + 1; // <= MaxTiles: CullRing only gets here with cellSize > 4 * Slack

            for (int i = 0; i < tiles; i++)
            {
                int t = first + i;
                double lo = Math.Max(t * cellSize - half - Slack, windowLo);
                double hi = Math.Min((t + 1) * cellSize - half + Slack, windowHi);

                double near = lo > center ? lo - center : hi < center ? center - hi : 0.0;
                nearSq[i] = near * near;
                cell[i] = ((t % CellsPerSide) + CellsPerSide) % CellsPerSide;

                if (planes == null) continue;
                for (int p = 0; p < 6; p++)
                {
                    double n = zAxis ? planes[p].Z : planes[p].X;
                    plane[p * MaxTiles + i] = Math.Max(n * lo, n * hi);
                }
            }
            return first;
        }

        private void Visit(int tileX, int tileZ)
        {
            // Past the ring's reach nothing is kept (no capacity, or no demand)
            if (_nearSqX[tileX] + _nearSqZ[tileZ] >= _reachSq) return;

            // The tile is out of the frustum when even its best corner, at the
            // best height, fails one plane.
            if (_useFrustum)
                for (int p = 0; p < 6; p++)
                    if (_planeX[p * MaxTiles + tileX] + _planeZ[p * MaxTiles + tileZ] + _planeW[p] < 0.0) return;

            int cell = _cellZ[tileZ] * CellsPerSide + _cellX[tileX];
            if (_listedStamp[cell] == _stamp) return;
            _listedStamp[cell] = _stamp;
            List(cell);
        }

        private void List(int cell)
        {
            int start = _cellStart[cell], count = _cellStart[cell + 1] - start;
            if (count <= 0) return;
            Cells[_ringBase + _count * 2] = (uint)start;
            Cells[_ringBase + _count * 2 + 1] = (uint)count;
            _count++;
        }
    }
}
