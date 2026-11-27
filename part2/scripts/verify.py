"""Clean, restore, build and test only part2; stop immediately on a failed command."""
from pathlib import Path
import subprocess,sys,shutil,json
root=Path(__file__).resolve().parents[1]
removed=[]
for name in ['src','tests']:
 for p in list((root/name).rglob('*')):
  if p.is_dir() and p.name in ['bin','obj']:
   removed.append(str(p.relative_to(root)));shutil.rmtree(p)
(root/'artifacts/final').mkdir(parents=True,exist_ok=True)
(root/'artifacts/final/clean-before-build.json').write_text(json.dumps(removed,indent=2))
for label,args in [('restore',['dotnet','restore']),('build',['dotnet','build']),('test',['dotnet','test','--logger','trx','--results-directory','artifacts/final/tests'])]:
 code=subprocess.run([sys.executable,str(root/'scripts/run.py'),'final/'+label,*args],cwd=root).returncode
 if code:sys.exit(code)
