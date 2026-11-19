"""SARIF 2.1.0 derived report. Findings without verified paths omit locations."""
from pathlib import PurePosixPath
from urllib.parse import quote

def to_sarif(report):
    rules={}
    results=[]
    for f in report['findings']:
        rule=f['engine']+'/'+f['rule']
        rules.setdefault(rule,{'id':rule,'shortDescription':{'text':f['title']}})
        result={'ruleId':rule,'level':{'high':'error','error':'error','medium':'warning'}.get(f['priority'],'note'),
                'message':{'text':f['title']+'\n'+f['evidence']+'\nNext: '+f['action']},
                'partialFingerprints':{'forgescope/v1':f['id']},
                'properties':{'engine':f['engine'],'correlation':f.get('correlation')}}
        path=f.get('path')
        if path and not PurePosixPath(path).is_absolute() and '..' not in PurePosixPath(path).parts:
            physical={'artifactLocation':{'uri':quote(path,safe='/'),'uriBaseId':'%SRCROOT%'}}
            if isinstance(f.get('line'),int) and f['line']>0:physical['region']={'startLine':f['line']}
            result['locations']=[{'physicalLocation':physical}]
        if f.get('status') in {'new','existing'}:result['baselineState']='new' if f['status']=='new' else 'unchanged'
        results.append(result)
    return {'$schema':'https://json.schemastore.org/sarif-2.1.0.json','version':'2.1.0',
            'runs':[{'tool':{'driver':{'name':'ForgeScope','version':'0.2.0',
                      'informationUri':'https://github.com/brunnojob/forgescope','rules':list(rules.values())}},
                     'results':results}]}
