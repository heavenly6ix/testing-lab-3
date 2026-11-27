from pathlib import Path
import json,xml.etree.ElementTree as E,shutil
root=Path(__file__).resolve().parents[1]; a=root/'artifacts';dest=a/'coverage';dest.mkdir(exist_ok=True)
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
totals={key:{'passed':0,'failed':0,'skipped':0,'total':0,'tests':[]} for key in ['unit','api','infrastructure']}
for file in (a/'final/tests').glob('*.trx'):
 r=E.parse(file).getroot();definitions={u.get('id'):u.find('t:TestMethod',ns).get('className') for u in r.findall('.//t:UnitTest',ns)}
 for test in r.findall('.//t:UnitTestResult',ns):
  cls=definitions.get(test.get('testId'),'');kind='unit' if cls.startswith('Subscriptions.Application.Tests') else 'infrastructure' if cls.endswith('InfrastructureTests') else 'api'
  t=totals[kind];outcome=test.get('outcome');t['total']+=1;t[{'Passed':'passed','Failed':'failed','NotExecuted':'skipped'}[outcome]]+=1;t['tests'].append({'class':cls,'name':test.get('testName'),'outcome':outcome})
for name in ['cobertura','opencover']:
 raw=next((a/'final/coverage').glob('*/coverage.'+name+'.xml'));shutil.copy2(raw,dest/('coverage.'+name+'.xml'))
r=E.parse(dest/'coverage.cobertura.xml').getroot();coverage={'line_percent':100*int(r.get('lines-covered'))/int(r.get('lines-valid')),'branch_percent':100*int(r.get('branches-covered'))/int(r.get('branches-valid')),'raw':r.attrib,'statement':'statement coverage not directly available','modules':{}}
for cl in r.findall('.//class'):
 if cl.get('name') in ['Subscriptions.Application.AnalyticsService','Subscriptions.Application.ForecastService']:
  coverage['modules'][cl.get('name').split('.')[-1]]={'line_percent':100*float(cl.get('line-rate')),'branch_percent':100*float(cl.get('branch-rate'))}
coverage['opencover_summary']=E.parse(dest/'coverage.opencover.xml').getroot().find('Summary').attrib
smoke=json.loads((a/'final/smoke/summary.json').read_text())
assert all(t['total']>0 and t['failed']==0 and t['skipped']==0 for t in totals.values())
assert coverage['line_percent']>90 and coverage['branch_percent']>80
assert all(v['branch_percent']>=90 for v in coverage['modules'].values()) and len(coverage['modules'])==2
assert smoke['failed']==0 and smoke['requests']>=22
for cmd in ['restore','build','test','smoke-run','coverage-run','swagger-ui','html']:
 assert json.loads((a/'final'/f'{cmd}.json').read_text())['exit_code']==0,cmd
summary={'tests':totals,'coverage':coverage,'smoke':{k:v for k,v in smoke.items() if k!='results'},'base_url':smoke['base_url']}
(a/'final/summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2))
(dest/'summary.json').write_text(json.dumps(coverage,ensure_ascii=False,indent=2))
lines=['# Фактический coverage бизнес-логики','',f'Line: {coverage["line_percent"]:.2f}% ({r.get("lines-covered")}/{r.get("lines-valid")}).',f'Branch: {coverage["branch_percent"]:.2f}% ({r.get("branches-covered")}/{r.get("branches-valid")}).','Statement/instruction: **statement coverage not directly available**, значение не подменяется line.','', '| Module | Line | Branch |','|---|---:|---:|']
for key,v in coverage['modules'].items():lines.append(f'| {key} | {v["line_percent"]:.2f}% | {v["branch_percent"]:.2f}% |')
lines+=['','Источник: artifacts/final/coverage, реальные unit tests; filter/exclusions — ../../coverage.runsettings и ../../docs/coverage-method.md. OpenCover summary сохранён в summary.json. HTML: html/index.html. Общая цель >80% ветвей подтверждена, >90% statements отдельной метрикой НЕ подтверждено. Q02 каждого модуля подтверждён.']
(dest/'SUMMARY.md').write_text('\n'.join(lines)+'\n')
print(json.dumps({'tests':{k:{n:v for n,v in t.items() if n!='tests'} for k,t in totals.items()},'coverage':coverage,'smoke':summary['smoke']},ensure_ascii=False,indent=2))
