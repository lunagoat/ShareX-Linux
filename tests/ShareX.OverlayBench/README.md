# Overlay render benchmark

Synthetic CPU-only Avalonia.Headless + Skia console benchmark of the existing
`RegionCaptureWindow`. No display server or desktop session is used.

From the repository root:

```bash
tests/ShareX.OverlayBench/run.sh /tmp/sharex-overlay-full.json
python3 tests/ShareX.OverlayBench/report.py /tmp/sharex-overlay-full.json > docs/status/native-overlay-bench.md
```

`--smoke` runs eight measured events in each scenario at 2560×1440 only; it is
for harness correctness and cannot generate an acceptance report. Full runs use
40 warmup + 240 measured events per case. Run without other builds/workloads.
The JSON contains every measured event and separate phase timings. `report.py`
also accepts gzip-compressed JSON. See the report for geometry, simulation limits,
phase definitions, checks and GPU inspection.

The benchmark deliberately bypasses native `OnOpened`, and uses reflection only
inside this test project to attach retained framebuffers and feed fake window
candidates. An Avalonia private ABI change fails the run. No application source
or global native-overlay configuration is changed.
