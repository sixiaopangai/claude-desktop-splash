# 白底立绘抠图 v2：原分辨率；发丝缝里的白底也抠掉；边缘按「前景色 + 白底」反解 alpha，去掉白边
import sys
import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage as ndi

src, out, protect = sys.argv[1], sys.argv[2], [int(v) for v in sys.argv[3].split(",")]
im = Image.open(src).convert("RGB")
im = im.filter(ImageFilter.UnsharpMask(radius=1.2, percent=45, threshold=2))
a = np.asarray(im).astype(np.float32)
mn, mx = a.min(2), a.max(2)
whiteish = (mn >= 236) & (mx - mn <= 12)

lab, n = ndi.label(whiteish)
idx = np.arange(1, n + 1)
border = set(np.unique(np.r_[lab[0], lab[-1], lab[:, 0], lab[:, -1]])) - {0}
size = ndi.sum(np.ones_like(mn), lab, idx)
mean = ndi.mean(mn, lab, idx)
sat = ndi.mean(mx - mn, lab, idx)
satmax = ndi.maximum(mx - mn, lab, idx)
com = ndi.center_of_mass(np.ones_like(mn), lab, idx)
x0, y0, x1, y1 = protect          # 眼睛一带的高光不能当背景抠掉
holes = [i for i, s, m, st, sm, (cy, cx) in zip(idx, size, mean, sat, satmax, com)
         if i not in border and s >= 12 and m >= 244 and st <= 5
         and not (x0 <= cx <= x1 and y0 <= cy <= y1)]
bg = np.isin(lab, list(border) + holes)
print("holes", len(holes))

# 只保留最大的那块前景（去掉散落的噪点）
fg = ~bg
keep, k = ndi.label(fg)
fg = keep == (np.argmax(ndi.sum(fg, keep, range(1, k + 1))) + 1)
bg = ~fg

d_in = ndi.distance_transform_edt(fg)        # 前景像素到背景的距离
d_out = ndi.distance_transform_edt(bg)       # 背景像素到前景的距离
# 「实色」像素（勾线、头发、裙子）原样保留；只有和白底混过色的过渡像素才反解
solid = fg & ((d_in > 2) | (mn < 120) | (mx - mn > 40))
band = (fg & ~solid & (d_in <= 3)) | (bg & (d_out <= 1.5))

# 过渡像素取最近的实色像素当前景色（通常就是勾线），再按 c = α·f + (1-α)·255 反解 α
_, (iy, ix) = ndi.distance_transform_edt(~solid, return_indices=True)
f = a[iy, ix]
num = ((255 - a) * (255 - f)).sum(2)
den = ((255 - f) ** 2).sum(2)
alpha_band = np.where(den > 3 * 30 ** 2, np.clip(num / np.maximum(den, 1), 0, 1), fg.astype(np.float32))
alpha = np.where(band, alpha_band, fg.astype(np.float32))
rgb = np.where((fg & ~band)[..., None], a, f)   # 透明区也填上邻近实色，压缩时就不会把白底渗回边缘

rgba = np.dstack([np.clip(rgb, 0, 255), alpha * 255]).astype(np.uint8)
img = Image.fromarray(rgba, "RGBA")
img = img.crop(img.getbbox())
img.save(out, "WEBP", lossless=True, quality=100, method=6)   # 无损：不做色度抽样，勾线不糊
print("out", img.size)
