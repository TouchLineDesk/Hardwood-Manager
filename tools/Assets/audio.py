import subprocess, numpy as np, sys, os
SR=44100
A=os.path.join(os.environ['ASSET_ROOT'],'audio')+'/'
SONIK_CHEER=A+'Sonniss.com-GDC2026-GameAudioBundle4of5/Sonik Sound Library - Spanish Crowds/CRWDCheer_Small Club, 50 People, Crowd, Spanish, Cheering and Applause-Surround_KSL_KS015.wav'
SONIK_WALLA=A+'Sonniss.com-GDC2026-GameAudioBundle4of5/Sonik Sound Library - Spanish Wallas/CRWDWalla_Crowd, Walla, Dense Immersive Group of Adults, After or Befor a Show, Exterior, Spanish-Stereo_KSL_KS014.wav'
JAKE=A+'Sonniss.com-GDC2026-GameAudioBundle3of5/Jake Fielding - British Walla and Crowd/CRWDWalla_Pub Walla, Crowd, Bar, _Jake Fielding_British Walla and Crowd_14.wav'
WHISTLE=A+'Sonniss.com-GDC2026-GameAudioBundle5of5/SoundBits - Vox Hominis - Human Effort Voices/WHSTHmn_Whistle Male 06 09_SNDBTS_VH.wav'
OUT=sys.argv[1]
def load(p, af=None):
    cmd=['ffmpeg','-v','error','-i',p]
    if af: cmd+=['-af',af]
    cmd+=['-ac','2','-ar',str(SR),'-f','f32le','-']
    return np.frombuffer(subprocess.run(cmd,capture_output=True,check=True).stdout,np.float32).reshape(-1,2).copy()
def norm(x, target_db):
    r=np.sqrt(np.mean(x**2)); return x*(10**(target_db/20)/r)
def loop(x, seconds, xf=3.0, start=0.0):
    n=int(seconds*SR); f=int(xf*SR); s=int(start*SR)
    seg=x[s:s+n+f].copy()
    head=seg[:f]; tail=seg[n:n+f]
    t=np.linspace(0,1,f)[:,None]
    out=seg[:n].copy()
    out[:f]=head*np.sqrt(t)+tail*np.sqrt(1-t)   # equal-power: end flows into start
    return out
def fade(x, fi=0.05, fo=0.6):
    x=x.copy(); a=int(fi*SR); b=int(fo*SR)
    x[:a]*=np.linspace(0,1,a)[:,None]; x[-b:]*=np.linspace(1,0,b)[:,None]; return x
def write(name, x, q=4):
    x=np.clip(x,-0.98,0.98).astype(np.float32)
    subprocess.run(['ffmpeg','-v','error','-y','-f','f32le','-ar',str(SR),'-ac','2','-i','-','-c:a','libvorbis','-q:a',str(q),os.path.join(OUT,name)],input=x.tobytes(),check=True)
    print(name, '%.1fs'%(len(x)/SR), '%dKB'%(os.path.getsize(os.path.join(OUT,name))//1024), 'peak %.2f'%np.abs(x).max())
# indoor feel: high-pass rumble, gentle hall reflections
HALL='highpass=f=90,aecho=0.8:0.6:45|90:0.25|0.15'
jake=load(JAKE, HALL); walla=load(SONIK_WALLA, HALL)
# bed-low: the quiet, dense Sonik walla alone (sparse crowd / early season)
write('crowd-bed-low.ogg', norm(loop(walla, 45, start=8), -26))
# bed-high: pub crowd plus walla layered = full house murmur
n=int(55*SR)
j=loop(jake,55,start=10); w=loop(np.concatenate([walla,walla]),55,start=20)
write('crowd-bed-high.ogg', norm(norm(j,-20)+norm(w,-24), -20))
# cheers: 5.1 surround folded to stereo
cheer=load(SONIK_CHEER,'pan=stereo|FL=FL+0.7*FC+0.5*BL|FR=FR+0.7*FC+0.5*BR,highpass=f=80')
for i,(a,b) in enumerate([(0.0,3.2),(6.8,12.2),(16.6,19.4)],1):
    seg=cheer[int(a*SR):int(b*SR)]
    write(f'crowd-cheer-{i}.ogg', fade(norm(seg,-18),0.08,0.9))
# applause tail: the calmer clapping between bursts
write('crowd-applause.ogg', fade(norm(cheer[int(19.4*SR):int(26.5*SR)],-18),0.3,1.5))
# a fan whistling in the stands (lip whistle, not a referee's whistle)
wh=load(WHISTLE); write('crowd-fan-whistle.ogg', fade(norm(wh,-20),0.02,0.3))
