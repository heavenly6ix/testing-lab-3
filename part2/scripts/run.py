from pathlib import Path
import subprocess,sys,json,datetime
root=Path(__file__).resolve().parents[1]
label=sys.argv[1]; cmd=sys.argv[2:]; out=root/'artifacts'/label; out.parent.mkdir(parents=True,exist_ok=True)
r=subprocess.run(cmd,cwd=root,capture_output=True,text=True)
out.with_suffix('.log').write_text('$ '+repr(cmd)+'\n'+r.stdout+r.stderr+'\nEXIT CODE: '+str(r.returncode)+'\n')
out.with_suffix('.json').write_text(json.dumps({'command':cmd,'exit_code':r.returncode,'utc':datetime.datetime.now(datetime.timezone.utc).isoformat()},indent=2))
print((r.stdout+r.stderr)[-12000:]);print('EXIT CODE:',r.returncode);sys.exit(r.returncode)
