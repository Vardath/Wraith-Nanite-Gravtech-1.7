using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using WraithNaniteGravtech;
internal static class Program {
    static int checks;
    static readonly IntVec3 Cell=new IntVec3(20,0,30);
    static readonly Vector3 Center=new Vector3(9,99,13);
    static readonly Color Tint=new Color(.2f,.4f,.7f);
    static readonly string[] Families={"WNG_OrganicGravshipHull","WNG_PrecursorGravshipHull","WNG_GoauldGravshipHull"};
    static readonly string[] Roots={"Wraith","Precursor","Goauld"};
    static readonly string[] Directions={"northwest","northeast","southwest","southeast"};
    static readonly IntVec3[] Vertical={IntVec3.North,IntVec3.North,IntVec3.South,IntVec3.South};
    static readonly IntVec3[] Horizontal={IntVec3.West,IntVec3.East,IntVec3.West,IntVec3.East};
    static readonly IntVec3[] Offsets={new IntVec3(-1,0,0),IntVec3.Zero,new IntVec3(-1,0,-1),new IntVec3(0,0,-1)};
    static void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
    static Map Setup(int family,int direction,bool full,bool roof=true,bool foundation=true){
        var map=new Map();var def=DefDatabase<ThingDef>.Defs[Families[family]];
        foreach(var delta in new[]{Vertical[direction],Horizontal[direction]})map.Edifices[Cell+delta]=new Thing{def=def,DrawColor=Tint};
        if(full)map.Edifices[Cell+Vertical[direction]+Horizontal[direction]]=new Thing{def=def,DrawColor=Tint};
        if(roof)map.Roofs.Add(Cell+Horizontal[direction]);
        if(foundation)map.terrainGrid.Foundations[Cell+Vertical[direction]]=new TerrainDef{IsSubstructure=true};
        SectionLayer_GravshipMask.CurrentMap=map;return map;
    }
    static List<LayerSubMesh> Bake(Map map)=>SectionLayer_WNGFamilyGravshipHull.BakeGravshipIndoorMesh(map,new CellRect(Cell),Center);
    static void Main(){
        foreach(var family in Families)DefDatabase<ThingDef>.Defs[family]=new ThingDef{defName=family};
        for(int f=0;f<3;f++)for(int d=0;d<4;d++)foreach(bool full in new[]{false,true}){
            var result=Bake(Setup(f,d,full));string label=$"{Families[f]} {Directions[d]} full={full}";
            Check(result.Count==1,label+" mesh count");var mesh=result[0];
            Check(mesh.Finalized,label+" finalization");Check(mesh.material.Shader==ShaderDatabase.IndoorMaskMasked,label+" shader");Check(mesh.material.RenderQueue==3185,label+" render queue");
            string expected=$"Things/Building/{Roots[f]}/Gravship/HullCorners/AngledGravshipHull_{(full?"":"Partial_")}{Directions[d]}";
            Check(mesh.material.mainTexture.Path==expected,label+" family/directional art");Check(mesh.verts.Count==4&&mesh.uvs.Count==4&&mesh.colors.Count==4,label+" vertex channels");
            var origin=(Cell+Offsets[d]).ToVector3()-Center;
            var uvs=new[]{new Vector2(0,0),new Vector2(0,1),new Vector2(1,1),new Vector2(1,0)};
            for(int i=0;i<4;i++){
                Check(mesh.verts[i].x==origin.x+uvs[i].x*2&&mesh.verts[i].z==origin.z+uvs[i].y*2,label+" relative vertex "+i);
                Check(mesh.verts[i].y==AltitudeLayer.MetaOverlays.AltitudeFor(),label+" altitude ignores center.y");
                Check(mesh.uvs[i].x==uvs[i].x&&mesh.uvs[i].y==uvs[i].y,label+" UV "+i);
                Check(mesh.colors[i].r==Tint.r&&mesh.colors[i].g==Tint.g&&mesh.colors[i].b==Tint.b,label+" inherited hull tint");
            }
            Check(string.Join(",",mesh.tris)=="0,1,2,0,2,3",label+" winding");
        }
        Check(Bake(Setup(0,0,true,false)).Count==0,"Unroofed hull corners must not be baked");
        Check(Bake(Setup(0,0,true,true,false)).Count==0,"No adjacent substructure must not be baked");
        var blocked=Setup(0,0,true);blocked.Edifices[Cell]=new Thing{def=new ThingDef()};Check(Bake(blocked).Count==0,"Occupied target excluded");
        var foundationTarget=Setup(0,0,true);foundationTarget.terrainGrid.Foundations[Cell]=new TerrainDef{IsSubstructure=true};Check(Bake(foundationTarget).Count==0,"Target with substructure excluded");
        var mixed=Setup(0,0,false);mixed.Edifices[Cell+IntVec3.West].def=DefDatabase<ThingDef>.Defs[Families[1]];Check(Bake(mixed).Count==0,"Mixed families cannot form a family hull corner");
        var junction=Setup(0,0,true);foreach(var delta in new[]{IntVec3.South,IntVec3.East})junction.Edifices[Cell+delta]=new Thing{def=DefDatabase<ThingDef>.Defs[Families[0]]};Check(Bake(junction).Count==0,"Four-way junction excluded");
        var vanilla=Setup(0,0,false);foreach(var thing in vanilla.Edifices.Values)thing.def=new ThingDef{defName="GravshipHull"};Check(Bake(vanilla).Count==0,"Vanilla-only hull corners untouched");
        var inactive=Setup(0,0,true);ModsConfig.OdysseyActive=false;Check(Bake(inactive).Count==0,"Odyssey-off no-op");ModsConfig.OdysseyActive=true;
        Check(Bake(null).Count==0,"Null map no-op");Check(SectionLayer_WNGFamilyGravshipHull.BakeGravshipIndoorMesh(Setup(0,0,true),new CellRect(),Center).Count==0,"Empty bake bounds no-op");
        var opposing=Setup(0,0,false);
        foreach(var delta in new[]{IntVec3.South,IntVec3.East})opposing.Edifices[Cell+delta]=new Thing{def=DefDatabase<ThingDef>.Defs[Families[1]],DrawColor=Tint};
        opposing.Roofs.Add(Cell+IntVec3.South);opposing.terrainGrid.Foundations[Cell+IntVec3.South]=new TerrainDef{IsSubstructure=true};
        var firstFamily=Bake(opposing);Check(firstFamily.Count==1&&firstFamily[0].material.mainTexture.Path.Contains("/Wraith/"),"Opposing family corners use the same first-family precedence as map rendering");
        opposing.Roofs.Clear();opposing.Roofs.Add(Cell+IntVec3.South);
        Check(Bake(opposing).Count==0,"An unroofed first-family corner suppresses a later family's eligible indoor mask");
        opposing.Roofs.Add(Cell+IntVec3.North);opposing.terrainGrid.Foundations.Remove(Cell+IntVec3.North);
        Check(Bake(opposing).Count==0,"A first-family corner without adjacent substructure suppresses later eligible family");
        var postfix=typeof(WNGGravshipBakeIndoorHullMaskPatch).GetMethod("Postfix",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
        var vanillaMesh=new LayerSubMesh(new Material());var preserved=new List<LayerSubMesh>{vanillaMesh};
        postfix.Invoke(null,new object[]{preserved,Setup(1,1,true),new CellRect(Cell),Center});
        Check(preserved.Count==2&&object.ReferenceEquals(preserved[0],vanillaMesh),"Native mesh result is preserved and family mask appended");
        var unmodified=new List<LayerSubMesh>{vanillaMesh};var noFamily=Setup(0,0,false);foreach(var thing in noFamily.Edifices.Values)thing.def=new ThingDef{defName="GravshipHull"};
        postfix.Invoke(null,new object[]{unmodified,noFamily,new CellRect(Cell),Center});Check(unmodified.Count==1&&object.ReferenceEquals(unmodified[0],vanillaMesh),"Vanilla-only postfix result untouched");
        ModsConfig.OdysseyActive=false;postfix.Invoke(null,new object[]{unmodified,Setup(0,0,true),new CellRect(Cell),Center});Check(unmodified.Count==1,"Inactive Odyssey postfix no-op");ModsConfig.OdysseyActive=true;
        postfix.Invoke(null,new object[]{null,Setup(0,0,true),new CellRect(Cell),Center});Check(true,"Null postfix result tolerated");
        var batched=Setup(0,0,false);var secondCell=Cell+new IntVec3(5,0,0);
        foreach(var delta in new[]{IntVec3.North,IntVec3.West})batched.Edifices[secondCell+delta]=new Thing{def=DefDatabase<ThingDef>.Defs[Families[0]],DrawColor=Tint};
        batched.Roofs.Add(secondCell+IntVec3.North);batched.terrainGrid.Foundations[secondCell+IntVec3.North]=new TerrainDef{IsSubstructure=true};
        var batching=SectionLayer_WNGFamilyGravshipHull.BakeGravshipIndoorMesh(batched,new CellRect(Cell,secondCell),Center);
        Check(batching.Count==1&&batching[0].verts.Count==8&&batching[0].Finalized&&string.Join(",",batching[0].tris)=="0,1,2,0,2,3,4,5,6,4,6,7","Same-family corner masks batch into one finalized mesh with correct second-quad vertex indices");
        Console.WriteLine($"PASS: {checks} assertions; all 3 families × 8 corner variants, native geometry/materials, roof/substructure gates and unrelated-wall exclusions.");
    }
}
