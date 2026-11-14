#!/usr/bin/env python3
"""One Linux runner: exercise historical placement, then nested frames and explicit zoom against real PTYs."""
from pathlib import Path
import runpy
import tempfile
from smoke_nested_frames import verify

here=Path(__file__).resolve().parent
original=runpy.run_path(str(here/'placement_focus_acceptance.py'))
if __name__=='__main__':
    with tempfile.TemporaryDirectory() as d:original['run'](Path(d))
    with tempfile.TemporaryDirectory() as d:verify(Path(d),original['SERVER'],original['CLI'])
