"""Run a command, preserving its actual output and exit code."""
from pathlib import Path
import subprocess,sys,json,datetime
root=Path(__file__).resolve().parents[2]
label=sys.argv[1]; cmd=sys.argv[2:]; dest=root/'artifacts'/label; dest.parent.mkdir(parents=True,exist_ok=True)
r=subprocess.run(cmd,cwd=root/'part1',stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
dest.with_suffix('.log').write_text('$ '+ ' '.join(cmd)+'\n'+r.stdout+f'\nEXIT CODE: {r.returncode}\n')
dest.with_suffix('.json').write_text(json.dumps({'command':cmd,'exit_code':r.returncode,'utc':datetime.datetime.now(datetime.timezone.utc).isoformat()},indent=2))
print(r.stdout[-14000:]); print('EXIT CODE:',r.returncode)
sys.exit(r.returncode)
