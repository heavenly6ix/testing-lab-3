"""Capture the real Kestrel OpenAPI response and verify Bearer behavior without storing tokens."""
from pathlib import Path
import subprocess,sys,tempfile,time,urllib.request,urllib.error,json,uuid,os
root=Path(__file__).resolve().parents[2]; out=Path(__file__).resolve().parent; mode=sys.argv[1]
url='http://127.0.0.1:5091'; result={'mode':mode,'url':url,'checks':[]}
def call(method,path,body=None,token=None):
 headers={'Content-Type':'application/json'}
 if token:headers['Authorization']='Bearer '+token
 request=urllib.request.Request(url+path,data=json.dumps(body).encode() if body is not None else None,headers=headers,method=method)
 try:
  with urllib.request.urlopen(request,timeout=10) as response:return response.status,response.read()
 except urllib.error.HTTPError as error:return error.code,error.read()
with tempfile.TemporaryDirectory(prefix='subscriptions-swagger-') as temp:
 env=os.environ.copy();env.update(ASPNETCORE_ENVIRONMENT='Development',ASPNETCORE_URLS=url,Database__Path=str(Path(temp)/'test.db'))
 with (out/(mode+'-server.log')).open('w') as log:
  cmd=['dotnet','run','--project','src/Subscriptions.Api','--no-build','--no-launch-profile']
  server=subprocess.Popen(cmd,cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT)
  try:
   for _ in range(100):
    if server.poll() is not None:raise RuntimeError('Backend exited; see server log')
    try:
     status,raw=call('GET','/swagger/v1/swagger.json');break
    except urllib.error.URLError:time.sleep(.1)
   else:raise RuntimeError('Backend did not start')
   assert status==200
   document=json.loads(raw);(out/(mode+'-swagger.json')).write_bytes(raw)
   protected=[];public=[]
   for path,methods in document['paths'].items():
    for method,operation in methods.items():
     if method not in ['get','post','patch','delete']:continue
     security=operation.get('security',[])
     item={'method':method,'path':path,'security':security}
     (public if path.startswith('/api/v1/auth/') or path=='/api/v1/health' else protected).append(item)
   result.update(protected_operations=protected,public_operations=public,bearer_scheme=document['components']['securitySchemes']['Bearer'])
   assert len(protected)==15 and len(public)==7
   if mode=='after':
    assert all(x['security']==[{'Bearer':[]}] for x in protected)
    assert all(x['security']==[] for x in public)
    assert result['bearer_scheme']['type']=='http' and result['bearer_scheme']['scheme']=='bearer'
    assert not document.get('security')
    status,_=call('GET','/api/v1/health');assert status==200;result['checks'].append({'request':'GET health without token','actual_http':status})
    payload={'name':'Swagger regression','amount':10,'currency':'RUB','billingPeriod':'monthly','category':'Test','startDate':'2020-01-01','nextPaymentDate':'2026-11-26'}
    status,_=call('POST','/api/v1/subscriptions',payload);assert status==401;result['checks'].append({'request':'POST subscriptions without Authorization','actual_http':status})
    credentials={'email':'swagger-'+uuid.uuid4().hex+'@example.test','password':uuid.uuid4().hex}
    status,_=call('POST','/api/v1/auth/register',credentials);assert status==201
    status,body=call('POST','/api/v1/auth/login',credentials);assert status==200;result['checks'].append({'request':'POST login without token','actual_http':status})
    access=json.loads(body)['accessToken']
    status,_=call('POST','/api/v1/subscriptions',payload,access);assert status==201;result['checks'].append({'request':'POST subscriptions with Authorization: Bearer [REDACTED]','actual_http':status})
   (out/(mode+'-summary.json')).write_text(json.dumps(result,ensure_ascii=False,indent=2))
   print(json.dumps({'mode':mode,'protected':len(protected),'public':len(public),'sample_security':protected[0]['security'],'checks':result['checks']},ensure_ascii=False,indent=2))
  finally:
   server.terminate()
   try:server.wait(timeout=10)
   except subprocess.TimeoutExpired:server.kill();server.wait()
