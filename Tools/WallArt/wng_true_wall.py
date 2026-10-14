#!/usr/bin/env python3
"""Vanilla-reference Wraith living wall: all 16 genuine linked-wall geometries.

Template measured from the 1024px ReBuild copy of RimWorld Wall_Atlas_Smooth,
Bricks and Planks. Original 192px sample is drawn at 2x: 384px in a 512px atlas cell.
Exposed edge inset: 18px; facade transition y204..230.
Lower wall face: y230..365; side bevel width up to 90px.
This is a material-specific repaint from existing approved Wraith artwork,
not a reskin of the copyrighted reference texture.
"""
from pathlib import Path
from PIL import Image, ImageDraw
import numpy as np

S = 384
EDGE = 18
TOP_END = 44
FACE_TRANSITION = 204
FACE_START = 230

def rectangle(mask):
    return (0 if mask & 8 else EDGE, 0 if mask & 1 else EDGE,
            S if mask & 2 else S-EDGE, S if mask & 4 else S-EDGE)

def approved_source(repo):
    image = Image.open(Path(repo)/"ArtSource/Walls/Wraith/Approved/WraithLivingWall_link00_approved.jpg").convert("RGB")
    rgb = np.asarray(image)
    visible = rgb.max(axis=2)>17
    if not visible.any():
        raise ValueError("Approved Wraith original painting missing or empty")
    filled = np.maximum.accumulate(visible,axis=1)&np.maximum.accumulate(visible[:,::-1],axis=1)[:,::-1]
    return Image.fromarray(np.dstack((rgb, np.where(filled,255,0).astype(np.uint8))),"RGBA")

def build_material(master):
    """Paint with large, high-resolution APPROVED Wraith image regions."""
    bbox = master.getchannel("A").getbbox()
    if bbox is None: raise ValueError("Missing original Wraith painting")
    original = master.crop(bbox).convert("RGB")
    w,h = original.size
    if min(w,h) < 600: raise ValueError("Master lacks sufficient resolution")
    def crop(box,size):
        a,b,c,d=box
        img=original.crop((round(a*w),round(b*h),round(c*w),round(d*h)))
        from PIL import ImageFilter
        return np.asarray(img.resize(size, Image.Resampling.LANCZOS).filter(
            ImageFilter.UnsharpMask(radius=1.0,percent=110,threshold=2)),dtype=np.float32)
    def tileable(a, sides="xy", band=36):
        a=a.copy()
        if "x" in sides:
            for j in range(band):
                weight=.5*(1+np.cos(np.pi*j/band))
                l,r=a[:,j,:].copy(),a[:,-j-1,:].copy()
                avg=(l+r)/2
                a[:,j,:]=l*(1-weight)+avg*weight
                a[:,-j-1,:]=r*(1-weight)+avg*weight
            a[:,0,:]=a[:,-1,:]=(a[:,0,:]+a[:,-1,:])/2
        if "y" in sides:
            for j in range(band):
                weight=.5*(1+np.cos(np.pi*j/band))
                t,b=a[j,:,:].copy(),a[-j-1,:,:].copy()
                avg=(t+b)/2
                a[j,:,:]=t*(1-weight)+avg*weight
                a[-j-1,:,:]=b*(1-weight)+avg*weight
            a[0,:,:]=a[-1,:,:]=(a[0,:,:]+a[-1,:,:])/2
        return a
    return (
        tileable(crop((.275,.22,.725,.665),(S,S))),
        tileable(crop((.23,.85,.77,.993),(S,150)),"x"),
        tileable(crop((.20,.005,.80,.115),(S,TOP_END)),"x"),
        crop((.008,.20,.205,.855),(90,S)),
        crop((.795,.20,.992,.855),(90,S)),
        tileable(crop((.24,.76,.76,.87),(S,36)),"x")
    )

