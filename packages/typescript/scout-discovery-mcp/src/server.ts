import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js'
import { z } from 'zod'
import {
  listConnectors,
  inspectSampleSchema,
  summariseMetadata,
  validateConnectorManifest,
  validateExtendedConnectorManifest,
  readConnectorManifest,
  validateManifestSchemaCompatibility,
  summariseConnectors,
  produceMetadataQualityReport,
} from './tools.js'

export function createDiscoveryServer(): McpServer {
  const server = new McpServer({
    name: 'scout-discovery',
    version: '2.9.0',
  })

  server.tool(
    'scout_list_connectors',
    'Lists all registered KynticAI Scout connector plugins with public metadata. ' +
      'Returns connector type, display name, description, aliases, and supported data source kinds.',
    {},
    async () => ({
      content: [{ type: 'text' as const, text: JSON.stringify(listConnectors(), null, 2) }],
    }),
  )

  server.tool(
    'scout_inspect_sample_schema',
    'Inspects the configuration schema and sample configuration for a specific connector. ' +
      'Accepts a connector type identifier or alias.',
    { connectorType: z.string().describe('Connector type or alias to inspect (e.g. "sqlDatabase", "restApi", "mock").') },
    async ({ connectorType }) => ({
      content: [{ type: 'text' as const, text: JSON.stringify(inspectSampleSchema(connectorType), null, 2) }],
    }),
  )

  server.tool(
    'scout_summarise_metadata',
    'Summarises all available public metadata: registered connectors, ' +
      'semantic attribute keys, data source kinds, and connector capabilities.',
    {},
    async () => ({
      content: [{ type: 'text' as const, text: JSON.stringify(summariseMetadata(), null, 2) }],
    }),
  )

  server.tool(
    'scout_validate_connector_manifest',
    'Validates a connector manifest JSON against the expected KynticAI Scout connector structure. ' +
      'Checks required fields, schema structure, and sample configuration completeness.',
    { manifest: z.string().describe('JSON string of the connector manifest to validate.') },
    async ({ manifest }) => {
      let parsed: unknown
      try {
        parsed = JSON.parse(manifest)
      } catch {
        return {
          content: [
            {
              type: 'text' as const,
              text: JSON.stringify(
                { isValid: false, errors: ['manifest is not valid JSON.'], warnings: [] },
                null,
                2,
              ),
            },
          ],
        }
      }
      return {
        content: [
          { type: 'text' as const, text: JSON.stringify(validateConnectorManifest(parsed), null, 2) },
        ],
      }
    },
  )

  server.tool(
    'scout_validate_connector_manifest_v2',
    'Validates an extended connector manifest against the full KynticAI Scout public schema. ' +
      'Checks connector ID format, semver version, supported source types, required config fields, ' +
      'safe metadata fields (rejects credential/PII leaks), and sample entity mappings.',
    {
      manifest: z.string().describe('JSON string of the extended connector manifest to validate.'),
      knownConnectorIds: z
        .string()
        .optional()
        .describe('Optional comma-separated list of existing connector IDs to check for duplicates.'),
    },
    async ({ manifest, knownConnectorIds }) => {
      let parsed: unknown
      try {
        parsed = JSON.parse(manifest)
      } catch {
        return {
          content: [
            {
              type: 'text' as const,
              text: JSON.stringify(
                { isValid: false, errors: ['manifest is not valid JSON.'], warnings: [] },
                null,
                2,
              ),
            },
          ],
        }
      }
      const options = knownConnectorIds !== undefined
        ? { knownConnectorIds: knownConnectorIds.split(',').map((id) => id.trim()) }
        : undefined
      return {
        content: [
          {
            type: 'text' as const,
            text: JSON.stringify(validateExtendedConnectorManifest(parsed, options), null, 2),
          },
        ],
      }
    },
  )

  server.tool(
    'scout_read_connector_manifest',
    'Reads the full connector manifest for a given connector type or alias. ' +
      'Returns all fields including configurationSchema, sampleConfiguration, ' +
      'supportedCapabilities, aliases, and description.',
    { connectorType: z.string().describe('Connector type or alias to read (e.g. "sqlDatabase", "restApi", "csvUpload").') },
    async ({ connectorType }) => ({
      content: [{ type: 'text' as const, text: JSON.stringify(readConnectorManifest(connectorType), null, 2) }],
    }),
  )

  server.tool(
    'scout_validate_manifest_schema_compatibility',
    'Validates that a connector manifest\'s configurationSchema is compatible with a ' +
      'provided schema. Checks field presence, type alignment, and required-field coverage ' +
      'in the sample configuration.',
    {
      manifest: z.string().describe('JSON string of the connector manifest to check.'),
      schema: z.string().describe('JSON string of the target schema to validate against.'),
    },
    async ({ manifest, schema }) => {
      let parsedManifest: unknown
      let parsedSchema: unknown
      try {
        parsedManifest = JSON.parse(manifest)
      } catch {
        return {
          content: [
            {
              type: 'text' as const,
              text: JSON.stringify(
                { isCompatible: false, issues: ['manifest is not valid JSON.'], compatible: [] },
                null,
                2,
              ),
            },
          ],
        }
      }
      try {
        parsedSchema = JSON.parse(schema)
      } catch {
        return {
          content: [
            {
              type: 'text' as const,
              text: JSON.stringify(
                { isCompatible: false, issues: ['schema is not valid JSON.'], compatible: [] },
                null,
                2,
              ),
            },
          ],
        }
      }
      return {
        content: [
          {
            type: 'text' as const,
            text: JSON.stringify(
              validateManifestSchemaCompatibility(parsedManifest, parsedSchema),
              null,
              2,
            ),
          },
        ],
      }
    },
  )

  server.tool(
    'scout_summarise_connectors',
    'Produces a detailed summary of all available connectors including per-connector ' +
      'field counts, capability coverage matrix, data source kind distribution, and alias totals.',
    {},
    async () => ({
      content: [{ type: 'text' as const, text: JSON.stringify(summariseConnectors(), null, 2) }],
    }),
  )

  server.tool(
    'scout_metadata_quality_report',
    'Produces a local metadata quality report by running the Scout metadata audit runner ' +
      'against a connector manifest and optional sample records. Returns readiness scores, ' +
      'field classifications, warnings, and recommendations.',
    {
      manifest: z.string().describe('JSON string of the connector manifest to audit.'),
      sampleRecords: z
        .string()
        .optional()
        .describe(
          'Optional JSON string of an array of sample records. ' +
          'Each record should have externalUserId (string), optional observedAtUtc (string), and payload (object).',
        ),
    },
    async ({ manifest, sampleRecords }) => {
      let parsedManifest: unknown
      try {
        parsedManifest = JSON.parse(manifest)
      } catch {
        return {
          content: [
            {
              type: 'text' as const,
              text: JSON.stringify(
                { error: 'manifest is not valid JSON.' },
                null,
                2,
              ),
            },
          ],
        }
      }
      let parsedRecords: unknown
      if (sampleRecords !== undefined) {
        try {
          parsedRecords = JSON.parse(sampleRecords)
        } catch {
          return {
            content: [
              {
                type: 'text' as const,
                text: JSON.stringify(
                  { error: 'sampleRecords is not valid JSON.' },
                  null,
                  2,
                ),
              },
            ],
          }
        }
      }
      return {
        content: [
          {
            type: 'text' as const,
            text: JSON.stringify(
              produceMetadataQualityReport(parsedManifest, parsedRecords),
              null,
              2,
            ),
          },
        ],
      }
    },
  )

  return server
}
