import { describe, it, expect } from 'vitest';
import { parseSchemaFields, buildConfigJson } from './schema';

describe('parseSchemaFields', () => {
  it('extracts fields with types and required flags', () => {
    const fields = parseSchemaFields(
      '{"type":"object","properties":{"channel":{"type":"string"},"pinned":{"type":"boolean"}},"required":["channel"]}',
    );
    expect(fields).toEqual([
      { name: 'channel', type: 'string', required: true },
      { name: 'pinned', type: 'boolean', required: false },
    ]);
  });

  it('returns no fields for an empty or malformed schema', () => {
    expect(parseSchemaFields('{}')).toEqual([]);
    expect(parseSchemaFields('not json')).toEqual([]);
  });
});

describe('buildConfigJson', () => {
  it('coerces values by declared type and omits empties', () => {
    const fields = parseSchemaFields(
      '{"type":"object","properties":{"seconds":{"type":"integer"},"note":{"type":"string"}},"required":["seconds"]}',
    );
    const json = buildConfigJson(fields, { seconds: '30', note: '' });
    expect(JSON.parse(json)).toEqual({ seconds: 30 });
  });

  it('always includes booleans', () => {
    const fields = parseSchemaFields(
      '{"type":"object","properties":{"pinned":{"type":"boolean"}}}',
    );
    expect(JSON.parse(buildConfigJson(fields, { pinned: true }))).toEqual({ pinned: true });
    expect(JSON.parse(buildConfigJson(fields, {}))).toEqual({ pinned: false });
  });
});
