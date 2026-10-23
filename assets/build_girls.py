# 把 gen/pose-*.png 整理成开机动画用的立绘：补满 alpha、裁到人物边界、存无损 webp，再包成 data URL 脚本
# （file:// 下 CSS mask 只认 data URL）。用法：python -I assets/build_girls.py
import base64, glob, json, re
from pathlib import Path
import numpy as np
from PIL import Image

root = Path(__file__).resolve().parent.parent
sizes = {}
for f in sorted(glob.glob(str(root / "gen" / "pose-*.png"))):
    pid = re.search(r"pose-([a-z])-", f).group(1)
    im = Image.open(f).convert("RGBA")
    a = np.asarray(im).copy()
    al = a[..., 3]
    al[al >= 240] = 255                 # 生图模型给的人物内部 alpha 只有 253～254，补成完全不透明
    al[al <= 6] = 0                     # 边缘外面几乎看不见的噪点直接清掉
    im = Image.fromarray(a, "RGBA")
    x0, y0, x1, y1 = im.getbbox()
    pad = 6
    im = im.crop((max(0, x0 - pad), max(0, y0 - pad), min(im.width, x1 + pad), min(im.height, y1 + pad)))
    webp = root / "assets" / f"girl-{pid}.webp"
    im.save(webp, "WEBP", lossless=True, quality=100, method=6)
    b64 = base64.b64encode(webp.read_bytes()).decode()
    (root / "assets" / f"girl-{pid}.js").write_text(
        f"// 拟人小克立绘 {pid.upper()}（{im.width}×{im.height}，透明 webp）\n"
        f"window.CLAUDE_GIRL = \"data:image/webp;base64,{b64}\";\n", encoding="utf-8")
    sizes[pid] = im.size
    print(pid, im.size, webp.stat().st_size)
print(json.dumps(sizes))
