"""Repeat published-app device listing without opening any capture device.

Usage: python3 tests/capture-startup-stress.py /path/to/pptcrunch --runs 200
Each process receives an invalid selection and must exit normally at the menu.
Run with camera/device-list access; sandboxed runs may see no devices.
"""
import argparse
import json
from pathlib import Path
import subprocess
import time

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('executable', type=Path)
parser.add_argument('--runs', type=int, default=100)
parser.add_argument('--output', type=Path, default=Path('artifacts/capture-startup-tests/stress.json'))
args = parser.parse_args()
if args.runs < 1:
    parser.error('--runs must be positive')
args.output.parent.mkdir(parents=True, exist_ok=True)
results = []
start = time.monotonic()
for index in range(args.runs):
    try:
        proc = subprocess.run([str(args.executable.resolve()), 'capture'], input='invalid\n',
                              text=True, capture_output=True, timeout=25)
        passed = proc.returncode == 1 and 'Invalid selection' in proc.stdout
        row = {'run': index + 1, 'exit': proc.returncode, 'passed': passed}
        if not passed:
            row['stdout'] = proc.stdout
            row['stderr'] = proc.stderr
    except subprocess.TimeoutExpired:
        row = {'run': index + 1, 'passed': False, 'timeout': True}
    results.append(row)
    if (index + 1) % 10 == 0:
        print(f'{index + 1}/{args.runs}: {sum(not r["passed"] for r in results)} failures', flush=True)
    args.output.write_text(json.dumps({'seconds': time.monotonic() - start, 'results': results}, indent=2) + '\n')
raise SystemExit(0 if all(r['passed'] for r in results) else 1)
