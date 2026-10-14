using System;
using System.Collections;
using System.Collections.Generic;
namespace UnityEngine {
    public struct Vector2 { public float x,y; public Vector2(float x,float y){this.x=x;this.y=y;} }
    public struct Vector3 { public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;} public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
    public struct Color { public float r,g,b,a; public Color(float r,float g,float b,float a=1){this.r=r;this.g=g;this.b=b;this.a=a;} public static Color white=>new Color(1,1,1); }
    public class Texture2D { public string Path; }
    public class Material { public Texture2D mainTexture;public Color color=Color.white;public string Shader;public int RenderQueue; }
}
namespace Verse {
    using UnityEngine;
    public struct IntVec3 : IEquatable<IntVec3> { public int x,y,z;public IntVec3(int x,int y,int z){this.x=x;this.y=y;this.z=z;} public static IntVec3 North=>new IntVec3(0,0,1);public static IntVec3 South=>new IntVec3(0,0,-1);public static IntVec3 East=>new IntVec3(1,0,0);public static IntVec3 West=>new IntVec3(-1,0,0);public static IntVec3 Zero=>default; public static IntVec3 operator +(IntVec3 a,IntVec3 b)=>new IntVec3(a.x+b.x,a.y+b.y,a.z+b.z);public Vector3 ToVector3()=>new Vector3(x,y,z);public bool Equals(IntVec3 other)=>x==other.x&&y==other.y&&z==other.z;public override bool Equals(object value)=>value is IntVec3 other&&Equals(other);public override int GetHashCode()=>HashCode.Combine(x,y,z); }
    public class ThingDef { public string defName; }
    public static class DefDatabase<T> where T:class { public static Dictionary<string,T> Defs=new Dictionary<string,T>();public static T GetNamedSilentFail(string name)=>Defs.TryGetValue(name,out T value)?value:null; }
    public class Thing { public ThingDef def;public Color DrawColor; }
    public class TerrainDef { public bool IsSubstructure; }
    public class TerrainGrid { public Dictionary<IntVec3,TerrainDef> Foundations=new Dictionary<IntVec3,TerrainDef>();public TerrainDef FoundationAt(IntVec3 c)=>Foundations.TryGetValue(c,out TerrainDef value)?value:null; }
    public class Map { public TerrainGrid terrainGrid=new TerrainGrid();public Dictionary<IntVec3,Thing> Edifices=new Dictionary<IntVec3,Thing>();public HashSet<IntVec3> Roofs=new HashSet<IntVec3>(); }
    public static class CellExtensions { public static Thing GetEdifice(this IntVec3 c,Map map)=>map.Edifices.TryGetValue(c,out Thing t)?t:null;public static Thing GetEdificeSafe(this IntVec3 c,Map map)=>c.GetEdifice(map);public static bool Roofed(this IntVec3 c,Map map)=>map.Roofs.Contains(c); }
    public struct CellRect:IEnumerable<IntVec3> { private IntVec3[] cells;public CellRect(params IntVec3[] cells){this.cells=cells;}public IEnumerator<IntVec3> GetEnumerator()=>((IEnumerable<IntVec3>)(cells??Array.Empty<IntVec3>())).GetEnumerator();IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator(); }
    public class CachedMaterial { public Material Material;public CachedMaterial(string path,string shader){Material=new Material{mainTexture=new Texture2D{Path=path},Shader=shader};} }
    public static class MaterialPool { public static Material MatFrom(Texture2D texture,string shader,Color color,int queue=0)=>new Material{mainTexture=texture,Shader=shader,color=color,RenderQueue=queue}; }
    public static class ShaderDatabase { public const string CutoutOverlay="CutoutOverlay",Transparent="Transparent",GravshipMaskMasked="GravshipMaskMasked",IndoorMaskMasked="IndoorMaskMasked"; }
    public enum MeshParts { All }
    public class LayerSubMesh { public Material material;public List<Vector3> verts=new List<Vector3>();public List<Vector2> uvs=new List<Vector2>();public List<Color> colors=new List<Color>();public List<int> tris=new List<int>();public bool Finalized;public LayerSubMesh(Material material){this.material=material;}public void FinalizeMesh(MeshParts parts){Finalized=true;} }
    public static class MapDrawLayer { public static LayerSubMesh CreateFreeSubMesh(Material material,Map map)=>new LayerSubMesh(material); }
    public class Section { public Map Map;public CellRect CellRect; }
    public class SectionLayer { protected Section section;protected ulong relevantChangeTypes;protected Map Map=>section.Map;public virtual bool Visible=>true;public SectionLayer(Section s){section=s;}public virtual void Regenerate(){}protected void ClearSubMeshes(MeshParts p){}protected void FinalizeMesh(MeshParts p){}protected LayerSubMesh GetSubMesh(Material m)=>new LayerSubMesh(m); }
    public enum AltitudeLayer { BuildingOnTop,TerrainEdges,MetaOverlays }
    public static class AltitudeExtensions { public static float AltitudeFor(this AltitudeLayer layer)=>(float)layer+5f; }
}
namespace RimWorld {
    using Verse;
    public static class ModsConfig { public static bool OdysseyActive=true; }
    public static class MapMeshFlagDefOf { public const ulong Buildings=1,Terrain=2,Things=4,Roofs=8; }
    public static class SectionLayer_GravshipMask { public static Map CurrentMap;public static bool IsValidSubstructure(IntVec3 cell)=>CurrentMap.terrainGrid.FoundationAt(cell)?.IsSubstructure??false; }
}

namespace HarmonyLib { public class HarmonyPatch:System.Attribute { public HarmonyPatch(System.Type type,string method){} } }
namespace RimWorld { public static class SectionLayer_GravshipHull { public static System.Collections.Generic.List<Verse.LayerSubMesh> BakeGravshipIndoorMesh(Verse.Map map,Verse.CellRect bounds,UnityEngine.Vector3 center)=>new System.Collections.Generic.List<Verse.LayerSubMesh>(); } }
