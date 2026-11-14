"""Inventory saved toll evidence without treating missing records as successful drives.

Usage: python Audit-TollHistory.py INPUT_DIRECTORY OUTPUT_JSON
Only original top-level schema=1 SDK recordings and saved JSON diagnostics are read.
Copied source/build output, test runs, and independent synthetic probes are excluded.
Country is deliberately not guessed from coordinates, filenames, or model names.
"""
import json
import math
import sys
from datetime import datetime, timezone
from collections import Counter
from pathlib import Path

def finite(value):
    return isinstance(value, (int, float)) and math.isfinite(value)

def audit(root):
    root = Path(root)
    errors, recordings, incidents = [], [], {}
    inspected_json = 0
    excluded = {'bin', 'obj', 'source', '.git'}
    for file in sorted(root.rglob('*')):
        if not file.is_file() or file.suffix not in {'.json', '.jsonl'}:
            continue
        relative = file.relative_to(root)
        if any(p in excluded for p in relative.parts):
            continue
        if any(t in file.name.lower() for t in ['tests', 'probe', 'replay', 'mapping', 'analysis', 'validation']):
            continue
        if file.suffix == '.jsonl':
            # Every file is inspected for schema; non-drive JSONL is reported, never inferred.
            status = None
            count = 0
            gates = {}
            first = last = None
            sdk_previous = None
            sdk_advances = 0
            malformed = 0
            with file.open(encoding='utf-8-sig') as stream:
                for number, line in enumerate(stream, 1):
                    try:
                        row = json.loads(line)
                    except (ValueError, UnicodeError):
                        malformed += 1
                        continue
                    if not isinstance(row, dict) or row.get('schema') != 1:
                        continue
                    count += 1
                    first = first or row.get('utc')
                    last = row.get('utc')
                    stamp = row.get('sdkTimestamp')
                    advanced = stamp is not None and sdk_previous is not None and stamp > sdk_previous
                    sdk_advances += int(advanced)
                    sdk_previous = stamp
                    if isinstance(row.get('status'), dict):
                        status = row['status']
                    age = row.get('statusAgeSeconds')
                    current = status if status and finite(age) and -.5 <= age <= 1.5 and status.get('utc') == row.get('statusUtc') else None
                    keys = []
                    if current and current.get('gate'):
                        keys.append((current['gate'], 'assigned'))
                    # Near raw gates expose missing assignment; no successful passage is inferred.
                    for gate in row.get('gates') or []:
                        if not all(finite(gate.get(k)) for k in ('id','x','y','z')) or gate['id'] < 0:
                            continue
                        if not all(finite(row.get(k)) for k in ('x','y','z')):
                            continue
                        distance = sum((row[k]-gate[k])**2 for k in ('x','y','z'))**.5
                        if distance <= 150:
                            keys.append((gate.get('key') or str(gate), 'nearRaw'))
                    sources_by_key = {}
                    for key, source in keys:
                        sources_by_key.setdefault(key, set()).add(source)
                    for key, sources in sources_by_key.items():
                        event = gates.setdefault(key, {'key': key, 'assignedRows': 0, 'nearRawRows': 0, 'faultRows': 0,
                            'payments': set(), 'phases': set(), 'freshMovingRows': 0, 'minimumAssignedDistance': None})
                        event['assignedRows'] += int('assigned' in sources)
                        event['nearRawRows'] += int('nearRaw' in sources)
                        event['faultRows'] += int(bool(current and current.get('faultBraking')))
                        if current:
                            if current.get('selectedPayment'): event['payments'].add(current['selectedPayment'])
                            if current.get('phase'): event['phases'].add(current['phase'])
                            candidate = current.get('candidate') or {}
                            if 'assigned' in sources and candidate.get('Key') == key and finite(candidate.get('Distance')):
                                distance = candidate['Distance']
                                event['minimumAssignedDistance'] = min(distance, event['minimumAssignedDistance']) if event['minimumAssignedDistance'] is not None else distance
                        event['freshMovingRows'] += int(bool(advanced and row.get('active') and not row.get('paused') and not row.get('issues')))
            if count:
                for event in gates.values():
                    event['payments'] = sorted(event['payments'])
                    event['phases'] = sorted(event['phases'])
                recordings.append({'file': str(relative), 'samples': count, 'malformedLines': malformed,
                    'firstUtc': first, 'lastUtc': last, 'advancingSdkSamples': sdk_advances,
                    'gateObservations': list(gates.values()), 'country': None, 'fullPassValidated': False})
            elif malformed:
                errors.append({'file': str(relative), 'malformedLines': malformed})
            continue
        try:
            data = json.loads(file.read_text(encoding='utf-8-sig'))
            inspected_json += 1
        except (ValueError, UnicodeError) as ex:
            errors.append({'file': str(relative), 'error': str(ex)[:160]})
            continue
        if not isinstance(data, dict):
            continue
        fault = data.get('lastFault') if isinstance(data.get('lastFault'), dict) else data
        if not fault.get('reason') or not fault.get('utc'):
            continue
        try:
            parsed_utc = datetime.fromisoformat(fault['utc'].replace('Z','+00:00'))
            if parsed_utc.utcoffset() is None:
                raise ValueError('Missing timezone')
            normalized_utc = parsed_utc.astimezone(timezone.utc).isoformat()
        except (ValueError, TypeError):
            errors.append({'file': str(relative), 'error': 'Fault time is not a valid absolute timestamp'})
            continue
        ident = (normalized_utc, fault['reason'])
        event = incidents.setdefault(ident, {'utc': ident[0], 'reason': ident[1], 'sources': [], 'country': None,
            'speedKph': fault.get('speedKph'), 'tracked': fault.get('tracked'),
            'routeReconstructionAvailable': bool(fault.get('routePlan')), 'hasMotion': bool(fault.get('motion'))})
        event['sources'].append(str(relative))
    return {'scope': 'All readable saved SDK-drive recordings and fault JSON in supplied directory, subject to explicit exclusions',
        'inspectedJsonFiles': inspected_json, 'countryCoverageEstablished': False, 'fullPassValidatedAutomatically': False,
        'limitations': ['Unrecorded drives cannot be audited', 'Country was not saved and is not guessed',
            'Nearby raw gate may belong to a parallel road or opposite direction',
            'Passing a gate coordinate alone does not prove truck clearance, payment, continuous assistance or exit routing',
            'Stand tests and duplicate recordings may exist; raw gate observations are not counts of distinct drives'],
        'recordings': recordings, 'incidents': list(incidents.values()), 'readErrors': errors}

if __name__ == '__main__':
    result = audit(sys.argv[1])
    output = Path(sys.argv[2])
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding='utf-8')
    print(json.dumps({'recordings': len(result['recordings']), 'faultEvents': len(result['incidents']),
        'recordingsWithGates': sum(bool(r['gateObservations']) for r in result['recordings']),
        'faultReasons': dict(Counter(e['reason'] for e in result['incidents'])),
        'readErrors': len(result['readErrors']), 'countryCoverageEstablished': False}, ensure_ascii=False))
