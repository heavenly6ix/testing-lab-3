"""Summarize real Cobertura/OpenCover/TRX artifacts without estimating metrics."""
from pathlib import Path
import xml.etree.ElementTree as E
import json
root=Path(__file__).resolve().parents[2]; a=root/'artifacts'
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
def trx(path):
 r=E.parse(path).getroot(); c=r.find('t:ResultSummary/t:Counters',ns)
 return {'file':str(path.relative_to(root)), 'counters':c.attrib,
         'skipped':sum(t.get('outcome')=='NotExecuted' for t in r.findall('.//t:UnitTestResult',ns)),
         'tests':[{'name':t.get('testName'),'outcome':t.get('outcome')} for t in r.findall('.//t:UnitTestResult',ns)]}
result={}
for stage in ['before','after']:
 path=next((a/'coverage'/stage).rglob('coverage.cobertura.xml')); r=E.parse(path).getroot()
 op=E.parse(next((a/'coverage'/stage).rglob('coverage.opencover.xml'))).getroot()
 summary=op.find('Summary')
 result[stage]={'cobertura':r.attrib,'opencover':summary.attrib,
  'classes':[c.attrib for c in r.findall('.//class')],
  'uncovered_lines':sorted({int(x.get('number')) for x in r.findall('.//class/lines/line') if x.get('hits')=='0'}),
  'trx':[trx(p) for p in (a/'coverage'/stage).rglob('*.trx')]}
(a/'coverage/summary.json').write_text(json.dumps(result,ensure_ascii=False,indent=2))
rows=['# BinaryMaxHeap: фактическое покрытие','',
'Coverlet collector 6.0.4; Debug, net10.0; фильтр `[BinaryMaxHeap]Heaps.BinaryMaxHeap*`. Вспомогательный ArrayList и тестовый код в знаменатель не входят.','',
'| Метрика | До дополнения | После дополнения |','|---|---:|---:|']
for key,title in [('line-rate','Line coverage'),('branch-rate','Branch coverage')]:
 rows.append(f'| {title} | {float(result["before"]["cobertura"][key])*100:.2f}% | {float(result["after"]["cobertura"][key])*100:.2f}% |')
for key,title in [('lines-covered','Покрыто строк'),('lines-valid','Всего строк'),('branches-covered','Покрыто ветвей'),('branches-valid','Всего ветвей')]:
 rows.append(f'| {title} | {result["before"]["cobertura"][key]} | {result["after"]["cobertura"][key]} |')
for key,title in [('numSequencePoints','Sequence points'),('visitedSequencePoints','Visited sequence points'),('numMethods','Methods'),('visitedMethods','Visited methods'),('maxCyclomaticComplexity','Max cyclomatic complexity')]:
 rows.append(f'| OpenCover: {title} | {result["before"]["opencover"][key]} | {result["after"]["opencover"][key]} |')
for key,title in [('total','Количество тестов, включая skipped'),('passed','Passed'),('failed','Failed')]:
 rows.append(f'| {title} | {result["before"]["trx"][0]["counters"][key]} | {result["after"]["trx"][0]["counters"][key]} |')
rows.append(f'| Skipped (по UnitTestResult) | {result["before"]["trx"][0]["skipped"]} | {result["after"]["trx"][0]["skipped"]} |')
rows.extend(['','Исходный полный комплект не компилировался. «До» — исходный BinaryMaxHeap.cs и исходные тесты с единственной необходимой адаптацией: 5 вызовов отсутствующего ToMinHeap заменены явными пропусками. Четыре ошибочных assert ещё не исправлены, 4 теста падают; coverage при этом реально собран. Снимки использованных файлов находятся в before/source. Новые тесты и исправления кучи в этот замер не попали.','',
'Покрытие до дополнения уже 100%, поэтому роста процентов нет. Ни одна инструментированная строка/ветвь не была непокрытой. Непокрыты были сценарии и проверяемые свойства, перечисленные в ../../part1/reviews/HEAP_TEST_REVIEW.md. Это не противоречие: branch coverage учитывает результаты отдельных условий, а не все сочетания состояний или качество assert. Исходный тест Initialize(empty) исполнял ветвь, но ожидал дефектное поведение.','',
'Знаменатели до/после различаются из-за минимальных исправлений реализации. Покрытие null guard внутри библиотечного ArgumentNullException.ThrowIfNull не считается ветвлением BinaryMaxHeap, поскольку оно находится в .NET. Это не оценка всего проекта BinaryMaxHeap с вспомогательными классами. В TRX агрегированный notExecuted у адаптера равен 0, хотя пять UnitTestResult имеют outcome=NotExecuted; skipped посчитаны по отдельным результатам и совпадают с консольным итогом. Все дополнительные метрики инструмента сохранены в исходном OpenCover и summary.json.'])
(a/'coverage/SUMMARY.md').write_text('\n'.join(rows)+'\n')
print('\n'.join(rows[:23]))
