"""Real curl smoke, not TestServer. Secrets stay in memory; saved evidence is explicitly redacted."""
from pathlib import Path
import argparse,subprocess,json,uuid,datetime,shlex
p=argparse.ArgumentParser();p.add_argument('--base',default='http://127.0.0.1:5087');p.add_argument('--out',default='artifacts/12-smoke');args=p.parse_args()
out=Path(args.out);out.mkdir(parents=True,exist_ok=True);rows=[];token=None
password='Smoke-'+uuid.uuid4().hex;email='smoke-'+uuid.uuid4().hex+'@example.test'
def redact(value):
 if isinstance(value,dict):return {k:('[REDACTED]' if k.lower() in ['accesstoken','refreshtoken','token','password'] else redact(v)) for k,v in value.items()}
 if isinstance(value,list):return [redact(v) for v in value]
 return value
def request(label,rid,method,path,expected,body=None,key=None,auth=True):
 global token
 headers=['Content-Type: application/json']
 if auth and token:headers.append('Authorization: Bearer '+token)
 if key:headers.append('Idempotency-Key: '+key)
 config=['silent','show-error','include','max-time = 20','request = '+json.dumps(method),'url = '+json.dumps(args.base+path)]
 config+=['header = '+json.dumps(h) for h in headers]
 if body is not None:config.append('data = '+json.dumps(json.dumps(body,separators=(',',':'))))
 result=subprocess.run(['curl','--config','-'],input='\n'.join(config)+'\n',capture_output=True,text=True)
 if result.returncode:raise RuntimeError('curl failed: '+result.stderr)
 head,sep,raw=result.stdout.partition('\n\n');status=int(head.splitlines()[0].split()[1]);data=json.loads(raw) if raw.strip() else None
 response_headers=dict(line.split(': ',1) for line in head.splitlines()[1:] if ': ' in line)
 correlation=next((v for k,v in response_headers.items() if k.lower()=='x-correlation-id'),None)
 safe_headers=[('Authorization: Bearer $ACCESS_TOKEN' if h.startswith('Authorization:') else h) for h in headers]
 safe_body=redact(body)
 cmd='curl -i -X '+method+' '+shlex.quote(args.base+path)+''.join(' -H '+shlex.quote(h) for h in safe_headers)
 if body is not None:cmd+=' --data '+shlex.quote(json.dumps(safe_body,separators=(',',':')))
 entry={'label':label,'requirement':rid,'request':cmd,'expected':expected,'actual_http':status,'response_headers':response_headers,'actual_result':redact(data),'passed':status==expected and correlation is not None}
 rows.append(entry);(out/(f'{len(rows):02d}-{label}.json')).write_text(json.dumps(entry,ensure_ascii=False,indent=2));print(f'{label}: expected {expected}, actual {status}, correlation={bool(correlation)}')
 if not entry['passed']:raise AssertionError(label+': unexpected actual status/headers (see redacted artifact)')
 return data
