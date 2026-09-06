"""Compare VP9 rate/distortion curves; no network or third-party Python packages.

python tests/benchmark-vp9.py --ffmpeg C:/ffmpeg/ffmpeg.exe
Outputs normalized local references, encodes and frame-aligned metrics in artifacts/.
Timings include process startup and both passes, but exclude metric computation.
"""
import argparse
import json
import os
from pathlib import Path
import subprocess
import time

parser = argparse.ArgumentParser()
parser.add_argument('--ffmpeg', default='ffmpeg')
opts = parser.parse_args()
root = Path(__file__).resolve().parent.parent
dest = root / 'artifacts' / 'vp9-benchmark'
dest.mkdir(parents=True, exist_ok=True)
ffprobe = str(Path(opts.ffmpeg).with_name('ffprobe' + ('.exe' if os.name == 'nt' else '')))

def run(arguments, executable=opts.ffmpeg):
    p = subprocess.run([executable, *map(str, arguments)], cwd=dest, capture_output=True, text=True, timeout=180)
    if p.returncode:
        raise RuntimeError(p.stderr)
    return p.stdout

refs = []
for i, source in enumerate(sorted(root.glob('2025-*.mkv'))):
    ref = dest / f'screen{i + 1}.mkv'
    run(['-v', 'error', '-i', source, '-t', '4', '-vf',
         'scale=1280:-2:flags=lanczos,format=yuv420p,fps=30', '-an', '-c:v', 'ffv1', '-y', ref])
    refs.append(ref)
ref = dest / 'motion.mkv'
run(['-v', 'error', '-f', 'lavfi', '-i', 'testsrc2=size=1280x720:rate=30:duration=4', '-an', '-c:v', 'ffv1', '-y', ref])
refs.append(ref)
results = []
for ref in refs:
    reference_probe = json.loads(run(['-v', 'error', '-count_frames', '-select_streams', 'v:0',
                                     '-show_entries', 'stream=nb_read_frames', '-of', 'json', ref], ffprobe))
    reference_frames = int(reference_probe['streams'][0]['nb_read_frames'])
    for passes, qualities in [(1, [24, 28, 30, 34]), (2, [24, 28, 30, 34, 38, 42])]:
        for q in qualities:
            name = f'{ref.stem}-{passes}pass-q{q}'
            output = dest / f'{name}.webm'
            common = ['-v', 'error', '-i', ref, '-map', '0:v:0', '-an', '-c:v', 'libvpx-vp9',
                      '-pix_fmt', 'yuv420p', '-profile:v', '0', '-crf', str(q), '-b:v', '0',
                      '-deadline', 'good', '-row-mt', '1', '-lag-in-frames', '25', '-g', '240', '-fps_mode', 'passthrough']
            start = time.perf_counter()
            if passes == 2:
                run([*common, '-cpu-used', '4', '-pass', '1', '-passlogfile', name,
                     '-f', 'null', '-y', os.devnull])
            run([*common, '-cpu-used', '2', *(['-pass', '2', '-passlogfile', name] if passes == 2 else []), '-y', output])
            seconds = time.perf_counter() - start
            graph = ('[0:v]settb=AVTB,setpts=N/(30*TB)[d];[1:v]settb=AVTB,setpts=N/(30*TB)[r];'
                     f'[d][r]libvmaf=n_threads=4:feature=name=psnr|name=float_ssim:log_fmt=json:log_path={name}.json')
            run(['-v', 'error', '-i', output, '-i', ref, '-lavfi', graph, '-an', '-f', 'null', os.devnull])
            metrics = json.loads((dest / f'{name}.json').read_text())
            frame_scores = sorted(f['metrics']['vmaf'] for f in metrics['frames'])
            probe = json.loads(run(['-v', 'error', '-count_frames', '-select_streams', 'v:0', '-show_entries',
                                   'stream=nb_read_frames', '-of', 'json', output], ffprobe))
            assert int(probe['streams'][0]['nb_read_frames']) == len(frame_scores) == reference_frames
            result = dict(clip=ref.stem, passes=passes, crf=q, bytes=output.stat().st_size,
                          seconds=round(seconds, 3), frames=len(frame_scores),
                          vmaf=metrics['pooled_metrics']['vmaf']['mean'],
                          vmaf_p10=frame_scores[int(len(frame_scores) * .1)],
                          psnr_y=metrics['pooled_metrics']['psnr_y']['mean'],
                          ssim=metrics['pooled_metrics']['float_ssim']['mean'])
            results.append(result)
            (dest / 'results.json').write_text(json.dumps(results, indent=2))
            print(json.dumps(result), flush=True)
