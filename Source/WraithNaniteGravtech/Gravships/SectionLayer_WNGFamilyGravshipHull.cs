using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// WNG family gravship corner layer.
    ///
    /// Geometry, offsets, masks and corner-detection rules intentionally mirror RimWorld 1.6
    /// SectionLayer_GravshipHull. The only family-specific behavior is which hull Def counts as
    /// the neighbour and which authored WNG corner/substructure texture set is used.
    /// </summary>
    public sealed class SectionLayer_WNGFamilyGravshipHull : SectionLayer
    {
        private enum CornerType
        {
            None,
            Corner_NW,
            Corner_NE,
            Corner_SW,
            Corner_SE,
            Diagonal_NW,
            Diagonal_NE,
            Diagonal_SW,
            Diagonal_SE
        }

        private sealed class FamilyVisuals
        {
            public readonly string HullDefName;
            public readonly string CornerRoot;

            public ThingDef HullDef;
            public CachedMaterial CornerNW;
            public CachedMaterial CornerNE;
            public CachedMaterial CornerSW;
            public CachedMaterial CornerSE;
            public CachedMaterial DiagonalNW;
            public CachedMaterial DiagonalNE;
            public CachedMaterial DiagonalSW;
            public CachedMaterial DiagonalSE;
            public CachedMaterial SubstructureW;
            public CachedMaterial SubstructureE;
            public CachedMaterial SubstructureExtraW;
            public CachedMaterial SubstructureExtraE;

            public FamilyVisuals(string hullDefName, string cornerRoot)
            {
                HullDefName = hullDefName;
                CornerRoot = cornerRoot;
            }

            public void EnsureInitialized()
            {
                if (HullDef != null && CornerNW != null)
                    return;

                HullDef = DefDatabase<ThingDef>.GetNamedSilentFail(HullDefName);
                CornerNW = new CachedMaterial(CornerRoot + "/AngledGravshipHull_northwest", ShaderDatabase.CutoutOverlay);
                CornerNE = new CachedMaterial(CornerRoot + "/AngledGravshipHull_northeast", ShaderDatabase.CutoutOverlay);
                CornerSW = new CachedMaterial(CornerRoot + "/AngledGravshipHull_southwest", ShaderDatabase.CutoutOverlay);
                CornerSE = new CachedMaterial(CornerRoot + "/AngledGravshipHull_southeast", ShaderDatabase.CutoutOverlay);
                DiagonalNW = new CachedMaterial(CornerRoot + "/AngledGravshipHull_Partial_northwest", ShaderDatabase.CutoutOverlay);
                DiagonalNE = new CachedMaterial(CornerRoot + "/AngledGravshipHull_Partial_northeast", ShaderDatabase.CutoutOverlay);
                DiagonalSW = new CachedMaterial(CornerRoot + "/AngledGravshipHull_Partial_southwest", ShaderDatabase.CutoutOverlay);
                DiagonalSE = new CachedMaterial(CornerRoot + "/AngledGravshipHull_Partial_southeast", ShaderDatabase.CutoutOverlay);
                SubstructureW = new CachedMaterial(CornerRoot + "/SubstructureCorner_Full_west", ShaderDatabase.Transparent);
                SubstructureE = new CachedMaterial(CornerRoot + "/SubstructureCorner_Full_east", ShaderDatabase.Transparent);
                SubstructureExtraW = new CachedMaterial(CornerRoot + "/SubstructureCorner_Tip_west", ShaderDatabase.Transparent);
                SubstructureExtraE = new CachedMaterial(CornerRoot + "/SubstructureCorner_Tip_east", ShaderDatabase.Transparent);
            }

            public CachedMaterial MaterialFor(CornerType type)
            {
                EnsureInitialized();
                switch (type)
                {
                    case CornerType.Corner_NW: return CornerNW;
                    case CornerType.Corner_NE: return CornerNE;
                    case CornerType.Corner_SW: return CornerSW;
                    case CornerType.Corner_SE: return CornerSE;
                    case CornerType.Diagonal_NW: return DiagonalNW;
                    case CornerType.Diagonal_NE: return DiagonalNE;
                    case CornerType.Diagonal_SW: return DiagonalSW;
                    case CornerType.Diagonal_SE: return DiagonalSE;
                    default: throw new ArgumentOutOfRangeException(nameof(type), type, null);
                }
            }
        }

        private static readonly Vector2[] UVs =
        {
            new Vector2(0f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 0f)
        };

        private static readonly FamilyVisuals[] Families =
        {
            new FamilyVisuals("WNG_OrganicGravshipHull", "Things/Building/Wraith/Gravship/HullCorners"),
            new FamilyVisuals("WNG_PrecursorGravshipHull", "Things/Building/Precursor/Gravship/HullCorners"),
            new FamilyVisuals("WNG_GoauldGravshipHull", "Things/Building/Goauld/Gravship/HullCorners")
        };

        private static readonly IntVec3[] Directions =
        {
            IntVec3.North,
            IntVec3.East,
            IntVec3.South,
            IntVec3.West,
            IntVec3.North + IntVec3.West,
            IntVec3.North + IntVec3.East,
            IntVec3.South + IntVec3.East,
            IntVec3.South + IntVec3.West
        };

        private static readonly int[][] DirectionPairs =
        {
            new[] { 0, 2 },
            new[] { 1, 3 },
            new[] { 4, 6 },
            new[] { 5, 7 }
        };

        private static readonly bool[] TmpChecks = new bool[Directions.Length];
        private const float HullCornerScale = 2f;
        private const int BakedIndoorMaskRenderQueue = 3185;
        private static readonly float CornerAltitude = AltitudeLayer.BuildingOnTop.AltitudeFor();
        private static readonly float SubstructureAltitude = AltitudeLayer.TerrainEdges.AltitudeFor();
        private static readonly float BakedAltitude = AltitudeLayer.MetaOverlays.AltitudeFor();

        // Straight wall tiles use RimWorld's normal linked-wall atlas. This separate layer mirrors
        // Odyssey's SectionLayer_GravshipHull specifically for the large angled outer-hull pieces.
        // Family art changes the skin only; topology, scale, offsets and masking remain vanilla.
        public override bool Visible => ModsConfig.OdysseyActive;

        public SectionLayer_WNGFamilyGravshipHull(Section section)
            : base(section)
        {
            relevantChangeTypes =
                (ulong)MapMeshFlagDefOf.Buildings |
                (ulong)MapMeshFlagDefOf.Terrain |
                (ulong)MapMeshFlagDefOf.Things |
                (ulong)MapMeshFlagDefOf.Roofs;
        }

        public override void Regenerate()
        {
            if (!ModsConfig.OdysseyActive)
                return;

            ClearSubMeshes(MeshParts.All);

            Map map = Map;
            TerrainGrid terrainGrid = map.terrainGrid;

            foreach (FamilyVisuals family in Families)
                family.EnsureInitialized();

            foreach (IntVec3 cell in section.CellRect)
            {
                foreach (FamilyVisuals family in Families)
                {
                    if (family.HullDef == null ||
                        !ShouldDrawCornerPiece(cell, map, terrainGrid, family.HullDef, out CornerType cornerType, out Color color))
                    {
                        continue;
                    }

                    CachedMaterial material = family.MaterialFor(cornerType);
                    IntVec3 offset = GetOffset(cornerType);
                    bool addGravshipMask = IsCornerSubstructure(cell, cornerType);
                    bool addIndoorMask = IsCornerIndoorMasked(cell, cornerType, map);

                    AddQuad(
                        material.Material,
                        cell + offset,
                        HullCornerScale,
                        CornerAltitude,
                        color,
                        addGravshipMask,
                        addIndoorMask);

                    bool substructureToSouth =
                        terrainGrid.FoundationAt(cell + IntVec3.South)?.IsSubstructure ?? false;

                    AddSubstructure(
                        family,
                        cornerType,
                        cell,
                        substructureToSouth,
                        addGravshipMask,
                        addIndoorMask);

                    break;
                }
            }

            FinalizeMesh(MeshParts.All);
        }

        /// <summary>
        /// Includes family hull corners in the indoor mask used by Odyssey's gravship animation.
        /// Uses the same corner topology as this map layer and the native bake's roof/substructure
        /// conditions, relative coordinates, altitude, shader and render queue.
        /// </summary>
        public static List<LayerSubMesh> BakeGravshipIndoorMesh(Map map, CellRect bounds, Vector3 center)
        {
            List<LayerSubMesh> result = new List<LayerSubMesh>();
            if (!ModsConfig.OdysseyActive || map == null)
                return result;

            TerrainGrid terrainGrid = map.terrainGrid;
            HashSet<IntVec3> matchedCells = new HashSet<IntVec3>();
            foreach (FamilyVisuals family in Families)
            {
                family.EnsureInitialized();
                if (family.HullDef == null)
                    continue;

                Dictionary<CornerType, LayerSubMesh> meshes = new Dictionary<CornerType, LayerSubMesh>();
                foreach (IntVec3 cell in bounds)
                {
                    if (matchedCells.Contains(cell) ||
                        !ShouldDrawCornerPiece(cell, map, terrainGrid, family.HullDef,
                            out CornerType cornerType, out Color color))
                    {
                        continue;
                    }

                    // Regenerate selects the first matching family at a cell, even if its
                    // corner does not need an indoor mask. The bake must make the same choice.
                    matchedCells.Add(cell);
                    if (!IsCornerSubstructure(cell, cornerType) ||
                        !IsCornerIndoorMasked(cell, cornerType, map))
                    {
                        continue;
                    }

                    if (!meshes.TryGetValue(cornerType, out LayerSubMesh mesh))
                    {
                        Material material = family.MaterialFor(cornerType).Material;
                        Texture2D source = material.mainTexture as Texture2D;
                        Material indoorMask = MaterialPool.MatFrom(
                            source, ShaderDatabase.IndoorMaskMasked, material.color,
                            BakedIndoorMaskRenderQueue);
                        mesh = MapDrawLayer.CreateFreeSubMesh(indoorMask, map);
                        meshes.Add(cornerType, mesh);
                    }

                    AddQuad(
                        mesh,
                        (cell + GetOffset(cornerType)).ToVector3() - center,
                        HullCornerScale,
                        BakedAltitude,
                        color);
                }

                foreach (LayerSubMesh mesh in meshes.Values)
                {
                    mesh.FinalizeMesh(MeshParts.All);
                    result.Add(mesh);
                }
            }

            return result;
        }

        private static bool ShouldDrawCornerPiece(
            IntVec3 pos,
            Map map,
            TerrainGrid terrGrid,
            ThingDef hullDef,
            out CornerType cornerType,
            out Color color)
        {
            cornerType = CornerType.None;
            color = Color.white;

            if (pos.GetEdifice(map) != null)
                return false;

            TerrainDef terrainDef = terrGrid.FoundationAt(pos);
            if (terrainDef != null && terrainDef.IsSubstructure)
                return false;

            for (int i = 0; i < Directions.Length; i++)
                TmpChecks[i] = (pos + Directions[i]).GetEdificeSafe(map)?.def == hullDef;

            if (TmpChecks[0] && TmpChecks[3] && !TmpChecks[2] && !TmpChecks[1])
                cornerType = TmpChecks[4] ? CornerType.Corner_NW : CornerType.Diagonal_NW;
            else if (TmpChecks[0] && TmpChecks[1] && !TmpChecks[2] && !TmpChecks[3])
                cornerType = TmpChecks[5] ? CornerType.Corner_NE : CornerType.Diagonal_NE;
            else if (TmpChecks[2] && TmpChecks[1] && !TmpChecks[0] && !TmpChecks[3])
                cornerType = TmpChecks[6] ? CornerType.Corner_SE : CornerType.Diagonal_SE;
            else if (TmpChecks[2] && TmpChecks[3] && !TmpChecks[0] && !TmpChecks[1])
                cornerType = TmpChecks[7] ? CornerType.Corner_SW : CornerType.Diagonal_SW;

            if (cornerType == CornerType.None)
                return false;

            for (int pairIndex = 0; pairIndex < DirectionPairs.Length; pairIndex++)
            {
                int[] pair = DirectionPairs[pairIndex];
                for (int i = 0; i < pair.Length; i++)
                {
                    int directionIndex = pair[i];
                    if (!TmpChecks[directionIndex])
                        continue;

                    Thing neighbour = (pos + Directions[directionIndex]).GetEdificeSafe(map);
                    if (neighbour?.def == hullDef)
                    {
                        color = neighbour.DrawColor;
                        return true;
                    }
                }
            }

            return true;
        }

        private static IntVec3 GetOffset(CornerType cornerType)
        {
            switch (cornerType)
            {
                case CornerType.Corner_NE:
                case CornerType.Diagonal_NE:
                    return IntVec3.Zero;
                case CornerType.Corner_NW:
                case CornerType.Diagonal_NW:
                    return new IntVec3(-1, 0, 0);
                case CornerType.Corner_SE:
                case CornerType.Diagonal_SE:
                    return new IntVec3(0, 0, -1);
                case CornerType.Corner_SW:
                case CornerType.Diagonal_SW:
                    return new IntVec3(-1, 0, -1);
                default:
                    return IntVec3.Zero;
            }
        }

        private static bool IsCornerSubstructure(IntVec3 c, CornerType cornerType)
        {
            switch (cornerType)
            {
                case CornerType.Corner_NE:
                case CornerType.Diagonal_NE:
                    return SectionLayer_GravshipMask.IsValidSubstructure(c + IntVec3.North) ||
                           SectionLayer_GravshipMask.IsValidSubstructure(c + IntVec3.East);
                case CornerType.Corner_NW:
                case CornerType.Diagonal_NW:
                    return SectionLayer_GravshipMask.IsValidSubstructure(c + IntVec3.North) ||
                           SectionLayer_GravshipMask.IsValidSubstructure(c + IntVec3.West);
                case CornerType.Corner_SE:
                case CornerType.Diagonal_SE:
                    return SectionLayer_GravshipMask.IsValidSubstructure(c + IntVec3.South) ||
                           SectionLayer_GravshipMask.IsValidSubstructure(c + IntVec3.East);
                case CornerType.Corner_SW:
                case CornerType.Diagonal_SW:
                    return SectionLayer_GravshipMask.IsValidSubstructure(c + IntVec3.South) ||
                           SectionLayer_GravshipMask.IsValidSubstructure(c + IntVec3.West);
                default:
                    return false;
            }
        }

        private static bool IsCornerIndoorMasked(IntVec3 c, CornerType cornerType, Map map)
        {
            switch (cornerType)
            {
                case CornerType.Corner_NE:
                case CornerType.Diagonal_NE:
                    return (c + IntVec3.North).Roofed(map) ||
                           (c + IntVec3.East).Roofed(map);
                case CornerType.Corner_NW:
                case CornerType.Diagonal_NW:
                    return (c + IntVec3.North).Roofed(map) ||
                           (c + IntVec3.West).Roofed(map);
                case CornerType.Corner_SE:
                case CornerType.Diagonal_SE:
                    return (c + IntVec3.South).Roofed(map) ||
                           (c + IntVec3.East).Roofed(map);
                case CornerType.Corner_SW:
                case CornerType.Diagonal_SW:
                    return (c + IntVec3.South).Roofed(map) ||
                           (c + IntVec3.West).Roofed(map);
                default:
                    return false;
            }
        }

        private static void AddQuad(
            LayerSubMesh subMesh,
            Vector3 c,
            float scale,
            float altitude,
            Color color)
        {
            int first = subMesh.verts.Count;

            for (int i = 0; i < 4; i++)
            {
                subMesh.verts.Add(
                    new Vector3(
                        c.x + UVs[i].x * scale,
                        altitude,
                        c.z + UVs[i].y * scale));
                subMesh.uvs.Add(UVs[i]);
                subMesh.colors.Add(color);
            }

            subMesh.tris.Add(first);
            subMesh.tris.Add(first + 1);
            subMesh.tris.Add(first + 2);
            subMesh.tris.Add(first);
            subMesh.tris.Add(first + 2);
            subMesh.tris.Add(first + 3);
        }

        private void AddQuad(
            Material material,
            IntVec3 c,
            float scale,
            float altitude,
            Color color,
            bool addGravshipMask,
            bool addIndoorMask)
        {
            LayerSubMesh subMesh = GetSubMesh(material);
            AddQuad(subMesh, c.ToVector3(), scale, altitude, color);

            Texture2D source = subMesh.material.mainTexture as Texture2D;
            if (source == null)
                return;

            if (addGravshipMask)
            {
                Material masked =
                    MaterialPool.MatFrom(
                        source,
                        ShaderDatabase.GravshipMaskMasked,
                        subMesh.material.color);
                AddQuad(GetSubMesh(masked), c.ToVector3(), scale, altitude, color);
            }

            if (addIndoorMask)
            {
                Material masked =
                    MaterialPool.MatFrom(
                        source,
                        ShaderDatabase.IndoorMaskMasked,
                        subMesh.material.color);
                AddQuad(GetSubMesh(masked), c.ToVector3(), scale, altitude, color);
            }
        }

        private void AddSubstructure(
            FamilyVisuals family,
            CornerType cornerType,
            IntVec3 c,
            bool substructureToSouth,
            bool addGravshipMask,
            bool addIndoorMask)
        {
            if (cornerType == CornerType.Corner_NW ||
                cornerType == CornerType.Diagonal_NW)
            {
                AddQuad(
                    family.SubstructureW.Material,
                    c,
                    1f,
                    SubstructureAltitude,
                    Color.white,
                    addGravshipMask,
                    addIndoorMask);

                if (!substructureToSouth)
                {
                    AddQuad(
                        family.SubstructureExtraW.Material,
                        c + IntVec3.South,
                        1f,
                        SubstructureAltitude,
                        Color.white,
                        addGravshipMask,
                        addIndoorMask);
                }
            }

            if (cornerType == CornerType.Corner_NE ||
                cornerType == CornerType.Diagonal_NE)
            {
                AddQuad(
                    family.SubstructureE.Material,
                    c,
                    1f,
                    SubstructureAltitude,
                    Color.white,
                    addGravshipMask,
                    addIndoorMask);

                if (!substructureToSouth)
                {
                    AddQuad(
                        family.SubstructureExtraE.Material,
                        c + IntVec3.South,
                        1f,
                        SubstructureAltitude,
                        Color.white,
                        addGravshipMask,
                        addIndoorMask);
                }
            }
        }
    }
}
