"""Measure AV1 quality, bit depth and preset tradeoffs on short local samples.

python tests/benchmark-av1.py --ffmpeg C:/ffmpeg/ffmpeg.exe source.mkv ...
Outputs and measurements go to artifacts/av1-review. No extra Python packages.
"""
import argparse
import json
from pathlib import Path
import re
import subprocess
import time

parser = argparse.ArgumentParser()
parser.add_argument('--ffmpeg', default='ffmpeg')
parser.add_argument('sources', nargs='+')
opts = parser.parse_args()
dest = Path('artifacts/av1-review').resolve()
dest.mkdir(parents=True, exist_ok=True)


def run(arguments):
    result = subprocess.run([opts.ffmpeg, *map(str, arguments)], capture_output=True,
                            text=True, timeout=300)
    if result.returncode:
        raise RuntimeError(result.stderr)
    return result.stderr


rows = []
for index, source in enumerate(opts.sources):
    reference = dest / f'reference-{index}.mkv'
    run(['-v', 'error', '-i', source, '-t', '2', '-vf',
         "scale=w='min(iw,1280)':h=-2:flags=lanczos,format=yuv420p,fps=30",
         '-an', '-c:v', 'ffv1', '-y', reference])
    variants = [(4, depth, crf) for depth in (8, 10) for crf in (34, 30, 26, 20)]
    variants += [(preset, 10, 30) for preset in (6, 3)]
    for preset, depth, crf in variants:
        output = dest / f'{index}-p{preset}-{depth}bit-q{crf}.mp4'
        start = time.monotonic()
        run(['-v', 'error', '-i', reference, '-an', '-c:v', 'libsvtav1',
             '-preset', preset, '-crf', crf, '-pix_fmt',
             'yuv420p10le' if depth == 10 else 'yuv420p',
             '-svtav1-params', 'tune=0', '-g', '240', '-movflags', '+faststart', '-y', output])
        elapsed = time.monotonic() - start
        metric = run(['-i', output, '-i', reference, '-lavfi',
                      '[0:v]format=yuv420p,settb=AVTB,setpts=N/(30*TB)[a];'
                      '[1:v]format=yuv420p,settb=AVTB,setpts=N/(30*TB)[b];'
                      '[a][b]libvmaf=n_threads=4:shortest=1', '-an', '-f', 'null', '-'])
        row = dict(source=str(source), preset=preset, depth=depth, crf=crf,
                   seconds=round(elapsed, 3), bytes=output.stat().st_size,
                   vmaf=float(re.search(r'VMAF score: ([0-9.]+)', metric)[1]))
        rows.append(row)
        print(row, flush=True)
        (dest / 'results.json').write_text(json.dumps(rows, indent=2))