def generate(source):
    roof, facade, topcap, west, east, rim=build_material(source)
    states=[]
    for mask in range(16):
        x0,y0,x1,y1=rectangle(mask)
        paint=roof.copy()
        if not mask&4:
            for y in range(FACE_TRANSITION,min(FACE_START,y1)):
                t=(y-FACE_TRANSITION)/(FACE_START-FACE_TRANSITION)
                paint[y,:,:]=paint[y,:,:]*(1-t)+rim[min(35,round(t*35)),:,:]*(.95-.15*t)*t
            for y in range(FACE_START,y1):
                fy=min(facade.shape[0]-1,round((y-FACE_START)*(facade.shape[0]-1)/(S-EDGE-FACE_START-1)))
                paint[y,:,:]=facade[fy,:,:]*(.74+.15*(y-FACE_START)/max(1,y1-FACE_START))
        if not mask&1:
            for y in range(y0,min(y0+TOP_END,y1)):
                k=y-y0
                t=k/max(1,TOP_END-1)
                paint[y,:,:]=paint[y,:,:]*(.12+.16*t)+topcap[k,:,:]*(.88-.16*t)*(.78+.20*t)
        for flag,side,is_west in ((8,west,True),(2,east,False)):
            if mask&flag: continue
            for y in range(y0,y1):
                width=100 if (mask&4 or y<FACE_START) else 42
                for d in range(width):
                    x=x0+d if is_west else x1-1-d
                    ratio=d/max(1,width-1)
                    sx=min(89,round(ratio*89))
                    weight=.98*(1-ratio)**.65
                    shade=(.97+.12*(1-ratio)) if y<FACE_TRANSITION else .86
                    paint[y,x,:]=paint[y,x,:]*(1-weight)+side[y,sx,:]*weight*shade
        port=20
        for j in range(port):
            amount=(1-j/port)**2
            if mask&8: paint[:,j,:]=paint[:,j,:]*(1-amount)+roof[:,0,:]*amount
            if mask&2: paint[:,-j-1,:]=paint[:,-j-1,:]*(1-amount)+roof[:,0,:]*amount
            if mask&1: paint[j,:,:]=paint[j,:,:]*(1-amount)+roof[0,:,:]*amount
            if mask&4: paint[-j-1,:,:]=paint[-j-1,:,:]*(1-amount)+roof[0,:,:]*amount
        if mask&8: paint[:,0,:]=roof[:,0,:]
        if mask&2: paint[:,-1,:]=roof[:,0,:]
        if mask&1: paint[0,:,:]=roof[0,:,:]
        if mask&4: paint[-1,:,:]=roof[0,:,:]
        rgba=np.zeros((S,S,4),dtype=np.uint8)
        rgba[y0:y1,x0:x1,:3]=np.clip(paint[y0:y1,x0:x1],0,255).astype(np.uint8)
        rgba[y0:y1,x0:x1,3]=255
        states.append(Image.fromarray(rgba,"RGBA"))
    verify(states)
    return states

def verify(states):
    if len(states)!=16:raise ValueError("Not sixteen Wraith masks")
    images=[np.asarray(im.convert("RGBA")) for im in states]
    for mask,image in enumerate(images):
        x0,y0,x1,y1=rectangle(mask)
        truth=np.zeros((S,S),dtype=bool)
        truth[y0:y1,x0:x1]=True
        if not np.array_equal(image[...,3]==255,truth):
            raise ValueError(f"Wraith mask {mask:02d} differs from measured reference silhouette")
    area=slice(EDGE,S-EDGE)
    for a in range(16):
        for b in range(16):
            if (a&2) and (b&8):
                if not np.array_equal(images[a][area,-1],images[b][area,0]):
                    raise ValueError(f"East/west incompatible Wraith seam: {a:02d}/{b:02d}")
            if (a&4) and (b&1):
                if not np.array_equal(images[a][-1,area],images[b][0,area]):
                    raise ValueError(f"South/north incompatible Wraith seam: {a:02d}/{b:02d}")

def pack(states):
    """Native MaterialAtlasPool: 2048px, 4x4 512px cells, 384px printed."""
    stride=S*4//3
    inset=S//6
    atlas=Image.new("RGBA",(stride*4,stride*4))
    for mask,tile in enumerate(states):
        area=np.asarray(tile.convert("RGBA"))
        padded=np.pad(area,((inset,inset),(inset,inset),(0,0)),mode="edge")
        atlas.paste(Image.fromarray(padded,"RGBA"),
                    ((mask%4)*stride,(3-mask//4)*stride))
    return atlas
