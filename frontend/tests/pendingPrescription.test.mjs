import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import { SourceTextModule, SyntheticModule, createContext } from 'node:vm';
import ts from 'typescript';

async function recover({ pages, resolved = [], current = null, patientFails = false }) {
  const calls = [];
  const context = createContext({});
  const dependencies = {
    '../api/consultations': { getCompletedConsultations: async (page) => { calls.push(page); return pages[page] ?? []; } },
    '../api/queue': { getCurrentPatient: async () => current },
    '../api/prescriptions': { getPrescriptionByQueueId: async (queue) => {
      if (resolved.includes(queue)) return { status: 'PENDING' };
      throw new Error('404');
    } },
    '../api/patients': { getPatient: async () => {
      if (patientFails) throw new Error('Unavailable');
      return { fullName: 'Synthetic Patient' };
    } },
  };
  const source = await readFile(new URL('../src/prescriptions/pendingPrescription.ts', import.meta.url), 'utf8');
  const javascript = ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText;
  const module = new SourceTextModule(javascript, { context });
  await module.link((specifier) => {
    const exports = dependencies[specifier];
    return new SyntheticModule(Object.keys(exports), function () {
      for (const [name, value] of Object.entries(exports)) this.setExport(name, value);
    }, { context });
  });
  await module.evaluate();
  return { result: await module.namespace.findPendingPrescriptionContext(), calls };
}

const visit = (id) => ({ consultationId: id, queueId: `queue-${id}`, patientId: `patient-${id}` });

test('recovers older unresolved visit when a newer visit has an outcome', async () => {
  const { result } = await recover({ pages: [[visit('A'), visit('B')]], resolved: ['queue-B'] });
  assert.equal(result.consultationId, 'A');
});

test('skips either recorded outcome and reaches an unresolved visit on the next page', async () => {
  const first = Array.from({ length: 50 }, (_, i) => visit(String(i)));
  const { result, calls } = await recover({ pages: [first, [visit('A')]], resolved: first.map((v) => v.queueId) });
  assert.equal(result.consultationId, 'A');
  assert.deepEqual(calls, [0, 1]);
});

test('chooses the oldest unresolved visit deterministically', async () => {
  const { result } = await recover({ pages: [[visit('A'), visit('B')]] });
  assert.equal(result.consultationId, 'A');
});

test('does not prescribe while queue completion is still pending', async () => {
  const { result } = await recover({ pages: [[visit('A'), visit('B')]], current: { queueId: 'queue-A' } });
  assert.equal(result.consultationId, 'B');
});

test('no completed work produces no recovery context', async () => {
  const { result } = await recover({ pages: [[]] });
  assert.equal(result, null);
});

test('patient name failure keeps identifiers available for recovery', async () => {
  const { result } = await recover({ pages: [[visit('A')]], patientFails: true });
  assert.equal(result.consultationId, 'A');
  assert.equal(result.patientName, undefined);
});
