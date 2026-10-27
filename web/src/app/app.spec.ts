import { describe, expect, it } from 'vitest';
import { toParams } from './core/api.service';
import { toneFor } from './shared/display';

describe('toParams', () => {
  it('drops empty values and keeps the rest as strings', () => {
    const p = toParams({ a: 'x', b: undefined, c: '', d: false, e: 0, f: true });
    expect(p.keys()).toEqual(['a', 'e', 'f']);
    expect(p.get('e')).toBe('0');
  });
});

describe('toneFor', () => {
  it('maps statuses to tones', () => {
    expect(toneFor('Published')).toBe('good');
    expect(toneFor('ValidationFailed')).toBe('bad');
    expect(toneFor('InReview')).toBe('warn');
    expect(toneFor('Deprecated')).toBe('off');
  });
});
