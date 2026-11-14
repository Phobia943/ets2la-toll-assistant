import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location('audit', Path(__file__).with_name('Audit-TollHistory.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class TollHistoryTest(unittest.TestCase):
    def test_stale_status_cannot_assign_gate_or_prove_passage(self):
        with tempfile.TemporaryDirectory() as directory:
            row = dict(schema=1, utc='2026-11-14T12:00:00Z', t=0, sdkTimestamp=1, statusUtc='old',
                statusAgeSeconds=8, status={'utc':'old','gate':'7:0.0:0.0','selectedPayment':'Automatic'},
                x=0,y=0,z=0,gates=[dict(key='7:0.0:0.0',id=7,x=0,y=0,z=0,state=3)],active=True,paused=False)
            Path(directory,'drive.jsonl').write_text(json.dumps(row)+'\n', encoding='utf-8')
            result=module.audit(directory)
            event=result['recordings'][0]['gateObservations'][0]
            self.assertEqual(event['assignedRows'],0)
            self.assertEqual(event['nearRawRows'],1)
            self.assertEqual(event['payments'],[])
            self.assertFalse(result['fullPassValidatedAutomatically'])

    def test_duplicate_faults_use_absolute_time_and_copies_are_excluded(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            for name,time in [('first','2026-11-14T13:00:00+01:00'),('second','2026-11-14T12:00:00Z')]:
                (root/(name+'.json')).write_text(json.dumps(dict(utc=time,reason='fault')),encoding='utf-8')
            (root/'source').mkdir()
            (root/'source'/'fault.json').write_text(json.dumps(dict(utc='2026-11-14T11:00:00Z',reason='synthetic')),encoding='utf-8')
            result=module.audit(root)
            self.assertEqual(len(result['incidents']),1)
            self.assertEqual(len(result['incidents'][0]['sources']),2)
            self.assertIsNone(result['incidents'][0]['country'])

    def test_assigned_and_raw_same_row_do_not_double_count_faults(self):
        with tempfile.TemporaryDirectory() as directory:
            gate='7:0.0:0.0'
            row=dict(schema=1,utc='now',sdkTimestamp=1,statusUtc='stamp',statusAgeSeconds=0,
                status=dict(utc='stamp',gate=gate,faultBraking=True),x=0,y=0,z=0,
                gates=[dict(key=gate,id=7,x=0,y=0,z=0,state=0)])
            Path(directory,'drive.jsonl').write_text(json.dumps(row)+'\n',encoding='utf-8')
            event=module.audit(directory)['recordings'][0]['gateObservations'][0]
            self.assertEqual(event['assignedRows'],1)
            self.assertEqual(event['nearRawRows'],1)
            self.assertEqual(event['faultRows'],1)

if __name__=='__main__':
    unittest.main()
