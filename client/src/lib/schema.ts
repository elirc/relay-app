// Parse the connector's JSON Schema subset into flat form fields, and build a
// config JSON object back from field values. Mirrors the server's validator:
// object schema with typed `properties` and a `required` list.

export interface SchemaField {
  name: string;
  type: string;
  required: boolean;
}

interface JsonSchemaShape {
  type?: unknown;
  properties?: Record<string, { type?: unknown } | undefined>;
  required?: unknown;
}

export function parseSchemaFields(schemaJson: string): SchemaField[] {
  let schema: JsonSchemaShape;
  try {
    schema = JSON.parse(schemaJson) as JsonSchemaShape;
  } catch {
    return [];
  }
  if (!schema || typeof schema !== 'object') return [];
  if (typeof schema.type === 'string' && schema.type !== 'object') return [];

  const props = schema.properties && typeof schema.properties === 'object' ? schema.properties : {};
  const required = Array.isArray(schema.required) ? (schema.required as unknown[]) : [];

  return Object.entries(props).map(([name, def]) => ({
    name,
    type: def && typeof def === 'object' && typeof def.type === 'string' ? def.type : 'string',
    required: required.includes(name),
  }));
}

export function buildConfigJson(
  fields: SchemaField[],
  values: Record<string, string | boolean>,
): string {
  const obj: Record<string, unknown> = {};
  for (const field of fields) {
    const value = values[field.name];
    if (field.type === 'boolean') {
      obj[field.name] = Boolean(value);
      continue;
    }
    if (value === undefined || value === '') continue;
    if (field.type === 'integer' || field.type === 'number') {
      const n = Number(value);
      if (!Number.isNaN(n)) obj[field.name] = n;
    } else {
      obj[field.name] = value;
    }
  }
  return JSON.stringify(obj);
}