try:
 request('health','R05','GET','/api/v1/health',200,auth=False)
 request('register','F01','POST','/api/v1/auth/register',201,{'email':email,'password':password},auth=False)
 request('duplicate','F01','POST','/api/v1/auth/register',409,{'email':' '+email.upper()+' ','password':password},auth=False)
 pair=request('login','F02','POST','/api/v1/auth/login',200,{'email':email,'password':password},auth=False);token=pair['accessToken']
 request('invalid-login','F02','POST','/api/v1/auth/login',401,{'email':email,'password':'wrong'},auth=False)
 request('unauthorized','Q05','GET','/api/v1/subscriptions',401,auth=False)
 today=datetime.datetime.now(datetime.timezone.utc).date();day=today.isoformat()
 sub=request('create-subscription','F06','POST','/api/v1/subscriptions',201,{'name':'Music','amount':10,'currency':'RUB','billingPeriod':'monthly','category':'Media','startDate':'2020-01-01','nextPaymentDate':day});sid=sub['id']
 request('get','F07','GET',f'/api/v1/subscriptions/{sid}',200)
 request('list','F07','GET','/api/v1/subscriptions?status=active&category=Media&pageSize=20',200)
 request('update','F08','PATCH',f'/api/v1/subscriptions/{sid}',200,{'name':'Updated Music','amount':12})
 pay=request('create-payment','F11','POST',f'/api/v1/subscriptions/{sid}/payments',201,{'amount':10,'currency':'RUB','date':day,'status':'successful'},key='smoke-key');pid=pay['id']
 replay=request('replay','F11','POST',f'/api/v1/subscriptions/{sid}/payments',201,{'amount':10.00,'currency':'RUB','date':day,'status':'successful'},key='smoke-key');assert replay==pay
 request('conflict','F11','POST',f'/api/v1/subscriptions/{sid}/payments',409,{'amount':11,'currency':'RUB','date':day,'status':'successful'},key='smoke-key')
 request('history','F12','GET',f'/api/v1/payments?subscriptionId={sid}&from={day}&to={day}',200)
 expenses=request('analytics','F14','GET',f'/api/v1/analytics/expenses?from={day}&to={day}',200);assert expenses['totals']['RUB']==10
 request('empty-analytics','F14','GET','/api/v1/analytics/expenses?from=2001-01-01&to=2001-01-01',200)
 forecast=request('forecast','F15','GET','/api/v1/analytics/forecast',200);assert any(x['subscriptionId']==sid for x in forecast['items'])
 request('pause','F09','POST',f'/api/v1/subscriptions/{sid}/pause',200)
 request('resume','F09','POST',f'/api/v1/subscriptions/{sid}/resume',200)
 request('correct','F13','PATCH',f'/api/v1/payments/{pid}',200,{'amount':15})
 request('void','F13','POST',f'/api/v1/payments/{pid}/void',200)
 request('audit','F13','GET',f'/api/v1/payments/{pid}/audit',200)
 empty=request('void-excluded','F14','GET',f'/api/v1/analytics/expenses?from={day}&to={day}',200);assert empty['totals']['RUB']==0
 request('cancel','F09','POST',f'/api/v1/subscriptions/{sid}/cancel',200)
 request('cancelled-resume','F09','POST',f'/api/v1/subscriptions/{sid}/resume',409)
 request('archive','F10','DELETE',f'/api/v1/subscriptions/{sid}',204)
 request('archived-history','F10','GET',f'/api/v1/payments?subscriptionId={sid}',200)
 other='other-'+uuid.uuid4().hex+'@example.test'
 request('register-other','F01','POST','/api/v1/auth/register',201,{'email':other,'password':password},auth=False)
 pair=request('login-other','F02','POST','/api/v1/auth/login',200,{'email':other,'password':password},auth=False);token=pair['accessToken']
 request('foreign-resource','Q05','GET',f'/api/v1/subscriptions/{sid}',404)
 request('openapi','N10','GET','/swagger/v1/swagger.json',200,auth=False)
 # Swagger HTML is checked with curl separately; this runner intentionally parses JSON API responses.
finally:
 summary={'base_url':args.base,'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'requests':len(rows),'passed':sum(r['passed'] for r in rows),'failed':sum(not r['passed'] for r in rows),'secrets_redacted':True,'results':rows}
 (out/'summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2))
 lines=['# Реальные smoke-тесты curl','',f'Base URL: {args.base}. Requests: {len(rows)}. Secrets deliberately redacted; status, headers and nonsensitive JSON are actual curl results.','', '| ID | Request | Expected | Actual HTTP | Actual Result |','|---|---|---:|---:|---|']
 for i,r in enumerate(rows,1):lines.append(f'| {r["requirement"]} / {r["label"]} | `{r["request"].replace("|","&#124;")}` | {r["expected"]} | {r["actual_http"]} | [{"PASS" if r["passed"] else "FAIL"}]({i:02d}-{r["label"]}.json) |')
 (out/'smoke-table.md').write_text('\n'.join(lines)+'\n')
 print(f'Real curl requests: {len(rows)}; status checks passed: {summary["passed"]}; failed: {summary["failed"]}')
