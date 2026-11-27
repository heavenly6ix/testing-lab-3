"""Reproduce the original heap measurement in an isolated temporary copy (expected 4 failures)."""
from pathlib import Path
import shutil, subprocess, tempfile, sys
root=Path(__file__).resolve().parents[2]; part=root/'part1'
with tempfile.TemporaryDirectory(prefix='part1-heap-baseline-') as temp:
 p=Path(temp)
 shutil.copytree(part/'src/BinaryMaxHeap',p/'src/BinaryMaxHeap',ignore=shutil.ignore_patterns('bin','obj'))
 t=p/'tests/BinaryMaxHeap.Tests'; t.mkdir(parents=True)
 shutil.copy2(part/'tests/BinaryMaxHeap.Tests/BinaryMaxHeap.Tests.csproj',t)
 source=root/'artifacts/coverage/before/source'
 shutil.copy2(source/'BinaryMaxHeap.cs',p/'src/BinaryMaxHeap/BinaryMaxHeap.cs')
 shutil.copy2(source/'BinaryMaxHeapTests.cs',t)
 cmd=[sys.executable,str(part/'scripts/run.py'),'coverage/reproduced-before/run','dotnet','test',str(t/'BinaryMaxHeap.Tests.csproj'),'--settings',str(source/'heap.runsettings'),'--collect','XPlat Code Coverage','--logger','trx','--results-directory',str(root/'artifacts/coverage/reproduced-before/results')]
 sys.exit(subprocess.run(cmd).returncode)
