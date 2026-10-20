#!/usr/bin/env python3
"""Print retained native-scene samples (JSON or gzip) as a Markdown table."""
import gzip
import json
import math
import sys

path = sys.argv[1]
with (gzip.open(path, "rt") if path.endswith(".gz") else open(path)) as stream:
    document = json.load(stream)
results = document["Results"]
print("| Layout | Event | p50 / p95 / max ms | MiB copied/event | Result |")
print("| --- | --- | ---: | ---: | --- |")
for result in results:
    samples = result["Samples"]
    assert len(samples) == 240
    values = sorted(sample["Total"] for sample in samples)
    limit = 16 if result["Scenario"] == "Mouse" else 33
    copied = sum(sample["Bytes"] for sample in samples) / len(samples) / 1048576
    print(f'| {result["Layout"]} | {result["Scenario"]} | '
          f'{values[119]:.2f} / {values[227]:.2f} / {values[-1]:.2f} | '
          f'{copied:.2f} | {"PASS" if values[-1] <= limit else "FAIL"} |')
cache = sorted(document["DimCacheMilliseconds"])
print(f'\n4K dim-cache p50 / p95 / max: {cache[math.ceil(len(cache)*.5)-1]:.2f} / '
      f'{cache[math.ceil(len(cache)*.95)-1]:.2f} / {cache[-1]:.2f} ms')
print(f'Correctness harness peak RSS: {document["PeakWorkingSetBytes"]/1048576:.1f} MiB')
