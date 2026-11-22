import sys
from PIL import Image, ImageEnhance, ImageOps
import numpy as np, os
Image.MAX_IMAGE_PIXELS=None
U=os.environ['ASSET_ROOT'].rstrip('/')+'/'
O=sys.argv[1].rstrip('/')+'/'
# Court floor: Poly Haven wood_floor (tileable), recoloured toward light maple.
im=Image.open(U+'floor/wood_floor_2k.blend/textures/wood_floor_diff_2k.jpg').convert('RGB').resize((1024,1024),Image.LANCZOS)
a=np.asarray(im).astype(np.float32)/255
lum=a.mean(2,keepdims=True)
lum=(lum-lum.mean())*1.25+0.5            # keep the grain, re-centre brightness
maple=np.array([0.93,0.76,0.52])         # warm light maple
out=np.clip(maple*(0.62+0.75*lum),0,1)
Image.fromarray((out*255).astype(np.uint8)).save(O+'floor/court-maple.jpg',quality=86,optimize=True)
# Holographic foils, cropped to the card's portrait shape (2:3) and sized down.
picks={'foil-diamond.jpg':13,'foil-pearl.jpg':5,'foil-amethyst.jpg':1,'foil-ruby.jpg':2,'foil-opal.jpg':11,'foil-prism.jpg':9}
for name,n in picks.items():
    im=Image.open(U+f'cards/HOLOFOIL-FRZ-Designs/JPG/{n}.jpg'); im.draft('RGB',(1500,1000)); im=im.convert('RGB')
    im=ImageOps.fit(im,(768,1152),Image.LANCZOS,centering=(0.5,0.5))
    im.save(O+'cards/'+name,quality=84,optimize=True)
# Faceted gem bodies (Vecteezy, low-poly backgrounds), same 2:3 card crop.
gems={'gem-ruby.jpg':'vecteezy_a-red-background-with-diamonds-shape_34817381_961/vecteezy_a-red-background-with-diamonds-shape_34817381-1.jpg',
      'gem-sapphire.jpg':'vecteezy_adobe-illustrator-artwork-triangle-poly-prism-background_34784959_79/vecteezy_adobe-illustrator-artwork-triangle-poly-prism-background_34784959.jpg',
      'gem-amethyst.jpg':'vecteezy_purple-polygonal-mosaic-background-creative-design-templates_561306/poly-g-4-150(1246).jpg'}
for name,src in gems.items():
    im=Image.open(U+'cards/'+src).convert('RGB')
    ImageOps.fit(im,(768,1152),Image.LANCZOS).save(O+'cards/'+name,quality=84,optimize=True)
for d in ('floor','cards'):
    for f in sorted(os.listdir(O+d)): print(d,f,os.path.getsize(O+d+'/'+f)//1024,'KB')
